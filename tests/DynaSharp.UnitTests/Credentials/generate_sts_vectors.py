"""Generates sts_vectors.json, the expected requests and responses of AssumeRoleWithWebIdentity, with botocore.

The form body and the XML parsing of WebIdentityCredentialsProvider must not be pinned to themselves, so every expectation here comes
from the code that real SDK users run: a botocore STS client, unsigned as botocore's own AssumeRoleWithWebIdentityCredentialFetcher makes
it, with the query protocol of sts/2011-06-15/service-2.json. A hook on before-send catches the request and answers with a canned response, so
nothing leaves the machine and the serializer, the endpoint resolution and the parser all run.

- requests: what the client sends for a role, a session name and a token.
- endpoints: the URL for a region and each value of AWS_STS_REGIONAL_ENDPOINTS, over every region botocore knows for STS.
- configured_endpoints: the URL when AWS_ENDPOINT_URL_STS or AWS_ENDPOINT_URL is set, beside each other and beside what they go ahead of.
- responses: what the parser reads from a successful XML body.
- errors: the error code the parser reads from a failed body, and whether botocore's standard retry mode would retry it.
- session_names: names checked against the model of roleSessionNameType. AWS applies the pattern with an ASCII \\w, hence re.ASCII.
- modeled_errors: the codes the model lists for the operation, with their status.

Setup (outside the repository):
    python -m venv <dir>
    <dir>/Scripts/pip install botocore        (bin/pip on Linux and macOS)

Run (from any directory):
    <dir>/Scripts/python generate_sts_vectors.py

Writes sts_vectors.json next to this script. The file is an embedded resource of the test project. The inputs of every case are defined
here only, so the C# side cannot drift from what botocore did.
"""

import datetime
import io
import json
import os
import re
from unittest import mock

import botocore
import botocore.awsrequest
import botocore.config
import botocore.exceptions
import botocore.loaders
import botocore.retries.standard
import botocore.session
from botocore import UNSIGNED

OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sts_vectors.json")

NAMESPACE = "https://sts.amazonaws.com/doc/2011-06-15/"
ROLE = "arn:aws:iam::123456789012:role/orders"
SESSION = "dynasharp-1793000000"

# A JWT of the shape the EKS token controller issues, with a made-up payload and signature.
JWT = (
    "eyJhbGciOiJSUzI1NiIsImtpZCI6ImV4YW1wbGUifQ."
    "eyJpc3MiOiJodHRwczovL29pZGMuZWtzLmV1LXdlc3QtMS5hbWF6b25hd3MuY29tL2lkL0VYQU1QTEUiLCJhdWQiOlsic3RzLmFtYXpvbmF3cy5jb20iXX0."
    "c2lnbmF0dXJlLW9mLXRoZS1leGFtcGxlLXRva2VuLW5vdC1yZWFsLV8tXw"
)

# AWS documentation example credentials.
ACCESS_KEY = "ASIAIOSFODNN7EXAMPLE"
SECRET_KEY = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
TOKEN = "IQoJb3JpZ2luX2VjEEM/Example+Token=="
EXPIRATION = "2026-10-31T18:34:56Z"

# What a refresh gets: other credentials, an hour later.
REFRESHED_ACCESS_KEY = "ASIAI44QH8DHBEXAMPLE"
REFRESHED_SECRET_KEY = "je7MtGbClwBF/2Zp9Utk/h3yCo8nvbEXAMPLEKEY"
REFRESHED_TOKEN = "AQoDYXdzEJr/Refreshed+Token=="
REFRESHED_EXPIRATION = "2026-10-31T19:34:56Z"

STS_VARIABLE = "AWS_ENDPOINT_URL_STS"
GLOBAL_VARIABLE = "AWS_ENDPOINT_URL"
MODE_VARIABLE = "AWS_STS_REGIONAL_ENDPOINTS"
LOOPBACK = "http://127.0.0.1:4566"
OTHER_LOOPBACK = "http://127.0.0.1:4567"

