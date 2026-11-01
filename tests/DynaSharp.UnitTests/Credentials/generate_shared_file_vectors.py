"""Generates SharedFileVectors.json, the expected results for the shared AWS config and credentials file tests, with botocore.

The INI parser and the profile lookup of DynaSharp must not be pinned to themselves, so every expectation here comes from
the code that real SDK users run: botocore.configloader.raw_config_parse (Python's configparser with botocore's nested
sections), build_profile_map, the profile merge of botocore.session.Session.full_config, SharedCredentialProvider,
ConfigProvider and the `region` setting of a session.

Setup (outside the repository):
    python -m venv <dir>
    <dir>/Scripts/pip install botocore        (bin/pip on Linux and macOS)

Run (from any directory):
    <dir>/Scripts/python generate_shared_file_vectors.py

Writes SharedFileVectors.json next to this script. The file is an embedded resource of the test project. The inputs of every
case are defined here only, so the C# side cannot drift from what botocore parsed. The script runs Python in UTF-8 mode so
that the result does not depend on the locale of the machine.
"""

import json
import os
import subprocess
import sys
import tempfile

import botocore
import botocore.configloader
import botocore.credentials
import botocore.exceptions
import botocore.session

OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "SharedFileVectors.json")

# AWS documentation example credentials.
ACCESS_KEY = "AKIAIOSFODNN7EXAMPLE"
SECRET_KEY = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
TOKEN = "IQoJb3JpZ2luX2VjEXAMPLETOKEN"
OTHER_ACCESS_KEY = "AKIAI44QH8DHBEXAMPLE"
OTHER_SECRET_KEY = "je7MtGbClwBF/2Zp9Utk/h3yCo8nvbEXAMPLEKEY"

U_UMLAUT = "\N{LATIN SMALL LETTER U WITH DIAERESIS}"


def keys(access=ACCESS_KEY, secret=SECRET_KEY):
    return f"aws_access_key_id = {access}\naws_secret_access_key = {secret}\n"


