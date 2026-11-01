"""Generates the SigV4 test vectors of SigV4Vectors.cs with botocore.

The expected Authorization headers must not come from the C# implementation, so they are produced here by the
reference implementation of AWS (botocore.auth.SigV4Auth). The requests carry exactly the header set that DynaSharp
sends: Content-Type, X-Amz-Target, plus X-Amz-Date and X-Amz-Security-Token added by botocore, and the host.

Setup (outside the repository):
    python3 -m venv /tmp/botocore-venv
    /tmp/botocore-venv/bin/pip install botocore

Run:
    /tmp/botocore-venv/bin/python generate_sigv4_vectors.py

Prints the elements of SigV4Vectors.All. Paste them into SigV4Vectors.cs, replacing the existing elements. The
inputs of every vector are defined here only, so the C# side cannot drift from what botocore signed.
"""

import datetime
import sys
from unittest import mock

import botocore
from botocore.auth import SigV4Auth
from botocore.awsrequest import AWSRequest
from botocore.credentials import Credentials

CONTENT_TYPE = "application/x-amz-json-1.0"
SERVICE = "dynamodb"

# AWS documentation example credentials.
ACCESS_KEY = "AKIDEXAMPLE"
SECRET_KEY = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY"
OTHER_ACCESS_KEY = "AKIAIOSFODNN7EXAMPLE"
OTHER_SECRET_KEY = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
SESSION_TOKEN = "IQoJb3JpZ2luX2VjEXAMPLETOKEN//////////wEaCXVzLWVhc3QtMSJHMEUCIQD+EXAMPLE=="


class Repeat:
    """A long value that is written as Repeat("unit", count) in C# to keep the vectors readable."""

    def __init__(self, unit, count):
        self.unit = unit
        self.count = count

    @property
    def value(self):
        return self.unit * self.count

    def csharp(self):
        return f'Repeat("{self.unit}", {self.count})'


LONG_SECRET = Repeat(SECRET_KEY, 5)  # 200 characters: HMAC hashes keys longer than its block size.
LONG_TOKEN = Repeat("FwoGZXIvYXdz", 125)  # 1500 characters, like the session tokens of STS.

GET_ITEM = ("DynamoDB_20120810.GetItem", '{"TableName":"Users","Key":{"Id":{"S":"user-1"}}}')
PUT_ITEM = (
    "DynamoDB_20120810.PutItem",
    '{"TableName":"Users","Item":{"Id":{"S":"user-1"},"Name":{"S":"Ada"}}}',
)
DESCRIBE_TABLE = ("DynamoDB_20120810.DescribeTable", '{"TableName":"Users"}')
QUERY = (
    "DynamoDB_20120810.Query",
    '{"TableName":"Users","KeyConditionExpression":"Id = :id","ExpressionAttributeValues":{":id":{"S":"user-1"}}}',
)
GET_RECORDS = ("DynamoDBStreams_20120810.GetRecords", '{"ShardIterator":"AAAAAAAAAA","Limit":100}')

DEFAULT_TIME = datetime.datetime(2026, 10, 31, 12, 34, 56)


def vector(name, region, url, operation, time=DEFAULT_TIME, access_key=ACCESS_KEY, secret_key=SECRET_KEY, token=None):
    target, body = operation
    return {
        "name": name,
        "region": region,
        "url": url,
        "target": target,
        "body": body,
        "access_key": access_key,
        "secret_key": secret_key,
        "token": token,
        "time": time,
    }


def default_url(region):
    return f"https://dynamodb.{region}.amazonaws.com/"