# Each case: name, role ARN, session name, token.
REQUESTS = [
    ("IrsaJwt", ROLE, SESSION, JWT),
    ("RoleWithAPath", "arn:aws:iam::123456789012:role/team/payments/orders", SESSION, JWT),
    ("ChinaPartitionRole", "arn:aws-cn:iam::123456789012:role/orders", SESSION, JWT),
    ("SessionNameWithEveryAllowedCharacter", ROLE, "a_B+c=d,e.f@g-h9", JWT),
    ("TokenWithReservedCharacters", ROLE, SESSION, "a+b/c=d~e&f%g?h#i:j;k'l\"m"),
    ("TokenWithASpace", ROLE, SESSION, "before after"),
    ("TokenWithNonAscii", ROLE, SESSION, "t\N{LATIN SMALL LETTER U WITH DIAERESIS}r\N{EURO SIGN}\N{GRINNING FACE}"),
    ("LongToken", ROLE, SESSION, "abcdefghij" * 2000),
]


def credentials_xml(access=ACCESS_KEY, secret=SECRET_KEY, token=TOKEN, expiration=EXPIRATION):
    return (
        f"<AccessKeyId>{access}</AccessKeyId><SecretAccessKey>{secret}</SecretAccessKey>"
        f"<SessionToken>{token}</SessionToken><Expiration>{expiration}</Expiration>"
    )


def success(credentials=None, before="", after="", namespace=f' xmlns="{NAMESPACE}"'):
    credentials = credentials_xml() if credentials is None else credentials
    return (
        f"<AssumeRoleWithWebIdentityResponse{namespace}><AssumeRoleWithWebIdentityResult>{before}"
        f"<Credentials>{credentials}</Credentials>{after}</AssumeRoleWithWebIdentityResult>"
        "<ResponseMetadata><RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId></ResponseMetadata>"
        "</AssumeRoleWithWebIdentityResponse>"
    )


# Each case: name, body. All are HTTP 200.
RESPONSES = [
    ("DocumentationSample", f"""<AssumeRoleWithWebIdentityResponse xmlns="{NAMESPACE}">
  <AssumeRoleWithWebIdentityResult>
    <SubjectFromWebIdentityToken>system:serviceaccount:shop:orders</SubjectFromWebIdentityToken>
    <Audience>sts.amazonaws.com</Audience>
    <AssumedRoleUser>
      <Arn>arn:aws:sts::123456789012:assumed-role/orders/dynasharp-1793000000</Arn>
      <AssumedRoleId>AROACLKWSDQRAOEXAMPLE:dynasharp-1793000000</AssumedRoleId>
    </AssumedRoleUser>
    <Credentials>
      <SessionToken>{TOKEN}</SessionToken>
      <SecretAccessKey>{SECRET_KEY}</SecretAccessKey>
      <Expiration>{EXPIRATION}</Expiration>
      <AccessKeyId>{ACCESS_KEY}</AccessKeyId>
    </Credentials>
    <SourceIdentity>SourceIdentityValue</SourceIdentity>
    <Provider>https://oidc.eks.eu-west-1.amazonaws.com/id/EXAMPLE</Provider>
  </AssumeRoleWithWebIdentityResult>
  <ResponseMetadata>
    <RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId>
  </ResponseMetadata>
</AssumeRoleWithWebIdentityResponse>
"""),
    ("NoWhitespaceBetweenElements", success()),
    ("XmlDeclaration", '<?xml version="1.0" encoding="UTF-8"?>\n' + success()),
    ("NoNamespace", success(namespace="")),
    ("OtherNamespace", success(namespace=' xmlns="https://example.com/sts"')),
    ("PrefixedElements", success(namespace=f' xmlns="{NAMESPACE}" xmlns:a="https://example.com/a"').replace(
        "<Credentials>", "<a:Credentials>").replace("</Credentials>", "</a:Credentials>").replace(
        "<AccessKeyId>", "<a:AccessKeyId>").replace("</AccessKeyId>", "</a:AccessKeyId>")),
    ("UnknownElementsAroundCredentials", success(before="<Unknown><Deep>1</Deep></Unknown>", after="<AlsoUnknown/>")),
    ("UnknownElementsInCredentials", success("<First>x</First>" + credentials_xml() + "<Last><Inner>y</Inner></Last>")),
    ("FieldNamesInsideUnknownElements", success(
        "<Extra><AccessKeyId>WRONG</AccessKeyId><SecretAccessKey>WRONG</SecretAccessKey></Extra>" + credentials_xml(),
        before="<AssumedRoleUser><Credentials><AccessKeyId>WRONG</AccessKeyId></Credentials></AssumedRoleUser>")),
    ("EscapedCharacters", success(credentials_xml(secret="a&amp;b&lt;c&gt;d&#43;e&#x2F;f"))),
    ("CDataSection", success(credentials_xml(token="<![CDATA[a<b&c]]>"))),
    ("CommentInsideAValue", success(credentials_xml(access="ASIA<!-- a comment -->EXAMPLE"))),
    ("ExpirationWithFractionOfASecond", success(credentials_xml(expiration="2026-10-31T18:34:56.123Z"))),
    ("ExpirationWithOffset", success(credentials_xml(expiration="2026-10-31T20:34:56+02:00"))),
    ("Refreshed", success(credentials_xml(REFRESHED_ACCESS_KEY, REFRESHED_SECRET_KEY, REFRESHED_TOKEN, REFRESHED_EXPIRATION))),
]

