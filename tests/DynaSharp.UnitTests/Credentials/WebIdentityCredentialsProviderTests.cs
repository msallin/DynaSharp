using System.Net;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Retries;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class WebIdentityCredentialsProviderTests
{
    private const string TokenPath = "/var/run/secrets/eks.amazonaws.com/serviceaccount/token";
    private const string RoleArn = "arn:aws:iam::123456789012:role/orders";

    // A recognizable token, so that a test can look for it where it must not appear.
    private const string Token = "eyJhbGciOiJSUzI1NiJ9.secret-payload-5d1a3e9b.signature";
    private const string RotatedToken = "eyJhbGciOiJSUzI1NiJ9.rotated-payload-7c2e4f10.signature";

    private static readonly AwsRegion Region = AwsRegion.FromName("eu-west-1");

    private static readonly (string Name, string Value)[] Irsa = [("AWS_WEB_IDENTITY_TOKEN_FILE", TokenPath), ("AWS_ROLE_ARN", RoleArn)];

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_CreatesProviderForTheProcessEnvironment()
    {
        // Arrange (constructing neither reads the environment, nor the file, nor calls the network)

        // Act
        var provider = new WebIdentityCredentialsProvider();

        // Assert
        await Assert.That(provider).IsAssignableTo<IRefreshableCredentialsProvider>();
    }

    [Test]
    public async Task Constructor_ExplicitSettings_CreatesProviderWithoutTouchingAnything()
    {
        // Act
        var provider = new WebIdentityCredentialsProvider(TokenPath, RoleArn, Region, "orders-session");

        // Assert
        await Assert.That(provider).IsAssignableTo<IRefreshableCredentialsProvider>();
    }

    [Test]
    public async Task Constructor_NoSessionName_CreatesProvider()
    {
        // Act
        var provider = new WebIdentityCredentialsProvider(TokenPath, RoleArn, Region);

        // Assert
        await Assert.That(provider).IsAssignableTo<IRefreshableCredentialsProvider>();
    }

    [Test]
    public async Task Constructor_EmptyTokenFile_ThrowsArgumentException()
    {
        // Act
        var act = () => new WebIdentityCredentialsProvider(string.Empty, RoleArn, Region);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithParameterName("tokenFile");
    }

    [Test]
    public async Task Constructor_EmptyRole_ThrowsArgumentException()
    {
        // Act
        var act = () => new WebIdentityCredentialsProvider(TokenPath, string.Empty, Region);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithParameterName("roleArn");
    }

    [Test]
    public async Task Constructor_NoRegion_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new WebIdentityCredentialsProvider(TokenPath, RoleArn, null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("region");
    }

    [Test]
    public async Task Constructor_InvalidSessionName_ThrowsAndSaysTheRule()
    {
        // Act
        var act = () => new WebIdentityCredentialsProvider(TokenPath, RoleArn, Region, "not valid");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("must be 2 to 64 letters, digits or characters of _+=,.@-", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryGetCredentialsAsync_TokenFileVariableNotSet_DoesNotApply()
    {
        // Arrange
        var world = new World(Env(("AWS_ROLE_ARN", RoleArn)));

        // Act
        var outcome = await world.Provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Kind, outcome.Reason, world.Handler.Requests.Count)).IsEqualTo((CredentialsOutcomeKind.DoesNotApply, (string?)"AWS_WEB_IDENTITY_TOKEN_FILE is not set", 0));
    }

    [Test]
    public async Task TryGetCredentialsAsync_EmptyTokenFileVariable_CountsAsUnset()
    {
        // Arrange
        var world = new World(Env(("AWS_WEB_IDENTITY_TOKEN_FILE", string.Empty), ("AWS_ROLE_ARN", RoleArn)));

        // Act
        var outcome = await world.Provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Kind).IsEqualTo(CredentialsOutcomeKind.DoesNotApply);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileVariableNotSet_ThrowsAndSaysSo()
    {
        // Arrange
        var world = new World(Env());

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the web identity token file: AWS_WEB_IDENTITY_TOKEN_FILE is not set.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileWithoutRole_ThrowsWithoutARequest()
    {
        // Arrange (botocore raises InvalidConfigError here)
        var world = new World(Env(("AWS_WEB_IDENTITY_TOKEN_FILE", TokenPath)));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.StartsWith("AWS_WEB_IDENTITY_TOKEN_FILE is set but AWS_ROLE_ARN is not", StringComparison.Ordinal), world.Handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_EmptyRoleVariable_CountsAsUnset()
    {
        // Arrange
        var world = new World(Env(("AWS_WEB_IDENTITY_TOKEN_FILE", TokenPath), ("AWS_ROLE_ARN", string.Empty)));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    public async Task GetCredentialsAsync_Irsa_ReturnsTheCredentialsOfTheResponse()
    {
        // Arrange
        var world = new World();

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo((AccessKeyId, SecretAccessKey, SessionToken, world.Time.GetUtcNow().AddHours(1)));
    }

    [Test]
    public async Task GetCredentialsAsync_Irsa_SendsTheRoleAndTheTokenOfTheFile()
    {
        // Arrange
        var world = new World();

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Sent().Single()).IsEqualTo((RoleArn, "dynasharp-1793450096", Token));
    }

    [Test]
    public async Task GetCredentialsAsync_Irsa_PostsToTheRegionOfTheClient()
    {
        // Arrange (AWS_REGION names another region: the client's own configuration wins)
        var world = new World(Env([.. Irsa, ("AWS_REGION", "us-west-2")]));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("https://sts.eu-west-1.amazonaws.com/");
    }

    [Test]
    public async Task GetCredentialsAsync_ChinaRegion_UsesTheDomainOfThePartition()
    {
        // Arrange
        var world = new World(region: AwsRegion.FromName("cn-north-1"));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("https://sts.cn-north-1.amazonaws.com.cn/");
    }

    [Test]
    [Arguments("legacy", "https://sts.amazonaws.com/")]
    [Arguments("LEGACY", "https://sts.amazonaws.com/")]
    [Arguments("regional", "https://sts.eu-west-1.amazonaws.com/")]
    [Arguments("something-else", "https://sts.eu-west-1.amazonaws.com/")]
    public async Task GetCredentialsAsync_RegionalEndpointsVariable_SelectsTheEndpoint(string value, string expected)
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_STS_REGIONAL_ENDPOINTS", value)]));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo(expected);
    }

    [Test]
    public async Task GetCredentialsAsync_LegacyInARegionThatHasNoGlobalEndpoint_StaysRegional()
    {
        // Arrange (a token of an opt-in region is not valid at the global endpoint, and China has none)
        var world = new World(Env([.. Irsa, ("AWS_STS_REGIONAL_ENDPOINTS", "legacy")]), region: AwsRegion.FromName("me-south-1"));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("https://sts.me-south-1.amazonaws.com/");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointVariableBesideExplicitSettings_StillGoesThere()
    {
        // Arrange (the settings of the constructor replace the variables that describe the role, and neither says where STS is, as with AWS_STS_REGIONAL_ENDPOINTS)
        var world = new World(Env(("AWS_ENDPOINT_URL_STS", "http://127.0.0.1:4566")), settings: new WebIdentityCredentialsProvider.Settings(TokenPath, RoleArn, Region, null));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("http://127.0.0.1:4566/");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointVariableChanges_IsReadAtEveryFetch()
    {
        // Arrange
        var endpoint = "http://127.0.0.1:4566";
        var world = new World(name => name == "AWS_ENDPOINT_URL_STS" ? endpoint : Env(Irsa)(name));
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        endpoint = "http://127.0.0.1:4567";
        world.Provider.TryInvalidate(first);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[^1].RequestUri!.AbsoluteUri).IsEqualTo("http://127.0.0.1:4567/");
    }

    [Test]
    public async Task GetCredentialsAsync_FailuresAtAConfiguredEndpoint_NameItsHost()
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_ENDPOINT_URL_STS", "http://127.0.0.1:4566")])) { Answer = (_, _) => Task.FromResult(Error("Throttling", HttpStatusCode.BadRequest)) };

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        await world.AdvanceThroughPausesAsync(2);
        var exception = await failing;

        // Assert
        await Assert.That(exception.Message).IsEqualTo("STS at 127.0.0.1 did not return credentials in 3 attempts. The last attempt: Throttling.");
    }

    [Test]
    [Arguments("AWS_ENDPOINT_URL_STS", "not a url", "The AWS_ENDPOINT_URL_STS environment variable is not a valid endpoint: it is not an absolute URL.")]
    [Arguments("AWS_ENDPOINT_URL_STS", "ftp://sts.example.com", "The AWS_ENDPOINT_URL_STS environment variable is not a valid endpoint: the scheme must be http or https.")]
    [Arguments("AWS_ENDPOINT_URL", "https://sts.example.com/base", "The AWS_ENDPOINT_URL environment variable is not a valid endpoint: it must not have a path.")]
    [Arguments("AWS_ENDPOINT_URL", "https://key:secret@sts.example.com", "The AWS_ENDPOINT_URL environment variable is not a valid endpoint: it must not have user info.")]
    public async Task GetCredentialsAsync_InvalidEndpointVariable_ThrowsNamingItWithoutItsValueOrARequest(string variable, string value, string message)
    {
        // Arrange (a token sent to the wrong place cannot be taken back, so a value that makes no sense is no reason to fall back on the region)
        var world = new World(Env([.. Irsa, (variable, value)]));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo((message, 0));
    }

    [Test]
    [Arguments("AWS_REGION", "ap-southeast-2")]
    [Arguments("AWS_DEFAULT_REGION", "sa-east-1")]
    public async Task GetCredentialsAsync_NoClient_FindsTheRegionInTheEnvironment(string variable, string region)
    {
        // Arrange
        var world = new World(Env([.. Irsa, (variable, region)]), region: null);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.Host).IsEqualTo($"sts.{region}.amazonaws.com");
    }

    [Test]
    public async Task GetCredentialsAsync_NoClient_FindsTheRegionInTheProfile()
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_CONFIG_FILE", "/config"), ("AWS_SHARED_CREDENTIALS_FILE", "/credentials")]), region: null);
        world.Files["/config"] = "[default]\nregion = ca-central-1\n";

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Single().RequestUri!.Host).IsEqualTo("sts.ca-central-1.amazonaws.com");
    }

    [Test]
    public async Task GetCredentialsAsync_NoRegionAnywhere_ThrowsWithoutARequest()
    {
        // Arrange
        var world = new World(Env(Irsa), region: null);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.StartsWith("The STS endpoint depends on the region, and it is not known: No AWS region configured.", StringComparison.Ordinal), world.Handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_InvalidRegionInTheEnvironment_ThrowsCredentialsUnavailableException()
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_REGION", "Not A Region")]), region: null);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("is not a valid AWS region name", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SessionNameVariable_SendsIt()
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_ROLE_SESSION_NAME", "orders-7f9c")]));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Sent().Single().SessionName).IsEqualTo("orders-7f9c");
    }

    [Test]
    public async Task GetCredentialsAsync_EmptySessionNameVariable_UsesTheDefault()
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_ROLE_SESSION_NAME", string.Empty)]));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Sent().Single().SessionName).StartsWith("dynasharp-", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("a")]
    [Arguments("with space")]
    [Arguments("tür")]
    public async Task GetCredentialsAsync_InvalidSessionNameVariable_ThrowsWithoutARequest(string name)
    {
        // Arrange
        var world = new World(Env([.. Irsa, ("AWS_ROLE_SESSION_NAME", name)]));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count))
            .IsEqualTo(("AWS_ROLE_SESSION_NAME must be 2 to 64 letters, digits or characters of _+=,.@-.", 0));
    }

    [Test]
    public async Task GetCredentialsAsync_ExplicitSettings_IgnoresTheVariables()
    {
        // Arrange
        var world = new World(
            Env(("AWS_WEB_IDENTITY_TOKEN_FILE", "/other"), ("AWS_ROLE_ARN", "arn:aws:iam::123456789012:role/other"), ("AWS_ROLE_SESSION_NAME", "other")),
            settings: new WebIdentityCredentialsProvider.Settings(TokenPath, RoleArn, AwsRegion.FromName("eu-central-2"), "explicit"));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((world.Handler.Requests.Single().RequestUri!.Host, world.Sent().Single()))
            .IsEqualTo(("sts.eu-central-2.amazonaws.com", (RoleArn, "explicit", Token)));
    }

    [Test]
    public async Task GetCredentialsAsync_ExplicitSettingsWithoutSessionName_UsesTheDefault()
    {
        // Arrange
        var world = new World(Env(), settings: new WebIdentityCredentialsProvider.Settings(TokenPath, RoleArn, Region, null));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Sent().Single().SessionName).IsEqualTo("dynasharp-1793450096");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenWithSurroundingWhitespace_SendsTheBareToken()
    {
        // Arrange (a shell that writes the file adds a line break)
        var world = new World();
        world.Files[TokenPath] = $"\n  {Token}\r\n";

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Sent().Single().Token).IsEqualTo(Token);
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwice_ReturnsTheSameInstanceWithOneRequest()
    {
        // Arrange (the signer caches its derived key per instance)
        var world = new World();

        // Act
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        var second = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), world.Handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_FifteenMinutesLeft_RefreshesInTheBackgroundWithTheRotatedToken()
    {
        // Arrange (the kubelet replaced the file in the meantime)
        var world = new World();
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Files[TokenPath] = RotatedToken;
        world.Time.Advance(TimeSpan.FromMinutes(46));

        // Act
        var during = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        var second = await world.WaitForKeyAsync("ASIA2");

        // Assert
        await Assert.That((ReferenceEquals(first, during), world.Sent().Select(static sent => sent.Token).Last(), second.AccessKeyId)).IsEqualTo((true, RotatedToken, "ASIA2"));
    }

    [Test]
    public async Task GetCredentialsAsync_MoreThanFifteenMinutesLeft_DoesNotRefresh()
    {
        // Arrange
        var world = new World();
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Time.Advance(TimeSpan.FromMinutes(44));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_TenMinutesLeft_WaitsForTheRefresh()
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
    public async Task GetCredentialsAsync_ConcurrentCallers_ShareOneFetch()
    {
        // Arrange
        var answer = new TaskCompletionSource<StubResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var world = new World { Answer = (_, cancellationToken) => answer.Task.WaitAsync(cancellationToken) };

        // Act
        var calls = Enumerable.Range(0, 20).Select(_ => world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask()).ToArray();
        while (world.Handler.Requests.Count == 0)
        {
            await Task.Yield();
        }

        answer.SetResult(world.Success());
        var results = await Task.WhenAll(calls);

        // Assert
        await Assert.That((results.All(credentials => ReferenceEquals(credentials, results[0])), world.Handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_CallerCancels_StopsWaitingAndTheFetchGoesOn()
    {
        // Arrange (this is how a client's attempt timeout ends a call to a slow STS: the next attempt finds what the fetch brought)
        var answer = new TaskCompletionSource<StubResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var world = new World { Answer = (_, cancellationToken) => answer.Task.WaitAsync(cancellationToken) };
        using var cancellation = new CancellationTokenSource();
        var waiting = world.Provider.GetCredentialsAsync(cancellation.Token).AsTask();

        // Act
        await cancellation.CancelAsync();
        var cancelled = await TestClient.CaptureAsync<OperationCanceledException>(async () => await waiting);
        answer.SetResult(world.Success());
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((cancelled is not null, credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo((true, AccessKeyId, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_AlreadyCancelled_ThrowsWithoutARequest()
    {
        // Arrange
        var world = new World();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(cancellation.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task TryInvalidate_CurrentCredentials_MakesTheNextCallFetchAndReadTheFile()
    {
        // Arrange
        var world = new World();
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Files[TokenPath] = RotatedToken;

        // Act
        var dropped = world.Provider.TryInvalidate(rejected);
        var next = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((dropped, next.AccessKeyId, world.Sent().Last().Token)).IsEqualTo((true, "ASIA2", RotatedToken));
    }

    [Test]
    public async Task TryInvalidate_ReplacedCredentials_KeepsTheCurrentOnes()
    {
        // Arrange
        var world = new World();
        var old = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Provider.TryInvalidate(old);
        var current = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        world.Provider.TryInvalidate(old);
        var again = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(current, again), world.Handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task TryInvalidate_Null_Throws()
    {
        // Arrange
        var world = new World();

        // Act
        var act = () => world.Provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    [Test]
    [Arguments("InvalidIdentityToken", HttpStatusCode.BadRequest, "STS rejected the web identity token with InvalidIdentityToken: the token is malformed or its signature is wrong, or the account has no OIDC provider for its issuer.")]
    [Arguments("IDPRejectedClaim", HttpStatusCode.Forbidden, "STS rejected the web identity token with IDPRejectedClaim: the identity provider does not accept a claim of the token, usually its audience or subject.")]
    [Arguments("AccessDenied", HttpStatusCode.Forbidden, "STS rejected the web identity token with AccessDenied: the trust policy of the role does not allow this token to assume it.")]
    [Arguments("RegionDisabledException", HttpStatusCode.Forbidden, "STS rejected the web identity token with RegionDisabledException.")]
    [Arguments("ValidationError", HttpStatusCode.BadRequest, "STS rejected the web identity token with ValidationError.")]
    [Arguments("", HttpStatusCode.Forbidden, "STS rejected the web identity token with HTTP 403.")]
    public async Task GetCredentialsAsync_PermanentError_ThrowsOnceAndSaysWhich(string code, HttpStatusCode status, string message)
    {
        // Arrange
        var world = new World { Answer = (_, _) => Task.FromResult(Error(code, status)) };

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo((message, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredToken_ReadsTheFileAgainAndSucceeds()
    {
        // Arrange (the kubelet rotated the file just after the request went out)
        var world = new World();
        world.Answer = (request, _) =>
        {
            world.Files[TokenPath] = RotatedToken;
            return Task.FromResult(request.BodyText.Contains("rotated-payload", StringComparison.Ordinal) ? world.Success() : Error("ExpiredTokenException", HttpStatusCode.BadRequest));
        };

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, string.Join(',', world.Sent().Select(static sent => sent.Token)))).IsEqualTo((AccessKeyId, $"{Token},{RotatedToken}"));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredToken_SendsAgainWithoutAPause()
    {
        // Arrange
        var world = new World();
        world.Answer = (_, _) => Task.FromResult(world.Handler.Requests.Count == 1 ? Error("ExpiredTokenException", HttpStatusCode.BadRequest) : world.Success());

        // Act (the clock stands still, so a pause would never end)
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenStillExpiredAfterReadingTheFile_ThrowsAfterTwoRequests()
    {
        // Arrange
        var world = new World { Answer = (_, _) => Task.FromResult(Error("ExpiredTokenException", HttpStatusCode.BadRequest)) };

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo(
            ("STS rejected the web identity token with ExpiredTokenException, also after the file was read again: whatever renews the file is behind or has stopped.", 2));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredTokenAfterTwoFailures_StillGetsItsOneMoreTry()
    {
        // Arrange
        var world = new World();
        world.Answer = (_, _) => Task.FromResult(world.Handler.Requests.Count switch
        {
            1 or 2 => Error("Throttling", HttpStatusCode.BadRequest),
            3 => Error("ExpiredTokenException", HttpStatusCode.BadRequest),
            _ => world.Success(),
        });

        // Act
        var calling = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(2);
        var credentials = await calling;

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo((AccessKeyId, 4));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredToken_LogsItWithTheSourceOnly()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(logger: logs.CreateLogger("test"));
        world.Answer = (_, _) => Task.FromResult(world.Handler.Requests.Count == 1 ? Error("ExpiredTokenException", HttpStatusCode.BadRequest) : world.Success());

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Select(static entry => (entry.EventId.Id, entry.Level, entry.Message)).Single())
            .IsEqualTo((24, LogLevel.Warning, "STS rejected the token of the web identity token file as expired. The file is read again and the request is sent once more."));
    }

    [Test]
    [Arguments("IDPCommunicationError", HttpStatusCode.BadRequest)]
    [Arguments("Throttling", HttpStatusCode.BadRequest)]
    [Arguments("InternalFailure", HttpStatusCode.InternalServerError)]
    [Arguments("ServiceUnavailable", HttpStatusCode.ServiceUnavailable)]
    [Arguments("TooManyRequests", HttpStatusCode.TooManyRequests)]
    public async Task GetCredentialsAsync_TransientError_RetriesAndSucceeds(string code, HttpStatusCode status)
    {
        // Arrange
        var world = new World();
        world.Answer = (_, _) => Task.FromResult(world.Handler.Requests.Count == 1 ? Error(code, status) : world.Success());

        // Act
        var calling = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(1);
        var credentials = await calling;

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo((AccessKeyId, 2));
    }

    [Test]
    [Arguments("IDPCommunicationError", HttpStatusCode.BadRequest)]
    [Arguments("Throttling", HttpStatusCode.BadRequest)]
    public async Task GetCredentialsAsync_TransientErrorThreeTimes_ThrowsAndNamesTheCode(string code, HttpStatusCode status)
    {
        // Arrange
        var world = new World { Answer = (_, _) => Task.FromResult(Error(code, status)) };

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        await world.AdvanceThroughPausesAsync(2);
        var exception = await failing;

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count))
            .IsEqualTo(($"STS at sts.eu-west-1.amazonaws.com did not return credentials in 3 attempts. The last attempt: {code}.", 3));
    }

    [Test]
    public async Task GetCredentialsAsync_ServerErrorPageThreeTimes_NamesTheStatus()
    {
        // Arrange (a load balancer in front of STS, or a proxy)
        var world = new World { Answer = (_, _) => Task.FromResult(new StubResponse { StatusCode = HttpStatusCode.BadGateway, Body = "<html>Bad Gateway<br></html>" }) };

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        await world.AdvanceThroughPausesAsync(2);
        var exception = await failing;

        // Assert
        await Assert.That(exception.Message).IsEqualTo("STS at sts.eu-west-1.amazonaws.com did not return credentials in 3 attempts. The last attempt: HTTP 502.");
    }

    [Test]
    public async Task GetCredentialsAsync_NetworkFailureThreeTimes_NamesTheFailure()
    {
        // Arrange
        var world = new World { Answer = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused") };

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        await world.AdvanceThroughPausesAsync(2);
        var exception = await failing;

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count))
            .IsEqualTo(("STS at sts.eu-west-1.amazonaws.com did not return credentials in 3 attempts. The last attempt: failed (ConnectionError).", 3));
    }

    [Test]
    public async Task GetCredentialsAsync_SuccessThatIsNotXmlThreeTimes_SaysSo()
    {
        // Arrange (a proxy that answers 200 with a page of its own)
        var world = new World { Answer = (_, _) => Task.FromResult(new StubResponse { Body = "<html>Please log in<br></html>" }) };

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        await world.AdvanceThroughPausesAsync(2);
        var exception = await failing;

        // Assert
        await Assert.That(exception.Message).IsEqualTo("STS at sts.eu-west-1.amazonaws.com did not return credentials in 3 attempts. The last attempt: the response is not XML.");
    }

    [Test]
    public async Task GetCredentialsAsync_AttemptTakesLongerThanFiveSeconds_GivesUpOnItAndRetries()
    {
        // Arrange
        var world = new World();
        world.Answer = async (_, cancellationToken) =>
        {
            if (world.Handler.Requests.Count == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return world.Success();
        };

        // Act
        var calling = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await WaitForTimersAsync(world.Time, 1);
        world.Time.Advance(TimeSpan.FromSeconds(5));
        await WaitForTimersAsync(world.Time, 2);
        world.Time.Advance(TimeSpan.FromSeconds(1));
        var credentials = await calling;

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo((AccessKeyId, 2));
    }

    [Test]
    public async Task GetCredentialsAsync_EveryAttemptTimesOut_EndsWithinTheDefaultAttemptTimeoutOfAClient()
    {
        // Arrange
        var world = new World
        {
            Answer = static async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new StubResponse();
            },
        };
        var start = world.Time.GetUtcNow();

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await WaitForTimersAsync(world.Time, (2 * attempt) - 1);
            world.Time.Advance(TimeSpan.FromSeconds(5));
            if (attempt < 3)
            {
                await WaitForTimersAsync(world.Time, 2 * attempt);
                world.Time.Advance(TimeSpan.FromSeconds(1));
            }
        }

        var exception = await failing;

        // Assert
        await Assert.That((exception.Message.EndsWith("The last attempt: timed out.", StringComparison.Ordinal), world.Time.GetUtcNow() - start < TimeSpan.FromSeconds(30))).IsEqualTo((true, true));
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden, "HTTP 403")]
    [Arguments(HttpStatusCode.ProxyAuthenticationRequired, "HTTP 407")]
    [Arguments(HttpStatusCode.TemporaryRedirect, "HTTP 307")]
    public async Task GetCredentialsAsync_ErrorStatusWithoutADocument_ThrowsOnce(HttpStatusCode status, string description)
    {
        // Arrange
        var world = new World { Answer = (_, _) => Task.FromResult(new StubResponse { StatusCode = status, Body = "<html>no<br></html>" }) };

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo(($"STS rejected the web identity token with {description}.", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ErrorStatusWithAnotherDocument_ThrowsWithTheStatus()
    {
        // Arrange
        var world = new World { Answer = (_, _) => Task.FromResult(new StubResponse { StatusCode = HttpStatusCode.Forbidden, Body = "<Forbidden/>" }) };

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("STS rejected the web identity token with HTTP 403.");
    }

    [Test]
    public async Task GetCredentialsAsync_RedirectToAnotherHost_DoesNotFollowIt()
    {
        // Arrange (following it would hand the bearer token to that host)
        var world = new World
        {
            Answer = (_, _) => Task.FromResult(new StubResponse
            {
                StatusCode = HttpStatusCode.TemporaryRedirect,
                Headers = new Dictionary<string, string> { ["Location"] = "https://attacker.example/" },
            }),
        };

        // Act
        await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(world.Handler.Requests.All(static request => request.RequestUri!.Host == "sts.eu-west-1.amazonaws.com")).IsTrue();
    }

    [Test]
    public async Task GetCredentialsAsync_SuccessWithAnErrorDocument_ThrowsWithTheCode()
    {
        // Arrange
        var world = new World { Answer = (_, _) => Task.FromResult(Error("InvalidIdentityToken", HttpStatusCode.OK)) };

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("InvalidIdentityToken", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ResponseWithoutASessionToken_ThrowsAndNamesTheField()
    {
        // Arrange
        var world = new World
        {
            Answer = (_, _) => Task.FromResult(new StubResponse
            {
                Body = "<R><AssumeRoleWithWebIdentityResult><Credentials><AccessKeyId>a</AccessKeyId><SecretAccessKey>b</SecretAccessKey><Expiration>2030-01-01T00:00:00Z</Expiration></Credentials></AssumeRoleWithWebIdentityResult></R>",
            }),
        };

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("STS returned an unusable response: the response does not contain SessionToken.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredCredentialsOnTheGivenClock_ThrowsBecauseExpiryIsJudgedThere()
    {
        // Arrange (a client passes the clock it corrects from the responses of the service)
        var world = new World();
        var stale = world.Success(expiresIn: TimeSpan.FromMinutes(-1));
        world.Answer = (_, _) => Task.FromResult(stale);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("already expired", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ServiceClockAheadOfTheLocalOne_RefreshesOnTheCorrectedClock()
    {
        // Arrange (the local clock is 51 minutes behind, which it took a rejected request to learn. On the local clock the credentials have an hour left, on that of the service 9 minutes)
        var world = new World();
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        var now = world.Time.GetUtcNow();
        world.Clock.TryCorrect(now, now.AddMinutes(51), out _);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo(("ASIA2", 2));
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileMissing_ThrowsWithoutARequest()
    {
        // Arrange
        var world = new World();
        world.Files.TryRemove(TokenPath, out _);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo(("The web identity token file does not exist.", 0));
    }

    [Test]
    [Arguments("")]
    [Arguments(" \r\n")]
    public async Task GetCredentialsAsync_TokenFileEmpty_ThrowsWithoutARequest(string content)
    {
        // Arrange
        var world = new World();
        world.Files[TokenPath] = content;

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo(("The web identity token file is empty.", 0));
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileUnreadable_ThrowsAndKeepsTheCause()
    {
        // Arrange
        var failure = new UnauthorizedAccessException("denied");
        var world = new World(files: _ => throw failure);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, exception.InnerException)).IsEqualTo(("The web identity token file cannot be read: access denied.", (Exception?)failure));
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileVanishesInTheAdvisoryPeriod_KeepsTheCredentialsAndWarns()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(logger: logs.CreateLogger("test"));
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Files.TryRemove(TokenPath, out _);
        world.Time.Advance(TimeSpan.FromMinutes(46));

        // Act
        var during = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        while (logs.Entries.Count == 0)
        {
            await Task.Yield();
        }

        // Assert
        await Assert.That((ReferenceEquals(first, during), logs.Entries.Single().EventId.Id)).IsEqualTo((true, 21));
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileVanishesInTheMandatoryPeriod_Throws()
    {
        // Arrange
        var world = new World();
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Files.TryRemove(TokenPath, out _);
        world.Time.Advance(TimeSpan.FromMinutes(51));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("The web identity token file does not exist.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_FileVanishesBetweenAttempts_ThrowsAtOnce()
    {
        // Arrange
        var world = new World();
        world.Answer = (_, _) =>
        {
            world.Files.TryRemove(TokenPath, out _);
            return Task.FromResult(Error("Throttling", HttpStatusCode.BadRequest));
        };

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        await world.AdvanceThroughPausesAsync(1);
        var exception = await failing;

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count)).IsEqualTo(("The web identity token file does not exist.", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ErrorThatQuotesTheToken_LeavesItOutOfTheException()
    {
        // Arrange (the validation errors of AWS services quote the value they refuse)
        var world = new World { Answer = (_, _) => Task.FromResult(Error("ValidationError", HttpStatusCode.BadRequest, $"Value '{Token}' at 'webIdentityToken' failed to satisfy constraint")) };

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.ToString()).DoesNotContain("secret-payload", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task GetCredentialsAsync_FailureOfARefreshThatQuotesTheToken_LeavesItOutOfTheLog(HttpStatusCode status)
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(logger: logs.CreateLogger("test"));
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Answer = (_, _) => Task.FromResult(Error("ValidationError", status, $"Value '{Token}' is invalid"));
        world.Time.Advance(TimeSpan.FromMinutes(51));
        var firstTimer = world.Time.TimersCreated;

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));
        if (status == HttpStatusCode.ServiceUnavailable)
        {
            await world.AdvanceThroughPausesAsync(2, firstTimer);
        }

        await failing;
        while (logs.Entries.Count == 0)
        {
            await Task.Yield();
        }

        // Assert
        var logged = string.Join('\n', logs.Entries.Select(static entry => entry.Message));
        await Assert.That(logged).DoesNotContain("secret-payload", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_FailedRequest_KeepsTheRoleAndTheFileOutOfTheException()
    {
        // Arrange (what a log of the failure shows is the source, and the code of STS)
        var world = new World { Answer = (_, _) => Task.FromResult(Error("AccessDenied", HttpStatusCode.Forbidden)) };

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.Contains(RoleArn, StringComparison.Ordinal), exception.Message.Contains(TokenPath, StringComparison.Ordinal))).IsEqualTo((false, false));
    }

    private static StubResponse Error(string code, HttpStatusCode status, string message = "The message.") => new()
    {
        StatusCode = status,
        Body = $"""<ErrorResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/"><Error><Type>Sender</Type><Code>{code}</Code><Message>{message}</Message></Error><RequestId>r</RequestId></ErrorResponse>""",
    };

    /// <summary>A fake pod: its environment, its files and STS. Each answer of STS has a new access key.</summary>
    private sealed class World
    {
        private int _issued;

        public World(
            Func<string, string?>? environment = null,
            ReadTextFile? files = null,
            AwsRegion? region = null,
            WebIdentityCredentialsProvider.Settings? settings = null,
            ILogger? logger = null)
            : this(environment, files, region, settings, logger, regionIsUnknown: false)
        {
        }

        public World(Func<string, string?> environment, AwsRegion? region)
            : this(environment, null, region, null, null, regionIsUnknown: region is null)
        {
        }

        private World(
            Func<string, string?>? environment,
            ReadTextFile? files,
            AwsRegion? region,
            WebIdentityCredentialsProvider.Settings? settings,
            ILogger? logger,
            bool regionIsUnknown)
        {
            Files[TokenPath] = Token;
            Answer = (_, _) => Task.FromResult(Success());
            Handler = new StubHandler((request, cancellationToken) => Answer(request, cancellationToken));
            Clock = new ClockSkewCorrection(Time);
            Provider = new WebIdentityCredentialsProvider(
                environment ?? Env(Irsa),
                files ?? ReadFile,
                Handler,
                Clock,
                logger ?? NullLogger.Instance,
                regionIsUnknown ? null : region ?? WebIdentityCredentialsProviderTests.Region,
                settings);
        }

        public ManualTimeProvider Time { get; } = new();

        /// <summary>Gets the clock a client gives the providers it makes: <see cref="Time"/>, corrected from the responses of the service.</summary>
        public ClockSkewCorrection Clock { get; }

        public System.Collections.Concurrent.ConcurrentDictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public StubHandler Handler { get; }

        public WebIdentityCredentialsProvider Provider { get; }

        public Func<RecordedRequest, CancellationToken, Task<StubResponse>> Answer { get; set; }

        /// <summary>Creates the answer of STS, with a key that says how many it has issued: <c>ASIAIOSFODNN7EXAMPLE</c>, then <c>ASIA2</c>.</summary>
        /// <param name="expiresIn">How long the credentials last; an hour by default, as nothing asks for another duration.</param>
        /// <returns>The response.</returns>
        public StubResponse Success(TimeSpan? expiresIn = null)
        {
            var issued = Interlocked.Increment(ref _issued);
            var key = issued == 1 ? AccessKeyId : $"ASIA{issued}";
            return new StubResponse
            {
                Body = $"""
                    <AssumeRoleWithWebIdentityResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
                      <AssumeRoleWithWebIdentityResult>
                        <Credentials>
                          <AccessKeyId>{key}</AccessKeyId>
                          <SecretAccessKey>{SecretAccessKey}</SecretAccessKey>
                          <SessionToken>{SessionToken}</SessionToken>
                          <Expiration>{Timestamp(Time.GetUtcNow() + (expiresIn ?? TimeSpan.FromHours(1)))}</Expiration>
                        </Credentials>
                      </AssumeRoleWithWebIdentityResult>
                    </AssumeRoleWithWebIdentityResponse>
                    """,
            };
        }

        /// <summary>Reads back what each request sent.</summary>
        /// <returns>The form fields of the requests that matter, decoded.</returns>
        public IEnumerable<(string RoleArn, string SessionName, string Token)> Sent() => Handler.Requests.Select(static request =>
        {
            var fields = request.BodyText.Split('&').Select(static field => field.Split('=', 2)).ToDictionary(static field => field[0], static field => Uri.UnescapeDataString(field[1]));
            return (fields["RoleArn"], fields["RoleSessionName"], fields["WebIdentityToken"]);
        });

        /// <summary>Lets the pauses between the attempts of a fetch pass. The timers are created in this order: attempt 1, pause 1, attempt 2, pause 2, ...</summary>
        /// <param name="pauses">How many pauses.</param>
        /// <param name="firstTimer">How many timers existed before the fetch began.</param>
        /// <returns>A task that completes when they have passed.</returns>
        public async Task AdvanceThroughPausesAsync(int pauses, int firstTimer = 0)
        {
            for (var pause = 1; pause <= pauses; pause++)
            {
                await WaitForTimersAsync(Time, firstTimer + (2 * pause));
                Time.Advance(TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>Asks until a refresh that runs in the background has delivered.</summary>
        /// <param name="accessKeyId">The key of the credentials to wait for.</param>
        /// <returns>The credentials.</returns>
        public async Task<AwsCredentials> WaitForKeyAsync(string accessKeyId)
        {
            while (true)
            {
                var credentials = await Provider.GetCredentialsAsync(CancellationToken.None);
                if (credentials.AccessKeyId == accessKeyId)
                {
                    return credentials;
                }

                await Task.Yield();
            }
        }

        private string? ReadFile(string path) => Files.GetValueOrDefault(path);
    }
}