# Each case: name, credentials file text (or None), config file text (or None).
CASES = [
    # --- plain files ---------------------------------------------------------------------------------------------------
    ("NoFiles", None, None),
    ("EmptyFiles", "", ""),
    ("OnlyComments", "# comment\n; another\n   # indented\n", "# nothing here\n"),
    ("DefaultKeysInCredentialsFile", "[default]\n" + keys(), None),
    ("DefaultKeysAndTokenInCredentialsFile", "[default]\n" + keys() + f"aws_session_token = {TOKEN}\n", None),
    ("DefaultKeysInConfigFile", None, "[default]\n" + keys() + "region = eu-central-1\n"),
    ("NamedProfileInConfigFile", None, "[profile dev]\n" + keys() + "region = us-west-2\n"),
    ("NamedProfileInCredentialsFile", "[dev]\n" + keys() + "\n[prod]\n" + keys(OTHER_ACCESS_KEY, OTHER_SECRET_KEY), None),
    ("RegionOnlyInConfigFile", "[default]\n" + keys(), "[default]\nregion = ap-southeast-2\n"),
    ("RegionInCredentialsFile", "[default]\n" + keys() + "region = eu-north-1\n", None),
    ("RegionInBothFilesCredentialsWins", "[default]\nregion = eu-west-1\n", "[default]\nregion = eu-west-2\n"),
    # --- sections ------------------------------------------------------------------------------------------------------
    ("ProfileKeywordInCredentialsFileIsAProfileNamedProfileDev", "[profile dev]\n" + keys(), None),
    ("NamedSectionWithoutProfileKeywordInConfigFileIsNotAProfile", None, "[dev]\n" + keys() + "[default]\nregion = eu-west-1\n"),
    ("DefaultSectionAndProfileDefaultInConfigFileLastWins", None,
     "[default]\nregion = eu-west-1\n[profile default]\nregion = eu-west-2\n"),
    ("ProfileDefaultThenDefaultInConfigFileLastWins", None,
     "[profile default]\nregion = eu-west-2\n[default]\nregion = eu-west-1\n"),
    ("SectionNamesAreCaseSensitive", "[Default]\n" + keys() + "[DEV]\n" + keys(), "[Profile dev]\nregion = eu-west-1\n"),
    ("ProfileKeywordIsCaseSensitive", None, "[Profile dev]\nregion = eu-west-1\n"),
    ("ProfileNameWithTwoSpaces", None, "[profile  dev]\nregion = eu-west-1\n"),
    ("ProfileNameWithTab", None, "[profile\tdev]\nregion = eu-west-1\n"),
    ("ProfileNameWithTrailingSpace", None, "[profile dev ]\nregion = eu-west-1\n"),
    ("SectionNameWithLeadingSpaceIsNotAProfile", None, "[ profile dev]\nregion = eu-west-1\n"),
    ("ProfileNameDoubleQuoted", None, "[profile \"my profile\"]\nregion = eu-west-1\n"),
    ("ProfileNameSingleQuoted", None, "[profile 'my profile']\nregion = eu-west-1\n"),
    ("ProfileNameQuotedWithoutSpace", None, "[profile \"dev\"]\nregion = eu-west-1\n"),
    ("ProfileNameEmptyQuoted", None, "[profile \"\"]\nregion = eu-west-1\n"),
    ("ProfileNameMixedQuoting", None, "[profile my\"pro file\"x]\nregion = eu-west-1\n"),
    ("ProfileNameBackslashEscapedSpace", None, "[profile my\\ profile]\nregion = eu-west-1\n"),
    ("ProfileNameEscapedQuoteInDoubleQuotes", None, "[profile \"a\\\"b\"]\nregion = eu-west-1\n"),
    ("ProfileNameEscapedBackslashInDoubleQuotes", None, "[profile \"a\\\\b\"]\nregion = eu-west-1\n"),
    ("ProfileNameOtherBackslashInDoubleQuotesIsKept", None, "[profile \"a\\nb\"]\nregion = eu-west-1\n"),
    ("ProfileNameBackslashInSingleQuotesIsKept", None, "[profile 'a\\b']\nregion = eu-west-1\n"),
    ("ProfileNameWithThreeWordsIsIgnored", None, "[profile my other profile]\nregion = eu-west-1\n"),
    ("ProfileKeywordAloneIsIgnored", None, "[profile]\nregion = eu-west-1\n"),
    ("ProfileNameUnterminatedQuoteIsIgnored", None, "[profile \"dev]\nregion = eu-west-1\n"),
    ("ProfileNameTrailingBackslashIsIgnored", None, "[profile dev\\]\nregion = eu-west-1\n"),
    ("SectionStartingWithProfileLettersIsAProfile", None, "[profiles dev]\nregion = eu-west-1\n[profile-x y]\nregion = eu-west-2\n"),
    ("ServicesAndSsoSessionSectionsAreNotProfiles", None,
     "[services s]\ndynamodb =\n  endpoint_url = http://localhost:8000\n[sso-session corp]\nsso_region = us-east-1\n[default]\nregion = eu-west-1\n"),
    ("UnicodeProfileNameAndRegion", None, f"[profile d{U_UMLAUT}v]\nregion = eu-west-1\n"),
    ("HeaderWithTextAfterTheBracket", "[default] trailing\n" + keys(), None),
    ("HeaderWithCommentAfterTheBracket", "[default] # comment\n" + keys(), None),
    ("HeaderWithInnerBracket", "[a]b]\n" + keys(), None),
    ("HeaderWithDoubleClosingBracket", "[a]]\n" + keys(), None),
    ("HeaderWithSpacesInside", "[ default ]\n" + keys(), None),
    ("HeaderWithoutClosingBracketIsAnError", "[default\n" + keys(), None),
    ("EmptyHeaderIsAnError", "[]\n" + keys(), None),
    ("IndentedHeaderAfterAnOptionIsAContinuation", "[default]\nregion = eu-west-1\n  [other]\n", None),
    ("IndentedHeaderAfterAHeaderIsAHeader", "[a]\n  [b]\nregion = eu-west-1\n", None),
    ("DuplicateSectionIsAnError", "[default]\n" + keys() + "[default]\n" + keys(), None),
    ("DuplicateSectionInConfigFileIsAnError", None, "[profile dev]\nregion = a\n[profile dev]\nregion = b\n"),
    ("DefaultSectionAndProfileDefaultAreDistinctSectionNames", None, "[default]\nregion = a\n[profile default]\nregion = b\n"),
    # --- options -------------------------------------------------------------------------------------------------------
    ("NoSpacesAroundEquals", "[default]\naws_access_key_id=" + ACCESS_KEY + "\naws_secret_access_key=" + SECRET_KEY + "\n", None),
    ("ColonDelimiter", "[default]\naws_access_key_id: " + ACCESS_KEY + "\naws_secret_access_key:" + SECRET_KEY + "\n", None),
    ("ManySpacesAndTabs", "[default]\naws_access_key_id\t=\t  " + ACCESS_KEY + "  \t\naws_secret_access_key   =   " + SECRET_KEY + "   \n", None),
    ("OptionNamesAreLowerCased", "[default]\nAWS_ACCESS_KEY_ID = " + ACCESS_KEY + "\nAws_Secret_Access_Key = " + SECRET_KEY + "\n", None),
    ("ValueWithEqualsAndColon", "[default]\nendpoint_url = http://localhost:8000/?a=b&c:d\n", None),
    ("OptionNameWithColonBeforeEquals", "[default]\na:b = c\n", None),
    ("InlineCommentsAreKeptInTheValue", "[default]\nregion = us-east-1 # eu-west-1\nother = x ; y\n", None),
    ("HashInsideValueIsKept", "[default]\nkey = a#b\n", None),
    ("CommentLinesBetweenOptions", "[default]\n# a\nregion = eu-west-1\n; b\n   # c\nother = 1\n", None),
    ("EmptyValue", "[default]\nregion =\nother =   \n", None),
    ("EmptyValueThenContinuation", "[default]\nregion =\n  eu-west-1\n", None),
    ("OptionWithoutDelimiterIsAnError", "[default]\naws_access_key_id\n", None),
    ("OptionWithoutDelimiterReportsTheFirstBadLine", "[default]\nregion = a\nbad one\nbad two\n", None),
    ("EmptyOptionNameIsAnError", "[default]\n= value\n", None),
    ("EmptyOptionNameWithColonIsAnError", "[default]\n: value\n", None),
    ("EmptyOptionNameAfterABadLineReportsTheFirstBadLine", "[default]\nbad\n= value\n", None),
    ("ContentBeforeTheFirstSectionIsAnError", "region = eu-west-1\n[default]\n", None),
    ("ContentBeforeTheFirstSectionAfterCommentsIsAnError", "# c\n\nregion = eu-west-1\n", None),
    ("DuplicateOptionIsAnError", "[default]\nregion = a\nregion = b\n", None),
    ("DuplicateOptionWithOtherCaseIsAnError", "[default]\nregion = a\nREGION = b\n", None),
    ("SameOptionInDifferentSectionsIsFine", "[a]\nregion = a\n[b]\nregion = b\n", None),
    ("DuplicateSectionBeatsAnEarlierBadLine", "[a]\nbad line\n[a]\n", None),
    ("BadLineAfterTheDuplicateOptionIsNotReported", "[a]\nx = 1\nx = 2\nbad\n", None),
    ("BadLineBeforeMissingSectionHeaderIsNotPossible", "bad\n[a]\n", None),
    ("IndentedOptionAfterHeaderIsAnOption", "[default]\n   region = eu-west-1\n", None),
    ("ManyOptions", "[default]\n" + "".join(f"k{i} = v{i}\n" for i in range(30)), None),
    # --- multi line values and nested sections ------------------------------------------------------------------------
    ("ContinuationLineJoinedWithNewline", "[default]\nkey = first\n  second\n  third\n", None),
    ("ContinuationLinesAreStripped", "[default]\nkey = first\n\t  second   \n", None),
    ("ContinuationAfterBlankLineIsKept", "[default]\nkey = first\n\n  second\n", None),
    ("TrailingBlankLinesOfAValueAreDropped", "[default]\nkey = first\n  second\n\n\nother = 1\n", None),
    ("CommentInsideAValueIsDropped", "[default]\nkey = first\n  # not part\n  second\n", None),
    ("UnindentedLineEndsTheValue", "[default]\nkey = first\nsecond = 2\n", None),
    ("ContinuationLessIndentedThanTheOptionIsAnOption", "[default]\n   key = first\n  second = 2\n", None),
    ("ContinuationMoreIndentedThanTheOptionIsPartOfTheValue", "[default]\n  key = first\n    second\n", None),
    ("ContinuationBeforeAnyOptionIsAnOption", "[default]\n    a = 1\n", None),
    ("NestedSection", "[default]\ns3 =\n  max_concurrent_requests = 20\n  addressing_style = path\n", None),
    ("NestedSectionWithBlankAndCommentLines", "[default]\ns3 =\n  a = 1\n\n  # comment\n  b = 2\n", None),
    ("NestedSectionKeepsKeyCase", "[default]\ns3 =\n  Max_Concurrent = 20\n", None),
    ("NestedSectionSplitsAtTheFirstEquals", "[default]\ns3 =\n  url = http://x?a=b\n", None),
    ("NestedSectionDuplicateKeyLastWins", "[default]\ns3 =\n  a = 1\n  a = 2\n", None),
    ("NestedSectionWithColonIsAnError", "[default]\ns3 =\n  a: 1\n", None),
    ("NestedSectionLineWithoutEqualsIsAnError", "[default]\ns3 =\n  a = 1\n  oops\n", None),
    ("NestedSectionInAnUnrelatedSectionIsStillValidated", "[default]\nregion = eu-west-1\n[other]\ns3 =\n  oops\n", None),
    ("NestedSectionInAProfile", None, "[profile dev]\nregion = eu-west-1\ns3 =\n  addressing_style = path\n"),
    ("NestedValueAsCredentialKey", "[default]\naws_access_key_id =\n  a = b\naws_secret_access_key = " + SECRET_KEY + "\n", None),
    ("ValueStartingOnTheNextLineIsNested", "[default]\nkey =\n  value\n", None),
    # --- DEFAULT section -----------------------------------------------------------------------------------------------
    ("DefaultSectionValuesAreInherited", "[DEFAULT]\nregion = eu-west-1\n[dev]\n" + keys() + "[prod]\nregion = us-east-1\n", None),
    ("DefaultSectionOnlyIsNoProfile", "[DEFAULT]\nregion = eu-west-1\n", None),
    ("DefaultSectionInConfigFileIsInherited", None, "[DEFAULT]\nregion = eu-west-1\n[profile dev]\n" + keys()),
    ("DefaultSectionRepeatedIsMerged", "[DEFAULT]\na = 1\n[x]\nb = 2\n[DEFAULT]\nc = 3\n", None),
    ("DefaultSectionDuplicateOptionIsAnError", "[DEFAULT]\na = 1\na = 2\n", None),
    ("DefaultSectionNestedErrorIsOnlyFoundWithASection", "[DEFAULT]\ns3 =\n  oops\n", None),
    ("DefaultSectionNestedErrorWithASection", "[DEFAULT]\ns3 =\n  oops\n[x]\na = 1\n", None),
    ("DefaultSectionCaseMatters", "[default]\na = 1\n[Default]\nb = 2\n", None),
    ("LowercaseDefaultIsAProfileUppercaseIsInheritedValues", None, "[DEFAULT]\nregion = eu-west-1\n[default]\n" + keys()),
    # --- line endings and encoding -------------------------------------------------------------------------------------
    ("CrLfLineEndings", "[default]\r\n" + keys().replace("\n", "\r\n"), None),
    ("CrOnlyLineEndings", "[default]\r" + keys().replace("\n", "\r"), None),
    ("MixedLineEndings", "[default]\r\naws_access_key_id = " + ACCESS_KEY + "\naws_secret_access_key = " + SECRET_KEY + "\rregion = eu-west-1\r\n", None),
    ("NoTrailingNewline", "[default]\nregion = eu-west-1", None),
    ("LineNumbersWithCrLfInErrors", "[default]\r\nregion = a\r\n\r\nbad\r\n", None),
    ("UnicodeValue", f"[default]\nregion = eu-w{U_UMLAUT}st-1\n", None),
    ("TabIndentedContinuation", "[default]\nkey = first\n\tsecond\n", None),
    # --- credentials and settings --------------------------------------------------------------------------------------
    ("SessionTokenFromAwsSessionToken", "[default]\n" + keys() + f"aws_session_token = {TOKEN}\n", None),
    ("SessionTokenFromAwsSecurityToken", "[default]\n" + keys() + f"aws_security_token = {TOKEN}\n", None),
    ("SecurityTokenWinsOverSessionToken", "[default]\n" + keys() + "aws_session_token = session\naws_security_token = security\n", None),
    ("EmptySessionToken", "[default]\n" + keys() + "aws_session_token =\n", None),
    ("AccessKeyWithoutSecretIsPartial", "[default]\naws_access_key_id = " + ACCESS_KEY + "\n", None),
    ("SecretWithoutAccessKeyIsNoCredentials", "[default]\naws_secret_access_key = " + SECRET_KEY + "\n", None),
    ("EmptyAccessKey", "[default]\naws_access_key_id =\naws_secret_access_key = " + SECRET_KEY + "\n", None),
    ("EmptySecret", "[default]\naws_access_key_id = " + ACCESS_KEY + "\naws_secret_access_key =\n", None),
    ("CredentialsFileKeysWinOverConfigFileKeys", "[default]\n" + keys(), "[default]\n" + keys(OTHER_ACCESS_KEY, OTHER_SECRET_KEY)),
    ("ConfigFileKeysUsedWhenCredentialsFileHasNone", "[default]\nregion = eu-west-1\n", "[default]\n" + keys(OTHER_ACCESS_KEY, OTHER_SECRET_KEY)),
    ("TokenOnlyInConfigFileIsNotMixedWithCredentialsFileKeys", "[default]\n" + keys(),
     f"[default]\naws_session_token = {TOKEN}\n"),
    ("ConfigFileKeysAreNotMixedWithCredentialsFileToken", f"[default]\naws_session_token = {TOKEN}\n", "[default]\n" + keys()),
    ("ProfilesSplitAcrossFiles", "[dev]\n" + keys(), "[profile dev]\nregion = eu-west-1\n[profile prod]\n" + keys(OTHER_ACCESS_KEY, OTHER_SECRET_KEY)),
    ("AccountIdSetting", "[default]\n" + keys() + "aws_account_id = 123456789012\n", None),
    ("CredentialsInMalformedConfigFileOnly", "[default]\n" + keys(), "bad\n"),
    ("MalformedCredentialsFileValidConfigFile", "bad\n", "[default]\nregion = eu-west-1\n"),
    ("MalformedBothFiles", "bad\n", "[a]\nbad\n"),
    # --- settings that DynaSharp does not support (the keys must be visible to the provider) -------------------------------
    ("AssumeRoleProfile", None, "[profile dev]\nrole_arn = arn:aws:iam::123456789012:role/dev\nsource_profile = default\n[default]\n" + keys()),
    ("WebIdentityProfile", None, "[profile dev]\nrole_arn = arn:aws:iam::123456789012:role/dev\nweb_identity_token_file = /var/run/token\n"),
    ("SsoProfile", None, "[profile dev]\nsso_session = corp\nsso_account_id = 123456789012\nsso_role_name = Admin\n[sso-session corp]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = us-east-1\n"),
    ("LegacySsoProfile", None, "[profile dev]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = us-east-1\nsso_account_id = 123456789012\nsso_role_name = Admin\n"),
    ("CredentialProcessProfile", None, "[profile dev]\ncredential_process = /usr/local/bin/creds --profile dev\n"),
    ("CredentialProcessAndStaticKeysInConfigFile", None, "[default]\n" + keys() + "credential_process = /usr/local/bin/creds\n"),
    ("CredentialProcessInConfigFileStaticKeysInCredentialsFile", "[default]\n" + keys(), "[default]\ncredential_process = /usr/local/bin/creds\n"),
    ("LoginProfile", None, "[profile dev]\nlogin_session = arn:aws:iam::123456789012:user/dev\n"),
    ("RoleArnInCredentialsFile", "[dev]\n" + keys() + "role_arn = arn:aws:iam::123456789012:role/dev\n", None),
]