# Each case: name, region, environment. A region of None is what a client gets without one.
CONFIGURED_ENDPOINTS = [
    ("ServiceVariable", "eu-west-1", {STS_VARIABLE: LOOPBACK}),
    ("GlobalVariable", "eu-west-1", {GLOBAL_VARIABLE: LOOPBACK}),
    ("ServiceVariableBeforeGlobal", "eu-west-1", {STS_VARIABLE: LOOPBACK, GLOBAL_VARIABLE: OTHER_LOOPBACK}),
    ("EmptyServiceVariableFallsToGlobal", "eu-west-1", {STS_VARIABLE: "", GLOBAL_VARIABLE: LOOPBACK}),
    ("BothEmptyFallToTheRegion", "eu-west-1", {STS_VARIABLE: "", GLOBAL_VARIABLE: ""}),
    ("ServiceVariableBeforeLegacy", "eu-west-1", {STS_VARIABLE: LOOPBACK, MODE_VARIABLE: "legacy"}),
    ("GlobalVariableBeforeLegacy", "eu-west-1", {GLOBAL_VARIABLE: LOOPBACK, MODE_VARIABLE: "legacy"}),
    ("EmptyVariablesLeaveLegacyAlone", "eu-west-1", {STS_VARIABLE: "", GLOBAL_VARIABLE: "", MODE_VARIABLE: "legacy"}),
    ("HttpsEndpoint", "eu-west-1", {STS_VARIABLE: "https://sts.example.com"}),
    ("HttpsEndpointWithAPort", "eu-west-1", {STS_VARIABLE: "https://sts.example.com:8443"}),
    ("TrailingSlash", "eu-west-1", {STS_VARIABLE: LOOPBACK + "/"}),
    ("LocalhostByName", "eu-west-1", {STS_VARIABLE: "http://localhost:4566"}),
    ("LoopbackOfIpv6", "eu-west-1", {STS_VARIABLE: "http://[::1]:4566"}),
    ("RegionOfAnotherPartition", "cn-north-1", {STS_VARIABLE: LOOPBACK}),
    ("NoRegionNeeded", None, {STS_VARIABLE: LOOPBACK}),
    ("NoRegionNeededByGlobal", None, {GLOBAL_VARIABLE: LOOPBACK}),
]