VECTORS = [
    vector("LongTermKeys", "eu-central-1", default_url("eu-central-1"), GET_ITEM),
    # Same credentials and date, later time of day: the signing key is reused, the signature differs.
    vector("LongTermKeysLaterSameDay", "eu-central-1", default_url("eu-central-1"), GET_ITEM,
           time=datetime.datetime(2026, 10, 31, 23, 59, 59)),
    # botocore drops default ports from the host, as Uri.Authority does.
    vector("ExplicitDefaultPort", "eu-central-1", "https://dynamodb.eu-central-1.amazonaws.com:443/", GET_ITEM),
    vector("SessionToken", "us-east-1", default_url("us-east-1"), PUT_ITEM, token=SESSION_TOKEN),
    vector("LongSessionToken", "us-west-2", default_url("us-west-2"), QUERY, token=LONG_TOKEN),
    vector("LongSecretKey", "eu-west-1", default_url("eu-west-1"), GET_ITEM, secret_key=LONG_SECRET),
    vector("OtherCredentials", "eu-central-1", default_url("eu-central-1"), GET_ITEM,
           access_key=OTHER_ACCESS_KEY, secret_key=OTHER_SECRET_KEY),
    # DynamoDB Local: the port is part of the host header.
    vector("DynamoDbLocalPort", "us-east-1", "http://localhost:8000/", DESCRIBE_TABLE),
    vector("DateRolloverBefore", "ap-southeast-2", default_url("ap-southeast-2"), DESCRIBE_TABLE,
           time=datetime.datetime(2026, 12, 31, 23, 59, 59)),
    vector("DateRolloverAfter", "ap-southeast-2", default_url("ap-southeast-2"), DESCRIBE_TABLE,
           time=datetime.datetime(2027, 1, 1, 0, 0, 0)),
    vector("ChinaRegion", "cn-north-1", "https://dynamodb.cn-north-1.amazonaws.com.cn/", GET_ITEM),
    vector("GovCloudRegion", "us-gov-west-1", default_url("us-gov-west-1"), PUT_ITEM, token=SESSION_TOKEN),
    # Streams has its own host but signs with the service name dynamodb.
    vector("StreamsEndpoint", "us-west-2", "https://streams.dynamodb.us-west-2.amazonaws.com/", GET_RECORDS),
]


def sign(v):
    """Signs the request of a vector with botocore and returns (X-Amz-Date, Authorization)."""
    token = v["token"].value if isinstance(v["token"], Repeat) else v["token"]
    secret = v["secret_key"].value if isinstance(v["secret_key"], Repeat) else v["secret_key"]
    credentials = Credentials(v["access_key"], secret, token)
    request = AWSRequest(
        method="POST",
        url=v["url"],
        data=v["body"].encode("utf-8"),
        headers={"Content-Type": CONTENT_TYPE, "X-Amz-Target": v["target"]},
    )
    with mock.patch("botocore.auth.get_current_datetime", return_value=v["time"]):
        SigV4Auth(credentials, SERVICE, v["region"]).add_auth(request)

    authorization = request.headers["Authorization"]
    expected_headers = "content-type;host;x-amz-date;x-amz-target"
    if token:
        expected_headers = "content-type;host;x-amz-date;x-amz-security-token;x-amz-target"
    if f"SignedHeaders={expected_headers}," not in authorization:
        sys.exit(f"{v['name']}: botocore signed another header set than DynaSharp: {authorization}")
    return request.headers["X-Amz-Date"], authorization


def csharp_string(value):
    return value.csharp() if isinstance(value, Repeat) else '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def csharp_vector(v, amz_date, authorization):
    time = v["time"]
    token = "null" if v["token"] is None else csharp_string(v["token"])
    fields = [
        csharp_string(v["name"]),
        csharp_string(v["region"]),
        csharp_string(v["url"]),
        csharp_string(v["target"]),
        '"""' + v["body"] + '"""',
        csharp_string(v["access_key"]),
        csharp_string(v["secret_key"]),
        token,
        f"new DateTimeOffset({time.year}, {time.month}, {time.day}, {time.hour}, {time.minute}, {time.second}, TimeSpan.Zero)",
        csharp_string(amz_date),
        csharp_string(authorization),
    ]
    return "        new(\n" + ",\n".join("            " + field for field in fields) + "),"


def main():
    signed = {v["name"]: sign(v) for v in VECTORS}
    if signed["ExplicitDefaultPort"] != signed["LongTermKeys"]:
        sys.exit("The explicit default port must not change the signature.")

    print(f"// Generated by generate_sigv4_vectors.py with botocore {botocore.__version__}. Do not edit by hand.")
    for v in VECTORS:
        amz_date, authorization = signed[v["name"]]
        print(csharp_vector(v, amz_date, authorization))


if __name__ == "__main__":
    main()