def parse_file(path):
    """Parses a file with botocore. Returns {"sections": [...]} or {"error": {"line": n or None}}.

    Option values are strings; nested sections are dicts of strings.
    """
    try:
        parsed = botocore.configloader.raw_config_parse(path)
    except botocore.exceptions.ConfigParseError as error:
        return {"error": {"line": error_line(error)}}
    return {"sections": [{"name": name, "values": values} for name, values in parsed.items()]}


def error_line(error):
    """The line botocore's underlying configparser error points at, or None (nested sections report no line)."""
    cause = error.kwargs.get("error")
    if cause is None:
        return None
    if hasattr(cause, "lineno"):
        return cause.lineno
    errors = getattr(cause, "errors", None)
    if errors:
        return errors[0][0]
    return None


def credentials_dict(credentials):
    if credentials is None:
        return None
    return {
        "access_key": credentials.access_key,
        "secret_key": credentials.secret_key,
        "token": credentials.token,
        "account_id": credentials.account_id,
    }


def provider_result(load):
    try:
        return credentials_dict(load())
    except botocore.exceptions.PartialCredentialsError:
        return {"error": "partial"}
    except botocore.exceptions.ConfigParseError:
        return {"error": "malformed"}


def region_of(credentials_path, config_path, profile):
    """The `region` setting of a botocore session for the profile, or an error marker."""
    session = botocore.session.Session()
    session.set_config_variable("credentials_file", credentials_path)
    session.set_config_variable("config_file", config_path)
    if profile is not None:
        session.set_config_variable("profile", profile)
    try:
        return {"value": session.get_config_variable("region")}
    except botocore.exceptions.ProfileNotFound:
        return {"error": "profile_not_found"}
    except botocore.exceptions.ConfigParseError:
        return {"error": "malformed"}