def error(code, message="The message of the service.", namespace=f' xmlns="{NAMESPACE}"'):
    return (
        f"<ErrorResponse{namespace}><Error><Type>Sender</Type><Code>{code}</Code><Message>{message}</Message></Error>"
        "<RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId></ErrorResponse>"
    )


# Each case: name, HTTP status, body.
ERRORS = [
    ("InvalidIdentityToken", 400, error("InvalidIdentityToken")),
    ("IDPRejectedClaim", 403, error("IDPRejectedClaim")),
    ("IDPCommunicationError", 400, error("IDPCommunicationError")),
    ("ExpiredTokenException", 400, error("ExpiredTokenException")),
    ("RegionDisabledException", 403, error("RegionDisabledException")),
    ("MalformedPolicyDocument", 400, error("MalformedPolicyDocument")),
    ("PackedPolicyTooLarge", 400, error("PackedPolicyTooLarge")),
    # Not in the model of the operation, and sent all the same.
    ("AccessDenied", 403, error("AccessDenied")),
    ("Throttling", 400, error("Throttling")),
    ("ValidationError", 400, error("ValidationError")),
    ("InternalFailure", 500, error("InternalFailure")),
    ("ServiceUnavailable", 503, error("ServiceUnavailable")),
    ("NoNamespace", 400, error("InvalidIdentityToken", namespace="")),
    ("WithWhitespace", 403, f"""<ErrorResponse xmlns="{NAMESPACE}">
  <Error>
    <Type>Sender</Type>
    <Code>AccessDenied</Code>
    <Message>Not authorized to perform sts:AssumeRoleWithWebIdentity</Message>
  </Error>
  <RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId>
</ErrorResponse>
"""),
]

SESSION_NAMES = [
    "",
    "a",
    "ab",
    SESSION,
    "a_B+c=d,e.f@g-h9",
    "a" * 64,
    "a" * 65,
    "with space",
    "with/slash",
    "with:colon",
    "t\N{LATIN SMALL LETTER U WITH DIAERESIS}r",
    "\N{ARABIC-INDIC DIGIT ONE}\N{ARABIC-INDIC DIGIT TWO}",
]


class Raw:
    """The part of a urllib3 response that botocore reads."""

    def __init__(self, body):
        self._body = io.BytesIO(body)

    def stream(self, **_):
        yield self._body.read()


def create_client(region):
    session = botocore.session.Session()
    config = botocore.config.Config(signature_version=UNSIGNED, retries={"max_attempts": 0, "mode": "standard"})
    return session.create_client("sts", region_name=region, config=config)


def call(client, status, body, role=ROLE, session=SESSION, token=JWT):
    """Calls the operation and returns the request that went out with the result of the parser."""

    sent = []

    def answer(request, **_):
        sent.append(request)
        return botocore.awsrequest.AWSResponse(request.url, status, {}, Raw(body.encode("utf-8")))

    client.meta.events.register("before-send.sts.AssumeRoleWithWebIdentity", answer)
    try:
        result = client.assume_role_with_web_identity(RoleArn=role, RoleSessionName=session, WebIdentityToken=token)
    except botocore.exceptions.ClientError as exception:
        result = exception.response
    finally:
        client.meta.events.unregister("before-send.sts.AssumeRoleWithWebIdentity", answer)
    return sent[0], result


def is_retryable(client, status, parsed):
    operation = client.meta.service_model.operation_model("AssumeRoleWithWebIdentity")
    context = botocore.retries.standard.RetryContext(
        attempt_number=1,
        operation_model=operation,
        parsed_response=parsed,
        http_response=botocore.awsrequest.AWSResponse("https://sts.amazonaws.com/", status, {}, Raw(b"")),
    )
    return botocore.retries.standard.StandardRetryConditions().is_retryable(context)


