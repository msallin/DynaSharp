# Credentials

Without `Credentials` in the options, the client takes the first source of the [default chain](getting-started.md#credentials) that has credentials. This page covers the sources that take more than a variable, and what fails when one is broken.

## Use IRSA on EKS

[IRSA](glossary.md#irsa) is the second source of the chain:

- **Setup:** IRSA sets `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN` in the pod, so `new DynamoDbClient()` needs only a [region](getting-started.md#region) on top.
- **Exchange:** the client trades the token for credentials at STS with `AssumeRoleWithWebIdentity`, and renews them before they expire.
- **Rotation:** it reads the token file again for each exchange, because the kubelet replaces it.
- **Other issuers:** any other OpenID Connect issuer that the account trusts takes the same request.

## Use the instance metadata service

The last source of the chain is the role of an EC2 instance, from the instance metadata service. Each setting is read from its environment variable, then from the selected profile, as the AWS CLI does:

- **Endpoint:** `AWS_EC2_METADATA_SERVICE_ENDPOINT` or `ec2_metadata_service_endpoint` replaces `http://169.254.169.254`.
- **IPv6:** `AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE` or `ec2_metadata_service_endpoint_mode` set to `IPv6` selects `http://[fd00:ec2::254]`, unless an endpoint is set. `IPv4` is the default.
- **IMDSv1:** `AWS_EC2_METADATA_V1_DISABLED` or `ec2_metadata_v1_disabled` set to `true` refuses the fallback to version 1 when the token request fails.
- **Config file:** a malformed config file is ignored for these settings.

## Get credentials from a program

A profile with `credential_process` gets its credentials from a program, as the AWS CLI does:

```ini
[profile vault]
credential_process = /usr/local/bin/vault-creds --account prod
```

- **Start:** the setting is split into the program and its arguments, on Windows by the rules of the C runtime and elsewhere like a POSIX shell, and the program starts without a shell, so a pipe or a redirection needs `sh -c '...'` or `cmd /c ...` in the setting.
- **Output:** the program writes `{"Version": 1, "AccessKeyId": ..., "SecretAccessKey": ..., "SessionToken": ..., "Expiration": ...}` and exits with code 0. `SessionToken` and `Expiration` are optional.
- **Refresh:** credentials with an `Expiration` are fetched again before it, by running the program again.
- **Limits:** the program gets no input and 20 seconds, and is stopped with what it started after that.
- **Secrets:** what the program writes, to standard output and to standard error, appears in no message and no log. A failure names the profile and the exit code.

## Assume a role

A profile with `role_arn` assumes that role at STS, with the credentials of its `source_profile` or of its `credential_source` (`Environment`, `Ec2InstanceMetadata` or `EcsContainer`), as the AWS CLI does:

```ini
[profile prod]
role_arn = arn:aws:iam::123456789012:role/orders
source_profile = dev
role_session_name = alice
duration_seconds = 3600

[profile dev]
aws_access_key_id = AKIA...
aws_secret_access_key = ...
```

- **Chains:** a source profile that has a `role_arn` and no keys assumes its own role first, so roles chain. A profile that comes back fails as a loop, unless a profile names itself and has static keys.
- **Region:** STS is asked in the region of the client, otherwise in `AWS_REGION`, `AWS_DEFAULT_REGION` or the `region` of the profile. `AWS_ENDPOINT_URL_STS` and `AWS_STS_REGIONAL_ENDPOINTS` (or `sts_regional_endpoints` in the profile) work as for IRSA.
- **Refresh:** the session is renewed before it expires, by assuming the role again.
- **Clock:** if STS rejects a local clock more than 4 minutes off, the signing time is corrected from its answer and the request is sent once more.
- **MFA:** a profile with `mfa_serial` fails, because the code of the device would be needed at every refresh.
- **Web identity:** `web_identity_token_file` with `role_arn` in a profile federates like IRSA, with the token of that file.

`AssumeRoleCredentialsProvider` does the same in code, over any other provider:

```csharp
var credentials = new AssumeRoleCredentialsProvider(
    new ProfileCredentialsProvider("dev"),
    "arn:aws:iam::123456789012:role/orders",
    "alice",
    new AssumeRoleOptions { Region = AwsRegion.FromName("eu-central-2"), Duration = TimeSpan.FromHours(1) });
using var orders = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2"), Credentials = credentials });
```

## Sign in with IAM Identity Center

A profile of AWS IAM Identity Center (SSO), as `aws configure sso` writes it, gets the credentials of its permission set from the portal, with the token that `aws sso login` cached:

```ini
[profile dev]
sso_session = corp
sso_account_id = 123456789012
sso_role_name = ReadOnly

[sso-session corp]
sso_start_url = https://corp.awsapps.com/start
sso_region = eu-west-1
```

- **Token:** read from `~/.aws/sso/cache/`, at every refresh of the credentials, so a new `aws sso login` takes effect without a restart.
- **Refresh:** a token of an `sso-session` is renewed with its refresh token in its last 15 minutes and written back to the cache, as the AWS CLI does. The older form with `sso_start_url` and `sso_region` in the profile works too, without the renewal.
- **Sign-in:** when the token is missing or has expired, the error says to run `aws sso login --profile dev`.

`SsoCredentialsProvider` does the same in code: `new SsoCredentialsProvider("https://corp.awsapps.com/start", AwsRegion.FromName("eu-west-1"), "123456789012", "ReadOnly", "corp")`.

## Test without STS

`StubSts` of `DynaSharp.Testing` stands in for STS on a port of the machine, so a test of a web identity or an assumed role runs without AWS, see [STS](testing.md#sts).

## When credentials fail

A source that applies but is broken throws `CredentialsUnavailableException`, which the client never retries.

| Source | When |
|---|---|
| The environment | `AWS_ACCESS_KEY_ID` is set without `AWS_SECRET_ACCESS_KEY`, `AWS_CREDENTIAL_EXPIRATION` is not an ISO 8601 time, or that time has passed and the variables were not renewed. The message names the variable |
| A `credential_process` | The program of the profile could not be started, ran past 20 seconds, wrote more than 64 KB, exited with another code than 0, or wrote no valid credentials. The message names the profile and the exit code and leaves out what the program wrote: run the command in a shell to see it |
| Assuming a role | STS refused `AssumeRole` (the message says `STS did not let the source credentials assume the role ...` and names the code, such as `AccessDenied` for a trust policy that does not allow the source), the source has no credentials, or the profile of the role is configured wrongly: no `source_profile` or `credential_source`, both, a source profile that does not exist or has no credentials, a loop of profiles, an unknown `credential_source`, `mfa_serial`, or a `duration_seconds` outside 900 to 43200. The message names the profile and the setting |
| SSO | The cached token of `aws sso login` is missing, malformed or expired and could not be renewed, or the portal rejected it (`UnauthorizedException`), which says to run `aws sso login --profile ...`; the portal does not give the user the role (`ForbiddenException`, `ResourceNotFoundException`); or the profile is configured wrongly: an `sso_session` that does not exist, a setting that differs between the profile and its `sso-session`, a missing `sso_start_url`, `sso_region`, `sso_account_id` or `sso_role_name`. The message names the profile or the session |
| Web identity, with STS's `InvalidClientTokenId` | STS refused the `AssumeRoleWithWebIdentity` call of the web identity provider with that code, and the message says `STS rejected the web identity token with InvalidClientTokenId`. The provider tries again only on `IDPCommunicationError`, `Throttling`, HTTP 429 or 5xx and a failure of the network, so it gives up at once, and the client never retries the exception |