def full_profiles(credentials_path, config_path):
    """The merged profile map of a botocore session (config file profiles, overlaid by credentials file sections)."""
    session = botocore.session.Session()
    session.set_config_variable("credentials_file", credentials_path)
    session.set_config_variable("config_file", config_path)
    try:
        return session.full_config["profiles"]
    except botocore.exceptions.ConfigParseError:
        return None


def build_case(directory, name, credentials_text, config_text):
    missing = os.path.join(directory, "missing")
    credentials_path = missing
    config_path = missing
    if credentials_text is not None:
        credentials_path = os.path.join(directory, "credentials")
        with open(credentials_path, "wb") as file:
            file.write(credentials_text.encode("utf-8"))
    if config_text is not None:
        config_path = os.path.join(directory, "config")
        with open(config_path, "wb") as file:
            file.write(config_text.encode("utf-8"))

    case = {"name": name, "credentials_file": credentials_text, "config_file": config_text}
    case["credentials_file_parse"] = None if credentials_text is None else parse_file(credentials_path)
    case["config_file_parse"] = None if config_text is None else parse_file(config_path)

    profiles = full_profiles(credentials_path, config_path)
    case["profiles"] = profiles

    # Query every profile botocore knows, the default profile (which may be missing) and one that never exists.
    names = list(profiles.keys()) if profiles is not None else []
    for extra in ("default", "missing-profile"):
        if extra not in names:
            names.append(extra)

    lookups = []
    for profile in names:
        lookups.append({
            "profile": profile,
            # An unset profile means "default" and is allowed to be missing; a set profile must exist.
            "region": region_of(credentials_path, config_path, None if profile == "default" else profile),
            "shared_credentials": provider_result(
                botocore.credentials.SharedCredentialProvider(credentials_path, profile).load),
            "config_credentials": provider_result(
                botocore.credentials.ConfigProvider(config_path, profile).load),
        })
    case["lookups"] = lookups
    return case