def request_vector(name, role, session, token):
    client = create_client("eu-west-1")
    request, _ = call(client, 200, success(), role, session, token)
    headers = {key.lower(): (value.decode() if isinstance(value, bytes) else value) for key, value in request.headers.items()}
    return {
        "name": name,
        "role_arn": role,
        "role_session_name": session,
        "token": token,
        "method": request.method,
        "content_type": headers["content-type"],
        "signed": "authorization" in headers,
        "body": request.body,
    }


def endpoint_vectors():
    session = botocore.session.Session()
    regions = []
    for partition in ("aws", "aws-cn", "aws-us-gov"):
        regions += session.get_available_regions("sts", partition_name=partition, allow_non_regional=False)
    # A bare "aws-global" and the FIPS names are no regions of a client.
    regions = sorted(r for r in regions if "fips" not in r and r != "aws-global")
    assert "us-east-1" in regions and "cn-north-1" in regions and "us-gov-west-1" in regions

    vectors = []
    for mode in (None, "regional", "legacy"):
        with mock.patch.dict(os.environ, {} if mode is None else {"AWS_STS_REGIONAL_ENDPOINTS": mode}):
            for region in regions:
                request, _ = call(create_client(region), 200, success())
                vectors.append({"region": region, "mode": mode, "url": request.url})
    return vectors


def configured_endpoint_vector(name, region, environment):
    with mock.patch.dict(os.environ, environment):
        request, _ = call(create_client(region), 200, success())
    return {"name": name, "region": region, "environment": environment, "url": request.url}


def timestamp(value):
    return value.astimezone(datetime.timezone.utc).isoformat()


def response_vector(name, body):
    client = create_client("eu-west-1")
    _, result = call(client, 200, body)
    credentials = result["Credentials"]
    return {
        "name": name,
        "body": body,
        "access_key_id": credentials["AccessKeyId"],
        "secret_access_key": credentials["SecretAccessKey"],
        "session_token": credentials["SessionToken"],
        "expiration": timestamp(credentials["Expiration"]),
    }


def error_vector(name, status, body):
    client = create_client("eu-west-1")
    _, result = call(client, status, body)
    return {
        "name": name,
        "status": status,
        "body": body,
        "code": result["Error"]["Code"],
        "retryable": is_retryable(client, status, result),
    }


def session_name_vector(name, shape):
    valid = shape["min"] <= len(name) <= shape["max"] and re.fullmatch(shape["pattern"], name, re.ASCII) is not None
    return {"name": name, "valid": valid}


def main():
    model = botocore.loaders.create_loader().load_service_model("sts", "service-2", "2011-06-15")
    assert model["metadata"]["protocol"] == "query"
    operation = model["operations"]["AssumeRoleWithWebIdentity"]
    assert operation["auth"] == ["smithy.api#noAuth"], "the operation is not signed"

    modeled = {model["shapes"][e["shape"]]["error"]["code"]: model["shapes"][e["shape"]]["error"]["httpStatusCode"] for e in operation["errors"]}
    missing = set(modeled) - {name for name, _, _ in ERRORS}
    assert not missing, f"model errors without a case: {missing}"

    # No configuration of this machine may reach the client.
    environment = {"AWS_CONFIG_FILE": os.devnull, "AWS_SHARED_CREDENTIALS_FILE": os.devnull, "AWS_EC2_METADATA_DISABLED": "true"}
    with mock.patch.dict(os.environ, environment, clear=True):
        vectors = {
            "botocore": botocore.__version__,
            "modeled_errors": modeled,
            "requests": [request_vector(*case) for case in REQUESTS],
            "endpoints": endpoint_vectors(),
            "configured_endpoints": [configured_endpoint_vector(*case) for case in CONFIGURED_ENDPOINTS],
            "responses": [response_vector(*case) for case in RESPONSES],
            "errors": [error_vector(*case) for case in ERRORS],
            "session_names": [session_name_vector(name, model["shapes"]["roleSessionNameType"]) for name in SESSION_NAMES],
        }

    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as file:
        json.dump(vectors, file, indent=1)
        file.write("\n")


if __name__ == "__main__":
    main()
