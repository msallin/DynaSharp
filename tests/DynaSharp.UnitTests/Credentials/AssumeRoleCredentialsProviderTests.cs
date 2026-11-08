using System.Net;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Retries;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class AssumeRoleCredentialsProviderTests
{
    private const string RoleArn = "arn:aws:iam::123456789012:role/orders";
    private const string SourceKeyId = "AKIASOURCEEXAMPLE";

    private static readonly AwsRegion Region = AwsRegion.FromName("eu-west-1");
    private static readonly AwsCredentials Source = new(SourceKeyId, "source-secret", "source-token");

    [Test]
    public async Task Constructor_NullSource_ThrowsArgumentNullException()
    {
        // Arrange
        ICredentialsProvider source = null!;

        // Act
        var act = () => new AssumeRoleCredentialsProvider(source, RoleArn);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("sourceCredentials");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Constructor_NoRoleArn_ThrowsArgumentException(string? roleArn)
    {
        // Arrange

        // Act
        var act = () => new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), roleArn!);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithParameterName("roleArn");
    }

    [Test]
    [Arguments("a")]
    [Arguments("has space")]
    [Arguments("ümlaut")]
    public async Task Constructor_InvalidSessionName_ThrowsArgumentException(string sessionName)
    {
        // Arrange

        // Act
        var act = () => new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, sessionName);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessageContaining($"The session name {Sts.SessionNameRule}. (Parameter 'roleSessionName')", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_EmptyExternalId_ThrowsArgumentException()
    {
        // Arrange
        var options = new AssumeRoleOptions { ExternalId = string.Empty };

        // Act
        var act = () => new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, null, options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessageContaining("The external id must not be empty; null sends none. (Parameter 'options')", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(899)]
    [Arguments(43201)]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Constructor_DurationOutOfRange_ThrowsArgumentOutOfRangeException(int seconds)
    {
        // Arrange
        var options = new AssumeRoleOptions { Duration = TimeSpan.FromSeconds(seconds) };

        // Act
        var act = () => new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, null, options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("The duration of the session must be 15 minutes to 12 hours. (Parameter 'options')", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_DurationWithFractionOfASecond_ThrowsArgumentException()
    {
        // Arrange
        var options = new AssumeRoleOptions { Duration = TimeSpan.FromSeconds(900.5) };

        // Act
        var act = () => new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, null, options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessageContaining("The duration of the session must be a whole number of seconds. (Parameter 'options')", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(900)]
    [Arguments(43200)]
    public async Task Constructor_DurationAtTheLimits_IsAccepted(int seconds)
    {
        // Arrange
        var options = new AssumeRoleOptions { Duration = TimeSpan.FromSeconds(seconds), ExternalId = "ext", Region = Region };

        // Act
        var provider = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, "session", options);

        // Assert
        await Assert.That(provider).IsAssignableTo<IRefreshableCredentialsProvider>();
    }

    [Test]
    [Arguments("ftp://sts.example.com/")]
    [Arguments("https://sts.example.com/path")]
    [Arguments("https://user:pass@sts.example.com/")]
    public async Task Constructor_InvalidEndpoint_ThrowsArgumentExceptionWithoutTheValue(string endpoint)
    {
        // Arrange
        var options = new AssumeRoleOptions { StsEndpoint = new Uri(endpoint) };

        // Act
        var exception = TestClient.Catch(() => _ = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, null, options));

        // Assert
        await Assert.That((exception is ArgumentException, exception.Message.Contains("sts.example.com", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    [Test]
    public async Task GetCredentialsAsync_Success_ReturnsTheCredentialsOfTheRole()
    {
        // Arrange
        var world = new World();

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId}|{credentials.SecretAccessKey}|{credentials.SessionToken}|{credentials.Expiration:O}")
            .IsEqualTo($"{AccessKeyId}|{SecretAccessKey}|{SessionToken}|{world.Time.GetUtcNow().AddHours(1):O}");
    }

    [Test]
    public async Task GetCredentialsAsync_AllSettings_SendsTheFormOfBotocore()
    {
        // Arrange (the body botocore's sts client serializes for the same arguments, see SigV4SignerFormTests)
        var world = new World(Settings("dev@corp") with { DurationSeconds = 3600, ExternalId = "ext-id/1", RoleArn = "arn:aws:iam::123456789012:role/orders reader" });

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].BodyText).IsEqualTo(
            "Action=AssumeRole&Version=2011-06-15&RoleArn=arn%3Aaws%3Aiam%3A%3A123456789012%3Arole%2Forders+reader&RoleSessionName=dev%40corp&DurationSeconds=3600&ExternalId=ext-id%2F1");
    }

    [Test]
    public async Task GetCredentialsAsync_NoSessionName_SendsDynasharpAndTheUnixTime()
    {
        // Arrange
        var world = new World(Settings(null));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].BodyText).IsEqualTo(
            $"Action=AssumeRole&Version=2011-06-15&RoleArn=arn%3Aaws%3Aiam%3A%3A123456789012%3Arole%2Forders&RoleSessionName=dynasharp-{world.Time.GetUtcNow().ToUnixTimeSeconds()}");
    }

    [Test]
    public async Task GetCredentialsAsync_Request_IsSignedForStsInTheRegionWithTheSourceCredentials()
    {
        // Arrange
        var world = new World();

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = world.Handler.Requests[0];
        await Assert.That($"{request.RequestUri} {Header(request, "Authorization").Split(',')[0]} {Header(request, "X-Amz-Security-Token")}")
            .IsEqualTo("https://sts.eu-west-1.amazonaws.com/ AWS4-HMAC-SHA256 Credential=AKIASOURCEEXAMPLE/20261031/eu-west-1/sts/aws4_request source-token");
    }

    [Test]
    public async Task GetCredentialsAsync_Request_SignsTheHeadersOfAForm()
    {
        // Arrange
        var world = new World();

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(Header(world.Handler.Requests[0], "Authorization")).Contains("SignedHeaders=content-type;host;x-amz-date;x-amz-security-token, Signature=", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("us-east-1", "https://sts.amazonaws.com/", "us-east-1")]
    [Arguments("eu-west-1", "https://sts.amazonaws.com/", "us-east-1")]
    [Arguments("eu-central-2", "https://sts.eu-central-2.amazonaws.com/", "eu-central-2")]
    [Arguments("cn-north-1", "https://sts.cn-north-1.amazonaws.com.cn/", "cn-north-1")]
    public async Task GetCredentialsAsync_LegacyRegionalEndpoints_UsesTheGlobalEndpointForTheRegionsOfBotocore(string region, string endpoint, string signingRegion)
    {
        // Arrange
        var world = new World(Settings("s") with { Region = AwsRegion.FromName(region) }, Env(("AWS_STS_REGIONAL_ENDPOINTS", "legacy")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = world.Handler.Requests[0];
        await Assert.That($"{request.RequestUri} {Header(request, "Authorization").Split('/')[2]}").IsEqualTo($"{endpoint} {signingRegion}");
    }

    [Test]
    [Arguments("us-east-1", "https://sts.amazonaws.com/")]
    [Arguments("eu-central-2", "https://sts.eu-central-2.amazonaws.com/")]
    public async Task GetCredentialsAsync_LegacyRegionalEndpointsInTheProfile_UsesTheGlobalEndpointForTheRegionsOfBotocore(string region, string endpoint)
    {
        // Arrange
        var files = Files(("/home/a/.aws/config", "[default]\nsts_regional_endpoints = legacy\n"));
        var world = new World(Settings("s") with { Region = AwsRegion.FromName(region) }, Env(("HOME", "/home/a")), files: files);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo(endpoint);
    }

    [Test]
    public async Task GetCredentialsAsync_RegionalInTheVariableLegacyInTheProfile_UsesTheRegionalEndpoint()
    {
        // Arrange
        var files = Files(("/home/a/.aws/config", "[default]\nsts_regional_endpoints = legacy\n"));
        var world = new World(Settings("s") with { Region = AwsRegion.FromName("us-east-1") }, Env(("HOME", "/home/a"), ("AWS_STS_REGIONAL_ENDPOINTS", "regional")), files: files);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://sts.us-east-1.amazonaws.com/");
    }

    [Test]
    public async Task GetCredentialsAsync_MalformedConfigFile_UsesTheRegionalEndpoint()
    {
        // Arrange
        var world = new World(Settings("s") with { Region = AwsRegion.FromName("us-east-1") }, Env(("HOME", "/home/a")), files: Files(("/home/a/.aws/config", "oops\n")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://sts.us-east-1.amazonaws.com/");
    }

    [Test]
    [Arguments("AWS_ENDPOINT_URL_STS")]
    [Arguments("AWS_ENDPOINT_URL")]
    public async Task GetCredentialsAsync_EndpointInTheEnvironment_SendsThereAndSignsForTheRegion(string variable)
    {
        // Arrange
        var world = new World(Settings("s"), Env((variable, "https://vpce-1.sts.eu-west-1.vpce.amazonaws.com")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = world.Handler.Requests[0];
        await Assert.That($"{request.RequestUri} {Header(request, "Authorization").Split('/')[2]}").IsEqualTo("https://vpce-1.sts.eu-west-1.vpce.amazonaws.com/ eu-west-1");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointAndRegionInTheEnvironmentAndARegionGiven_SignsForTheRegionGiven()
    {
        // Arrange
        var world = new World(Settings("s"), Env(("AWS_ENDPOINT_URL_STS", "http://localhost:4566"), ("AWS_REGION", "ap-south-2")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(Header(world.Handler.Requests[0], "Authorization").Split('/')[2]).IsEqualTo(Region.Name);
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointInTheEnvironmentAndNoRegion_SignsForTheRegionOfTheEnvironment()
    {
        // Arrange
        var world = new World(Settings("s") with { Region = null }, Env(("AWS_ENDPOINT_URL_STS", "http://localhost:4566"), ("AWS_REGION", "ap-south-2")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(Header(world.Handler.Requests[0], "Authorization").Split('/')[2]).IsEqualTo("ap-south-2");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointInTheEnvironmentAndNoRegionAnywhere_SignsForUsEast1()
    {
        // Arrange (an emulator does not check the region)
        var world = new World(Settings("s") with { Region = null }, Env(("AWS_ENDPOINT_URL_STS", "http://localhost:4566")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(Header(world.Handler.Requests[0], "Authorization").Split('/')[2]).IsEqualTo("us-east-1");
    }

    [Test]
    public async Task GetCredentialsAsync_ExplicitEndpoint_GoesAheadOfTheEnvironment()
    {
        // Arrange
        var world = new World(Settings("s") with { Endpoint = new Uri("https://sts.internal.example/") }, Env(("AWS_ENDPOINT_URL_STS", "https://sts.other.example")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://sts.internal.example/");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointVariableIsInvalid_FailsNamingTheVariable()
    {
        // Arrange
        var world = new World(Settings("s"), Env(("AWS_ENDPOINT_URL_STS", "not a url")));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("AWS_ENDPOINT_URL_STS environment variable is not a valid endpoint", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_NoRegionAnywhere_FailsSayingThatTheEndpointNeedsOne()
    {
        // Arrange
        var world = new World(Settings("s") with { Region = null });

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("The STS endpoint depends on the region, and it is not known", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_RegionInTheEnvironment_IsUsedWhenNoneIsGiven()
    {
        // Arrange
        var world = new World(Settings("s") with { Region = null }, Env(("AWS_DEFAULT_REGION", "sa-east-1")));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://sts.sa-east-1.amazonaws.com/");
    }

    [Test]
    [Arguments("AccessDenied", "AccessDenied: the trust policy of the role, or a policy of the source, does not allow the source to assume it")]
    [Arguments("ExpiredToken", "ExpiredToken: the source credentials have expired")]
    [Arguments("InvalidClientTokenId", "InvalidClientTokenId: STS does not know the access key of the source credentials")]
    [Arguments("SignatureDoesNotMatch", "SignatureDoesNotMatch: the secret of the source credentials does not belong to its access key, or the local clock is off by more than five minutes")]
    [Arguments("RegionDisabledException", "RegionDisabledException: STS is not activated in this region of the account")]
    [Arguments("ValidationError", "ValidationError: a parameter is not valid, such as a duration beyond the maximum session duration of the role")]
    [Arguments("MalformedPolicyDocument", "MalformedPolicyDocument")]
    public async Task GetCredentialsAsync_Rejected_FailsNamingTheRoleAndTheCode(string code, string description)
    {
        // Arrange
        var world = new World();
        world.Respond(_ => Error(code, HttpStatusCode.Forbidden));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"STS did not let the source credentials assume the role {RoleArn}: {description}.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_Rejected_LeavesOutTheMessageOfSts()
    {
        // Arrange
        var world = new World();
        world.Respond(_ => Error("AccessDenied", HttpStatusCode.Forbidden, "User arn:aws:iam::123456789012:user/secret-name is not authorized"));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).DoesNotContain("secret-name", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden, "<html>proxy says no</html>", "HTTP 403")]
    [Arguments(HttpStatusCode.BadRequest, "<ErrorResponse><Error><Code></Code></Error></ErrorResponse>", "HTTP 400")]
    public async Task GetCredentialsAsync_RejectedWithoutCode_FailsNamingTheStatus(HttpStatusCode status, string body, string description)
    {
        // Arrange
        var world = new World();
        world.Respond(_ => new StubResponse { StatusCode = status, Body = body });

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining($"assume the role {RoleArn}: {description}.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SuccessWithoutAField_FailsNamingIt()
    {
        // Arrange
        var world = new World();
        world.Respond(_ => new StubResponse { Body = "<AssumeRoleResponse><AssumeRoleResult><Credentials><AccessKeyId>a</AccessKeyId></Credentials></AssumeRoleResult></AssumeRoleResponse>" });

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("STS returned an unusable response: the response does not contain SecretAccessKey, SessionToken, Expiration.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ServerErrorThenSuccess_RetriesAfterAPause()
    {
        // Arrange
        var world = new World();
        var answers = new Queue<StubResponse>([new StubResponse { StatusCode = HttpStatusCode.ServiceUnavailable, Body = string.Empty }, world.Success()]);
        world.Respond(_ => answers.Dequeue());

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(1);
        var credentials = await fetch;

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo((AccessKeyId, 2));
    }

    [Test]
    public async Task GetCredentialsAsync_ClockTenMinutesOff_SignsAgainWithTheCorrectedTime()
    {
        // Arrange (STS answers with its own Date, which is 10 minutes ahead of the clock that signed)
        var world = new World();
        var serverTime = world.Time.GetUtcNow().AddMinutes(10);
        world.Respond(request => world.Handler.Requests.Count == 1 ? Skewed(serverTime) : world.Success());

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((world.Handler.Requests.Count, Header(world.Handler.Requests[1], "X-Amz-Date"), credentials.AccessKeyId))
            .IsEqualTo((2, "20261031T124456Z", AccessKeyId));
    }

    [Test]
    public async Task GetCredentialsAsync_ClockTenMinutesOff_LogsTheCorrection()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(logs: logs);
        var serverTime = world.Time.GetUtcNow().AddMinutes(10);
        world.Respond(_ => world.Handler.Requests.Count == 1 ? Skewed(serverTime) : world.Success());

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Select(static entry => (entry.EventId.Id, entry.Level, entry.Message)).Single())
            .IsEqualTo((30, LogLevel.Warning, "The local clock differs from the STS clock by 00:10:00. The signing time is corrected and the request is sent again."));
    }

    [Test]
    public async Task GetCredentialsAsync_ClockWithinTolerance_FailsNamingTheClockAsACause()
    {
        // Arrange
        var world = new World();
        var serverTime = world.Time.GetUtcNow().AddMinutes(2);
        world.Respond(_ => Skewed(serverTime));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("or the local clock is off by more than five minutes", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ClockWithinTolerance_SendsOnce()
    {
        // Arrange
        var world = new World();
        var serverTime = world.Time.GetUtcNow().AddMinutes(2);
        world.Respond(_ => Skewed(serverTime));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_StsKeepsRejectingTheClock_SendsAgainOnceOnly()
    {
        // Arrange (every answer is 10 minutes ahead of the clock that signed the request before it)
        var world = new World();
        world.Respond(_ => Skewed(world.Time.GetUtcNow().AddMinutes(10 * world.Handler.Requests.Count)));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetCredentialsAsync_RejectionWithoutADate_SendsOnce()
    {
        // Arrange
        var world = new World();
        world.Respond(_ => Error("SignatureDoesNotMatch", HttpStatusCode.Forbidden));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_OtherRejectionWithASkewedDate_SendsOnce()
    {
        // Arrange
        var world = new World();
        var serverTime = world.Time.GetUtcNow().AddMinutes(10);
        world.Respond(_ => Skewed(serverTime, "AccessDenied"));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_AnswerThatIsNotXmlWithASkewedDate_SendsOnce()
    {
        // Arrange
        var world = new World();
        var serverTime = world.Time.GetUtcNow().AddMinutes(10);
        world.Respond(_ => new StubResponse { StatusCode = HttpStatusCode.BadRequest, Body = "not xml", Headers = new Dictionary<string, string> { ["Date"] = serverTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) } });

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_ClockThatCannotBeCorrected_SendsOnce()
    {
        // Arrange (a clock of a test, or of a provider that was built with another one)
        var world = new World(correctsClock: false);
        var serverTime = world.Time.GetUtcNow().AddMinutes(10);
        world.Respond(_ => Skewed(serverTime));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_HttpEndpointOnRemoteHost_WarnsOnceAcrossFetches()
    {
        // Arrange: an STS emulator in docker-compose, reached by its service name
        using var logs = new CapturingLoggerFactory(LogLevel.Warning);
        var world = new World(Settings("s") with { Endpoint = new Uri("http://sts-emulator:4566") }, logs: logs);
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Provider.TryInvalidate(first);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert (the drop logs event 23 besides)
        await Assert.That((world.Handler.Requests.Count, logs.Entries.Where(static entry => entry.EventId.Id != 23).Select(static entry => (entry.EventId.Id, entry.Level, entry.Message)).Single())).IsEqualTo((
            2,
            (25, LogLevel.Warning, "STS is reached at sts-emulator over plain http. The request, its token and the credentials in the response travel unencrypted.")));
    }

    [Test]
    [Arguments("http://localhost:4566")]
    [Arguments("http://127.0.0.1:4566")]
    [Arguments("https://sts.example.com")]
    public async Task GetCredentialsAsync_HttpsOrLoopbackEndpoint_LogsNoWarning(string endpoint)
    {
        // Arrange
        using var logs = new CapturingLoggerFactory(LogLevel.Warning);
        var world = new World(Settings("s") with { Endpoint = new Uri(endpoint) }, logs: logs);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries).IsEmpty();
    }

    [Test]
    public async Task GetCredentialsAsync_Retried_SignsEachAttemptAnew()
    {
        // Arrange (a signature carries its time, so a resend after a pause has one of its own)
        var world = new World();
        var answers = new Queue<StubResponse>([new StubResponse { StatusCode = HttpStatusCode.InternalServerError, Body = string.Empty }, world.Success()]);
        world.Respond(_ => answers.Dequeue());

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(1);
        await fetch;

        // Assert
        await Assert.That(Header(world.Handler.Requests[1], "X-Amz-Date")).IsEqualTo("20261031T123457Z");
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, "<ErrorResponse><Error><Code>Throttling</Code></Error></ErrorResponse>", "Throttling")]
    [Arguments(HttpStatusCode.OK, "not xml", "the response is not XML")]
    [Arguments(HttpStatusCode.TooManyRequests, "", "HTTP 429")]
    [Arguments(HttpStatusCode.BadGateway, "", "HTTP 502")]
    public async Task GetCredentialsAsync_TransientThreeTimes_FailsWithTheLastAttempt(HttpStatusCode status, string body, string lastFailure)
    {
        // Arrange
        var world = new World();
        world.Respond(_ => new StubResponse { StatusCode = status, Body = body });

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(2);
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(() => fetch);

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count))
            .IsEqualTo(($"STS at sts.eu-west-1.amazonaws.com did not return credentials in 3 attempts. The last attempt: {lastFailure}.", 3));
    }

    [Test]
    public async Task GetCredentialsAsync_NetworkFailsThreeTimes_FailsNamingTheError()
    {
        // Arrange
        var world = new World();
        world.Respond(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(2);
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(() => fetch);

        // Assert
        await Assert.That(exception.Message).EndsWith("The last attempt: failed (ConnectionError).", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SourceHasNoCredentials_FailsWithTheMessageOfTheSourceAndSendsNothing()
    {
        // Arrange
        var world = new World(source: new ThrowingProvider(new CredentialsUnavailableException("No credentials from the source.")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count))
            .IsEqualTo(($"Assuming the role {RoleArn} needs the credentials of its source, and there are none: No credentials from the source.", 0));
    }

    [Test]
    public async Task GetCredentialsAsync_SourceThrowsSomethingElse_LetsItThrough()
    {
        // Arrange (a bug of a provider of the application is not a missing credential)
        var world = new World(source: new ThrowingProvider(new InvalidOperationException("bug")));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwiceWhileValid_AssumesTheRoleOnce()
    {
        // Arrange
        var world = new World();
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), world.Handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_InTheMandatoryPeriod_AssumesTheRoleAgain()
    {
        // Arrange
        var world = new World();
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Time.Advance(TimeSpan.FromMinutes(51));

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo("ASIA2");
    }

    [Test]
    public async Task TryInvalidate_Current_AssumesTheRoleAgainOnTheNextCall()
    {
        // Arrange
        var world = new World();
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = world.Provider.TryInvalidate(rejected);
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, credentials.AccessKeyId)).IsEqualTo((true, "ASIA2"));
    }

    [Test]
    public async Task TryInvalidate_Current_LogsTheRoleWhoseCredentialsItDrops()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(logs: logs);
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        world.Provider.TryInvalidate(rejected);

        // Assert
        await Assert.That(logs.Entries.Single().Message)
            .IsEqualTo($"DynamoDB rejected the credentials from the role {RoleArn} as expired. They are dropped and fetched again.");
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn);

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredCredentialsFromSts_FailsAsAlreadyExpired()
    {
        // Arrange
        var world = new World();
        world.Respond(_ => world.Success(TimeSpan.FromMinutes(-1)));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"No credentials from the role {RoleArn}: it returned credentials that are already expired (check the system clock).", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_OverASocket_SendsTheSignedFormToTheLoopbackServer()
    {
        // Arrange (what a real HttpClient puts on the wire: the Host header must be the authority that was signed)
        using var server = new LoopbackHttpServer(StsSuccess(AccessKeyId, DateTimeOffset.UtcNow.AddHours(1)));
        var settings = Settings("s") with { Endpoint = server.Endpoint };
        var dependencies = new CredentialsDependencies(Env(), NoFiles, NoWrites, CredentialProcess.Runner, null, TimeProvider.System, NullLoggerFactory.Instance);
        var provider = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), settings, dependencies);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        server.Requests.TryPeek(out var request);
        await Assert.That((request!.RequestLine, request.Headers.Contains($"Host: {server.Endpoint.Authority}"), request.Headers.Contains("Content-Type: application/x-www-form-urlencoded; charset=utf-8")))
            .IsEqualTo(("POST / HTTP/1.1", true, true));
    }

    [Test]
    public async Task GetCredentialsAsync_OptionsOnTheStub_SendsTheRoleTheSessionTheDurationAndTheExternalId()
    {
        // Arrange (the public constructor, whose options become the form; the stub answers every attempt, as a busy machine may time one out)
        using var sts = new StubSts { DefaultResponse = StubStsResponse.Credentials(new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken)) };
        var options = new AssumeRoleOptions { ExternalId = "partner-7", Duration = TimeSpan.FromHours(2), Region = Region, StsEndpoint = sts.Endpoint };
        var provider = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, "dev-session", options);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = sts.Requests[0];
        await Assert.That((request.Action, request.RoleArn, request.RoleSessionName, request.DurationSeconds, request.ExternalId)).IsEqualTo(("AssumeRole", RoleArn, "dev-session", "7200", "partner-7"));
    }

    [Test]
    public async Task GetCredentialsAsync_AnswerOfTheStub_ReturnsTheCredentialsItWasGiven()
    {
        // Arrange
        var expiration = new DateTimeOffset(2100, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var sts = new StubSts { DefaultResponse = StubStsResponse.Credentials(new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken, expiration)) };
        var provider = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), RoleArn, options: new AssumeRoleOptions { Region = Region, StsEndpoint = sts.Endpoint });

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration)).IsEqualTo((AccessKeyId, SecretAccessKey, SessionToken, expiration));
    }

    [Test]
    public async Task GetCredentialsAsync_InternationalizedEndpoint_SignsTheHostThatIsSent()
    {
        // Arrange (the name does not resolve, so the connection goes to the loopback server)
        using var server = new LoopbackHttpServer(StsSuccess(AccessKeyId, DateTimeOffset.UtcNow.AddHours(1)));
        var port = server.Endpoint.Port;
        using var connecting = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Loopback, port, token);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            },
        };
        var settings = Settings("s") with { Endpoint = new Uri($"http://b\u00fccher.example:{port}/") };
        var dependencies = new CredentialsDependencies(Env(), NoFiles, NoWrites, CredentialProcess.Runner, connecting, TimeProvider.System, NullLoggerFactory.Instance);
        var provider = new AssumeRoleCredentialsProvider(new StaticCredentialsProvider(Source), settings, dependencies);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        server.Requests.TryPeek(out var request);
        await Assert.That(request!.Headers.Contains($"Host: xn--bcher-kva.example:{port}")).IsTrue();
    }

    internal static string StsSuccess(string accessKeyId, DateTimeOffset expiration) => $"""
        <AssumeRoleResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
          <AssumeRoleResult>
            <SourceIdentity>Alice</SourceIdentity>
            <AssumedRoleUser>
              <Arn>arn:aws:sts::123456789012:assumed-role/orders/s</Arn>
              <AssumedRoleId>ARO123EXAMPLE123:s</AssumedRoleId>
            </AssumedRoleUser>
            <Credentials>
              <AccessKeyId>{accessKeyId}</AccessKeyId>
              <SecretAccessKey>{SecretAccessKey}</SecretAccessKey>
              <SessionToken>{SessionToken}</SessionToken>
              <Expiration>{Timestamp(expiration)}</Expiration>
            </Credentials>
            <PackedPolicySize>6</PackedPolicySize>
          </AssumeRoleResult>
          <ResponseMetadata><RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId></ResponseMetadata>
        </AssumeRoleResponse>
        """;

    private static AssumeRoleCredentialsProvider.Settings Settings(string? sessionName = "s") => new(RoleArn, sessionName, null, null, Region, null);

    private static string Header(RecordedRequest request, string name) => request.Headers.First(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static StubResponse Skewed(DateTimeOffset serverTime, string code = "SignatureDoesNotMatch") => new()
    {
        StatusCode = HttpStatusCode.Forbidden,
        Body = Error(code, HttpStatusCode.Forbidden).Body,
        Headers = new Dictionary<string, string> { ["Date"] = serverTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) },
    };

    private static StubResponse Error(string code, HttpStatusCode status, string message = "The message.") => new()
    {
        StatusCode = status,
        Body = $"""<ErrorResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/"><Error><Type>Sender</Type><Code>{code}</Code><Message>{message}</Message></Error><RequestId>r</RequestId></ErrorResponse>""",
    };

    private sealed class ThrowingProvider(Exception exception) : ICredentialsProvider
    {
        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromException<AwsCredentials>(exception);
    }

    /// <summary>A source, STS and a clock. Each answer of STS has a new access key: <c>ASIAIOSFODNN7EXAMPLE</c>, then <c>ASIA2</c>.</summary>
    private sealed class World
    {
        private int _issued;
        private Func<RecordedRequest, StubResponse> _answer;

        public World(
            AssumeRoleCredentialsProvider.Settings? settings = null,
            Func<string, string?>? environment = null,
            ICredentialsProvider? source = null,
            ILoggerFactory? logs = null,
            bool correctsClock = true,
            ReadTextFile? files = null)
        {
            _answer = _ => Success();
            Handler = new StubHandler((request, _) => Task.FromResult(_answer(request)));
            var dependencies = new CredentialsDependencies(environment ?? Env(), files ?? NoFiles, NoWrites, CredentialProcess.Runner, Handler, correctsClock ? new ClockSkewCorrection(Time) : Time, logs ?? NullLoggerFactory.Instance);
            Provider = new AssumeRoleCredentialsProvider(source ?? new StaticCredentialsProvider(Source), settings ?? Settings(), dependencies);
        }

        public ManualTimeProvider Time { get; } = new();

        public StubHandler Handler { get; }

        public AssumeRoleCredentialsProvider Provider { get; }

        public void Respond(Func<RecordedRequest, StubResponse> answer) => _answer = answer;

        public StubResponse Success(TimeSpan? expiresIn = null)
        {
            var issued = Interlocked.Increment(ref _issued);
            return new StubResponse { Body = StsSuccess(issued == 1 ? AccessKeyId : $"ASIA{issued}", Time.GetUtcNow() + (expiresIn ?? TimeSpan.FromHours(1))) };
        }

        /// <summary>Lets the pauses between the attempts of a fetch pass. The timers are created in this order: attempt 1, pause 1, attempt 2, pause 2, ...</summary>
        public async Task AdvanceThroughPausesAsync(int pauses)
        {
            for (var pause = 1; pause <= pauses; pause++)
            {
                await WaitForTimersAsync(Time, 2 * pause);
                Time.Advance(TimeSpan.FromSeconds(1));
            }
        }
    }
}