def main():
    if not sys.flags.utf8_mode:
        # Re-run in UTF-8 mode: configparser opens files with the locale encoding otherwise.
        sys.exit(subprocess.run([sys.executable, "-X", "utf8", *sys.argv], check=False).returncode)

    for variable in ("AWS_PROFILE", "AWS_DEFAULT_PROFILE", "AWS_REGION", "AWS_DEFAULT_REGION", "AWS_CONFIG_FILE",
                     "AWS_SHARED_CREDENTIALS_FILE"):
        os.environ.pop(variable, None)

    names = [case[0] for case in CASES]
    if len(set(names)) != len(names):
        sys.exit("Case names must be unique.")

    cases = []
    for name, credentials_text, config_text in CASES:
        with tempfile.TemporaryDirectory() as directory:
            cases.append(build_case(directory, name, credentials_text, config_text))

    document = {
        "generator": "generate_shared_file_vectors.py",
        "botocore": botocore.__version__,
        "python": sys.version.split()[0],
        "cases": cases,
    }
    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as file:
        json.dump(document, file, indent=1, ensure_ascii=True)
        file.write("\n")
    print(f"Wrote {len(cases)} cases to {OUTPUT} (botocore {botocore.__version__}, python {document['python']})")


if __name__ == "__main__":
    main()
