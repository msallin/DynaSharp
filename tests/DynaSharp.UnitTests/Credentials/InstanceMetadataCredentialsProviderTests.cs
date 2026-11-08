using System.Diagnostics.CodeAnalysis;
using System.Net;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator gives the tests the same jitter as the provider under test.")]
public sealed class InstanceMetadataCredentialsProviderTests
{
    private const int Seed = 4711;
    private const string TokenPath = "/latest/api/token";
    private const string RolePath = "/latest/meta-data/iam/security-credentials/";

    // The first value that Random(Seed) hands out for the jitter of botocore (120 to 600 seconds).
    private static readonly TimeSpan Jitter = TimeSpan.FromSeconds(new Random(Seed).Next(120, 601));
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(15) + Jitter;
    private static readonly string ProfileHome = Path.Combine("/home", "a");

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_CreatesProviderForTheProcessEnvironment()
    {
        // Arrange (constructing neither reads the environment nor calls the network)

        // Act
        var provider = new InstanceMetadataCredentialsProvider();

        // Assert
        await Assert.That(provider).IsAssignableTo<ICredentialsProvider>();
    }

    [Test]
    public async Task GetCredentialsAsync_DocumentationSample_ReturnsAllFields()
    {
        // Arrange (the example output of the EC2 documentation; the clock is set to a time at which it is still valid)
        var time = new ManualTimeProvider(new DateTimeOffset(2017, 5, 17, 8, 0, 0, TimeSpan.Zero));
        var imds = new FakeImds
        {
            Credentials = _ => StubResponse.Json("""
                {
                  "Code" : "Success",
                  "LastUpdated" : "2012-04-26T16:39:16Z",
                  "Type" : "AWS-HMAC",
                  "AccessKeyId" : "ASIAIOSFODNN7EXAMPLE",
                  "SecretAccessKey" : "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                  "Token" : "token",
                  "Expiration" : "2017-05-17T15:09:54Z"
                }
                """),
        };
        var provider = Create(imds, time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo(("ASIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "token", new DateTimeOffset(2017, 5, 17, 15, 9, 54, TimeSpan.Zero)));
    }

    [Test]
    public async Task GetCredentialsAsync_InstanceMetadataV2_SendsTheThreeRequestsOfBotocore()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Describe()).IsEqualTo($"PUT {TokenPath}|GET {RolePath} token-v2|GET {RolePath}my-role token-v2");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenRequest_AsksForSixHours()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].Headers["x-aws-ec2-metadata-token-ttl-seconds"]).IsEqualTo("21600");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenWithTrailingNewline_SendsTheTrimmedToken()
    {
        // Arrange
        var imds = new FakeImds { Token = _ => StubResponse.Json("token-v2\n") };
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[1].Headers["x-aws-ec2-metadata-token"]).IsEqualTo("token-v2");
    }

    [Test]
    public async Task GetCredentialsAsync_RoleNameWithSpecialCharacters_IsEscapedInThePath()
    {
        // Arrange (IAM allows + = , . @ - _ in role names; a path separator must never come from the service)
        var imds = new FakeImds { Role = _ => StubResponse.Json("my role/x+y") };
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[2].RequestUri!.AbsolutePath).IsEqualTo(RolePath + "my%20role%2Fx%2By");
    }

    [Test]
    public async Task GetCredentialsAsync_RoleNameOnTheFirstOfSeveralLines_UsesThatLine()
    {
        // Arrange
        var imds = new FakeImds { Role = _ => StubResponse.Json("first-role\nsecond-role\n") };
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[2].RequestUri!.AbsolutePath).IsEqualTo(RolePath + "first-role");
    }

    [Test]
    [Arguments("true")]
    [Arguments("TRUE")]
    [Arguments("True")]
    public async Task GetCredentialsAsync_MetadataDisabled_FailsWithoutARequest(string value)
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_DISABLED", value)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: it is disabled by AWS_EC2_METADATA_DISABLED=true.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_MetadataDisabled_SendsNoRequest()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_DISABLED", "true")));
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var requests = imds.Handler.Requests.Count;

        // Assert
        await Assert.That(requests).IsEqualTo(0);
    }

    [Test]
    [Arguments("false")]
    [Arguments("")]
    [Arguments("1")]
    [Arguments("yes")]
    public async Task GetCredentialsAsync_MetadataDisabledWithAnotherValue_StillFetches(string value)
    {
        // Arrange (only "true" disables, as in botocore)
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_DISABLED", value)));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_DefaultEndpoint_IsTheLinkLocalAddress()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests.Select(request => request.RequestUri!.Authority).Distinct().Single()).IsEqualTo("169.254.169.254");
    }

    [Test]
    [Arguments("http://127.0.0.1:1338", "127.0.0.1:1338")]
    [Arguments("http://127.0.0.1:1338/", "127.0.0.1:1338")]
    [Arguments("https://imds.internal", "imds.internal")]
    public async Task GetCredentialsAsync_EndpointVariable_ReplacesTheEndpoint(string endpoint, string authority)
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT", endpoint)));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests.Select(request => request.RequestUri!.Authority).Distinct().Single()).IsEqualTo(authority);
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointVariableWithPath_KeepsThePath()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT", "http://proxy.internal:8080/imds/")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.AbsolutePath).IsEqualTo("/imds" + TokenPath);
    }

    [Test]
    public async Task GetCredentialsAsync_EmptyEndpointVariable_UsesTheDefault()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT", string.Empty)));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("169.254.169.254");
    }

    [Test]
    [Arguments("not a url")]
    [Arguments("ftp://imds.internal")]
    [Arguments("/relative")]
    [Arguments("http://user:hunter2@imds.internal")]
    public async Task GetCredentialsAsync_InvalidEndpointVariable_FailsWithoutEchoingIt(string endpoint)
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT", endpoint)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("AWS_EC2_METADATA_SERVICE_ENDPOINT or ec2_metadata_service_endpoint must be an absolute http or https URL without user info.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.MethodNotAllowed)]
    public async Task GetCredentialsAsync_TokenEndpointRefuses_FallsBackToInstanceMetadataV1(HttpStatusCode status)
    {
        // Arrange (403, 404 and 405 are what botocore treats as "no IMDSv2 here")
        var imds = new FakeImds { Token = _ => StubResponse.Json("no", status) };
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Describe()).IsEqualTo($"PUT {TokenPath}|GET {RolePath}|GET {RolePath}my-role");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenEndpointAnswersWithEmptyBody_FallsBackToInstanceMetadataV1()
    {
        // Arrange (an empty token is no token, as in botocore)
        var imds = new FakeImds { Token = _ => StubResponse.Json(string.Empty) };
        var provider = Create(imds);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Describe()).IsEqualTo($"PUT {TokenPath}|GET {RolePath}|GET {RolePath}my-role");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenRequestTimesOut_FallsBackToInstanceMetadataV1()
    {
        // Arrange (a hop limit that swallows the answer looks like a timeout)
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imds = new FakeImds
        {
            TokenAsync = async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return StubResponse.Json("unreachable");
            },
        };
        var provider = Create(imds, time);
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await started.Task;

        // Act
        time.Advance(TimeSpan.FromSeconds(1));
        await fetch;

        // Assert
        await Assert.That(imds.Describe()).IsEqualTo($"PUT {TokenPath}|GET {RolePath}|GET {RolePath}my-role");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenRequestNotYetOneSecond_KeepsWaiting()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imds = new FakeImds
        {
            TokenAsync = async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                return StubResponse.Json("token-v2");
            },
        };
        var provider = Create(imds, time);
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await started.Task;

        // Act
        time.Advance(TimeSpan.FromMilliseconds(999));
        release.SetResult();
        await fetch;

        // Assert
        await Assert.That(imds.Describe()).IsEqualTo($"PUT {TokenPath}|GET {RolePath} token-v2|GET {RolePath}my-role token-v2");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenUnavailableAndV1Disabled_FailsWithoutFurtherRequests()
    {
        // Arrange
        var imds = new FakeImds { Token = _ => StubResponse.Json("no", HttpStatusCode.Forbidden) };
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_V1_DISABLED", "true")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, imds.Handler.Requests.Count))
            .IsEqualTo(("No credentials from the EC2 instance metadata service: the IMDSv2 token is not available and IMDSv1 is disabled by AWS_EC2_METADATA_V1_DISABLED=true or ec2_metadata_v1_disabled=true.", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_TokenUnavailableAndV1DisabledInTheProfile_FailsWithoutFurtherRequests()
    {
        // Arrange
        var imds = new FakeImds { Token = _ => StubResponse.Json("no", HttpStatusCode.Forbidden) };
        var provider = Create(imds, environment: Env(("HOME", ProfileHome)), files: ProfileFile("ec2_metadata_v1_disabled = true"));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, imds.Handler.Requests.Count))
            .IsEqualTo(("No credentials from the EC2 instance metadata service: the IMDSv2 token is not available and IMDSv1 is disabled by AWS_EC2_METADATA_V1_DISABLED=true or ec2_metadata_v1_disabled=true.", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_V1DisabledInTheProfileButNotInTheVariable_FallsBackToV1()
    {
        // Arrange (the variable goes ahead of the profile)
        var imds = new FakeImds { Token = _ => StubResponse.Json("no", HttpStatusCode.Forbidden) };
        var provider = Create(imds, environment: Env(("HOME", ProfileHome), ("AWS_EC2_METADATA_V1_DISABLED", "false")), files: ProfileFile("ec2_metadata_v1_disabled = true"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointInTheProfile_ReplacesTheEndpoint()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("HOME", ProfileHome)), files: ProfileFile("ec2_metadata_service_endpoint = http://profile.internal:8080"));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("profile.internal:8080");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointInTheVariableAndTheProfile_TheVariableWins()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("HOME", ProfileHome), ("AWS_EC2_METADATA_SERVICE_ENDPOINT", "http://variable.internal")), files: ProfileFile("ec2_metadata_service_endpoint = http://profile.internal"));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("variable.internal");
    }

    [Test]
    [Arguments("IPv6", "[fd00:ec2::254]")]
    [Arguments("ipv6", "[fd00:ec2::254]")]
    [Arguments("IPv4", "169.254.169.254")]
    [Arguments("ipv4", "169.254.169.254")]
    public async Task GetCredentialsAsync_EndpointModeInTheVariable_SelectsTheDefaultEndpoint(string mode, string authority)
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE", mode)));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo(authority);
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointModeInTheProfile_SelectsTheIpv6Endpoint()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("HOME", ProfileHome)), files: ProfileFile("ec2_metadata_service_endpoint_mode = IPv6"));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("[fd00:ec2::254]");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointAndMode_TheEndpointWins()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT", "http://variable.internal"), ("AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE", "IPv6")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("variable.internal");
    }

    [Test]
    public async Task GetCredentialsAsync_InvalidEndpointMode_FailsNamingTheVariableAndTheSetting()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE", "IPv5")));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE or ec2_metadata_service_endpoint_mode must be IPv4 or IPv6.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_MalformedConfigFile_UsesTheDefaultEndpoint()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("HOME", ProfileHome)), files: Files((Path.Combine(ProfileHome, ".aws", "config"), "oops\n")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("169.254.169.254");
    }

    [Test]
    public async Task GetCredentialsAsync_NoConfigFile_UsesTheDefaultEndpoint()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("HOME", ProfileHome)));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests[0].RequestUri!.Authority).IsEqualTo("169.254.169.254");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenAvailableAndV1Disabled_Fetches()
    {
        // Arrange (the setting only matters when the token is not available)
        var imds = new FakeImds();
        var provider = Create(imds, environment: Env(("AWS_EC2_METADATA_V1_DISABLED", "true")));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, "the token request HTTP 400")]
    [Arguments(HttpStatusCode.InternalServerError, "the token request HTTP 500")]
    [Arguments(HttpStatusCode.Unauthorized, "the token request HTTP 401")]
    public async Task GetCredentialsAsync_TokenEndpointFails_EndsTheFetchWithoutFallback(HttpStatusCode status, string reason)
    {
        // Arrange (only 403, 404, 405 and a timeout lead to IMDSv1)
        var imds = new FakeImds { Token = _ => StubResponse.Json("no", status) };
        var provider = Create(imds);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, imds.Handler.Requests.Count)).IsEqualTo(($"No credentials from the EC2 instance metadata service: {reason}.", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ConnectionRefused_EndsTheFetchWithoutFallback()
    {
        // Arrange (not running on EC2 is the normal case for this)
        var imds = new FakeImds { Token = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (169.254.169.254:80)") };
        var provider = Create(imds);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, imds.Handler.Requests.Count))
            .IsEqualTo(("No credentials from the EC2 instance metadata service: the token request failed (ConnectionError).", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_NoRoleAttached_SaysSo()
    {
        // Arrange
        var imds = new FakeImds { Role = _ => StubResponse.Json("not found", HttpStatusCode.NotFound) };
        var provider = Create(imds);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: no IAM role is attached to the instance.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_RoleRequestFails_NamesTheStatus()
    {
        // Arrange
        var imds = new FakeImds { Role = _ => StubResponse.Json("error", HttpStatusCode.InternalServerError) };
        var provider = Create(imds);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: the request for the role name HTTP 500.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_RoleRequestConnectionFails_NamesTheError()
    {
        // Arrange
        var imds = new FakeImds { Role = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "closed") };
        var provider = Create(imds);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: the request for the role name failed (ConnectionError).", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("")]
    [Arguments("\n")]
    [Arguments("   \r\n")]
    [Arguments("\nmy-role\n")]
    public async Task GetCredentialsAsync_EmptyRoleName_FailsWithoutAThirdRequest(string body)
    {
        // Arrange
        var imds = new FakeImds { Role = _ => StubResponse.Json(body) };
        var provider = Create(imds);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, imds.Handler.Requests.Count)).IsEqualTo(("No credentials from the EC2 instance metadata service: the service returned no role name.", 2));
    }

    [Test]
    public async Task GetCredentialsAsync_RoleRequestTimesOut_FailsWithoutRetry()
    {
        // Arrange (one attempt of one second per request)
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imds = new FakeImds
        {
            RoleAsync = async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return StubResponse.Json("unreachable");
            },
        };
        var provider = Create(imds, time);
        var fetch = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await started.Task;

        // Act
        time.Advance(TimeSpan.FromSeconds(1));
        var exception = await fetch;

        // Assert
        await Assert.That((exception.Message, imds.Handler.Requests.Count))
            .IsEqualTo(("No credentials from the EC2 instance metadata service: the request for the role name timed out.", 2));
    }

    [Test]
    public async Task GetCredentialsAsync_CredentialsRequestFails_NamesTheStatus()
    {
        // Arrange
        var imds = new FakeImds { Credentials = _ => StubResponse.Json("error", HttpStatusCode.ServiceUnavailable) };
        var provider = Create(imds);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: the request for the credentials HTTP 503.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_CredentialsResponseIsNoJson_Fails()
    {
        // Arrange
        var imds = new FakeImds { Credentials = _ => StubResponse.Json("<html>Bad Gateway</html>") };
        var provider = Create(imds);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: the response is not a JSON object.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_RoleCannotBeAssumed_NamesTheCodeOfTheService()
    {
        // Arrange (HTTP 200 with an error document, as the service answers when EC2 is not a trusted entity of the role)
        var imds = new FakeImds { Credentials = _ => StubResponse.Json("""{"Code":"AssumeRoleUnauthorizedAccess","Message":"EC2 cannot assume the role my-role."}""") };
        var provider = Create(imds);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the EC2 instance metadata service: the response does not contain AccessKeyId, SecretAccessKey, Token, Expiration (the service returned the code AssumeRoleUnauthorizedAccess).", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_CredentialsWithoutExpiration_FailsWithoutEchoingTheKeys()
    {
        // Arrange
        var imds = new FakeImds { Credentials = _ => StubResponse.Json($$"""{"AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}","Token":"{{SessionToken}}"}""") };
        var provider = Create(imds);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("No credentials from the EC2 instance metadata service: the response does not contain Expiration.");
    }

    [Test]
    public async Task GetCredentialsAsync_EverythingFails_EachRequestIsSentOnce()
    {
        // Arrange (single attempt: botocore uses one attempt for IMDS)
        var imds = new FakeImds { Credentials = _ => StubResponse.Json("error", HttpStatusCode.InternalServerError) };
        var provider = Create(imds);
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var requests = imds.Handler.Requests.Count;

        // Assert
        await Assert.That(requests).IsEqualTo(3);
    }

    [Test]
    public async Task GetCredentialsAsync_FailureThenServiceRecovers_FailureIsNotCached()
    {
        // Arrange
        var imds = new FakeImds { Role = _ => StubResponse.Json("error", HttpStatusCode.InternalServerError) };
        var provider = Create(imds);
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();
        imds.Role = _ => StubResponse.Json("my-role");

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task TryGetCredentialsAsync_ServiceUnavailable_ReturnsTheReasonInsteadOfThrowing()
    {
        // Arrange
        var imds = new FakeImds { Role = _ => StubResponse.Json("not found", HttpStatusCode.NotFound) };
        var provider = Create(imds);

        // Act
        var outcome = await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Reason).IsEqualTo("no IAM role is attached to the instance");
    }

    [Test]
    public async Task TryGetCredentialsAsync_BrokenEndpointVariable_Throws()
    {
        // Arrange (a source that applies but is misconfigured stops the default chain)
        var provider = Create(new FakeImds(), environment: Env(("AWS_EC2_METADATA_SERVICE_ENDPOINT", "nonsense")));

        // Act
        var act = async () => await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwiceWithLongValidity_ReturnsTheSameInstanceWithOneFetch()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), imds.Handler.Requests.Count)).IsEqualTo((true, 3));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpirationFarAway_IsTakenAsItIs()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var expiration = time.GetUtcNow().AddHours(6);
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(expiration)) };
        var provider = Create(imds, time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(expiration);
    }

    [Test]
    public async Task GetCredentialsAsync_ExpirationSoonerThanTheRefreshWindow_IsExtendedToTheWindow()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddMinutes(5))) };
        var provider = Create(imds, time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(time.GetUtcNow() + RefreshWindow);
    }

    [Test]
    public async Task GetCredentialsAsync_ExpirationOneSecondBeyondTheRefreshWindow_IsNotExtended()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var expiration = time.GetUtcNow() + RefreshWindow + TimeSpan.FromSeconds(1);
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(expiration)) };
        var provider = Create(imds, time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(expiration);
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredCredentials_AreExtendedLikeBotocoreDoes()
    {
        // Arrange (a service that hands out credentials that have just expired has an availability problem; botocore keeps trying)
        var time = new ManualTimeProvider();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddMinutes(-1))) };
        var provider = Create(imds, time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(time.GetUtcNow() + RefreshWindow);
    }

    [Test]
    public async Task GetCredentialsAsync_ExtendedCredentials_AreNotFetchedAgainRightAway()
    {
        // Arrange (this is what keeps a busy process from asking the service with every request)
        var time = new ManualTimeProvider();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddMinutes(5))) };
        var provider = Create(imds, time);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), imds.Handler.Requests.Count)).IsEqualTo((true, 3));
    }

    [Test]
    public async Task GetCredentialsAsync_NoRandomGiven_ExtendsByBetweenSeventeenAndTwentyFiveMinutes()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddMinutes(5))) };
        var provider = new InstanceMetadataCredentialsProvider(Env(), NoFiles, imds.Handler, time, NullLogger.Instance, null);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var extension = credentials.Expiration!.Value - time.GetUtcNow();
        await Assert.That(extension >= TimeSpan.FromMinutes(17) && extension <= TimeSpan.FromMinutes(25)).IsTrue();
    }

    [Test]
    public async Task GetCredentialsAsync_AdvisoryRefresh_ReturnsNewCredentialsFromANewFetch()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var counter = 0;
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddHours(6), accessKeyId: $"ASIAKEY{counter++}")) };
        var provider = Create(imds, time);
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(14));

        // The stub completes at once, so the refresh this call starts is done when it returns.
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var refreshed = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((refreshed.AccessKeyId, imds.Handler.Requests.Count)).IsEqualTo(("ASIAKEY1", 6));
    }

    [Test]
    public async Task GetCredentialsAsync_FifteenMinutesBeforeExpiration_DoesNotRefreshYet()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddHours(6))) };
        var provider = Create(imds, time);
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(15));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(imds.Handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetCredentialsAsync_AdvisoryRefreshFails_KeepsTheCredentialsAndLogsAWarning()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddHours(6))) };
        var provider = Create(imds, time, logger: logs.CreateLogger("test"));
        var first = await provider.GetCredentialsAsync(CancellationToken.None);
        imds.Role = _ => StubResponse.Json("error", HttpStatusCode.InternalServerError);
        time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(12));

        // Act
        var duringFailure = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, duringFailure), logs.Entries.Select(entry => (entry.EventId.Id, entry.Level)).Single())).IsEqualTo((true, (21, LogLevel.Warning)));
    }

    [Test]
    public async Task GetCredentialsAsync_AdvisoryRefreshFails_WarningHasNoSecret()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddHours(6))) };
        var provider = Create(imds, time, logger: logs.CreateLogger("test"));
        await provider.GetCredentialsAsync(CancellationToken.None);
        imds.Credentials = _ => StubResponse.Json($$"""{"Code":"Success","AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}","Token":"{{SessionToken}}"}""");
        time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(12));
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var logged = string.Join('\n', logs.Entries.Select(entry => entry.Message));

        // Assert
        await Assert.That(new[] { AccessKeyId, SecretAccessKey, SessionToken, "token-v2" }.Any(secret => logged.Contains(secret, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task GetCredentialsAsync_MandatoryRefreshFails_ThrowsCredentialsUnavailableException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var imds = new FakeImds { Credentials = _ => StubResponse.Json(InstanceMetadataJson(time.GetUtcNow().AddHours(6))) };
        var provider = Create(imds, time);
        await provider.GetCredentialsAsync(CancellationToken.None);
        imds.Role = _ => StubResponse.Json("error", HttpStatusCode.InternalServerError);
        time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(9));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("Refreshing the credentials from the EC2 instance metadata service failed: the request for the role name HTTP 500.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ManyCallersAtTheSameTime_ShareOneFetch()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imds = new FakeImds
        {
            TokenAsync = async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return StubResponse.Json("token-v2");
            },
        };
        var provider = Create(imds);
        var callers = Enumerable.Range(0, 8).Select(_ => provider.GetCredentialsAsync(CancellationToken.None).AsTask()).ToArray();
        await started.Task;

        // Act
        release.SetResult();
        var results = await Task.WhenAll(callers);

        // Assert
        await Assert.That((results.Distinct().Count(), imds.Handler.Requests.Count)).IsEqualTo((1, 3));
    }

    [Test]
    public async Task GetCredentialsAsync_OneCallerCancels_TheFetchContinuesForTheOthers()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imds = new FakeImds
        {
            TokenAsync = async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return StubResponse.Json("token-v2");
            },
        };
        var provider = Create(imds);
        using var cancel = new CancellationTokenSource();
        var cancelled = provider.GetCredentialsAsync(cancel.Token).AsTask();
        var other = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await started.Task;
        await cancel.CancelAsync();
        await Assert.That(async () => await cancelled).Throws<OperationCanceledException>();

        // Act
        release.SetResult();
        var credentials = await other;

        // Assert
        await Assert.That((credentials.AccessKeyId, imds.Handler.Requests.Count)).IsEqualTo((AccessKeyId, 3));
    }

    [Test]
    public async Task TryInvalidate_TheCurrentCredentials_ReturnsTrueAndTheNextCallFetches()
    {
        // Arrange
        var imds = new FakeImds();
        var provider = Create(imds);
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var next = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert (three requests per fetch)
        await Assert.That((invalidated, ReferenceEquals(next, rejected), imds.Handler.Requests.Count)).IsEqualTo((true, false, 6));
    }

    [Test]
    public async Task TryInvalidate_ServiceHasNoRoleAnyMore_TheNextCallThrowsCredentialsUnavailableException()
    {
        // Arrange (a problem of the service counts as "does not apply", which a provider used on its own reports as a failure)
        var imds = new FakeImds();
        var provider = Create(imds);
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);
        provider.TryInvalidate(rejected);
        imds.Role = _ => StubResponse.Json("not found", HttpStatusCode.NotFound);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("no IAM role is attached", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = Create(new FakeImds());

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    private static ReadTextFile ProfileFile(string settings) => Files((Path.Combine(ProfileHome, ".aws", "config"), "[default]\n" + settings + "\n"));

    private static InstanceMetadataCredentialsProvider Create(
        FakeImds imds,
        ManualTimeProvider? time = null,
        Func<string, string?>? environment = null,
        ILogger? logger = null,
        Random? random = null,
        ReadTextFile? files = null) =>
        new(environment ?? Env(), files ?? NoFiles, imds.Handler, time ?? imds.Time, logger ?? NullLogger.Instance, random ?? new Random(Seed));

    /// <summary>A fake instance metadata service. Each answer can be replaced by the test.</summary>
    private sealed class FakeImds
    {
        public FakeImds()
        {
            Handler = new StubHandler(RespondAsync);
        }

        public ManualTimeProvider Time { get; } = new();

        public StubHandler Handler { get; }

        public Func<RecordedRequest, StubResponse> Token { get; set; } = _ => StubResponse.Json("token-v2");

        public Func<RecordedRequest, StubResponse> Role { get; set; } = _ => StubResponse.Json("my-role\n");

        public Func<RecordedRequest, StubResponse> Credentials { get; set; } = _ => StubResponse.Json(InstanceMetadataJson(new DateTimeOffset(2026, 10, 31, 18, 34, 56, TimeSpan.Zero)));

        public Func<RecordedRequest, CancellationToken, Task<StubResponse>>? TokenAsync { get; set; }

        public Func<RecordedRequest, CancellationToken, Task<StubResponse>>? RoleAsync { get; set; }

        /// <summary>The requests as <c>METHOD path</c>, with the token of the request if it has one, joined with bars.</summary>
        public string Describe() => string.Join(
            '|',
            Handler.Requests.Select(request => request.Headers.TryGetValue("x-aws-ec2-metadata-token", out var token)
                ? $"{request.Method} {request.RequestUri!.AbsolutePath} {token}"
                : $"{request.Method} {request.RequestUri!.AbsolutePath}"));

        private async Task<StubResponse> RespondAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                return TokenAsync is null ? Token(request) : await TokenAsync(request, cancellationToken);
            }

            if (path.EndsWith(RolePath, StringComparison.Ordinal))
            {
                return RoleAsync is null ? Role(request) : await RoleAsync(request, cancellationToken);
            }

            return Credentials(request);
        }
    }
}
