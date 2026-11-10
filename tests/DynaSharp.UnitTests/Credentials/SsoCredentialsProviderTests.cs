using System.Collections.Concurrent;
using System.Net;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Generators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class SsoCredentialsProviderTests
{
    private const string StartUrl = "https://my-sso-portal.awsapps.com/start";
    private const string Session = "my-sso";
    private const string Home = "/home/dev";

    // SHA-1 in hexadecimal of "my-sso" and of the start URL, as Python's hashlib.sha1(...).hexdigest() gives them.
    private const string SessionKey = "0ad374308c5a4e22f723adf10145eafad7c4031c";
    private const string StartUrlKey = "c7aaaf71fcc8777ae2475525ed049d39fe16c484";

    private static readonly AwsRegion SsoRegion = AwsRegion.FromName("eu-central-1");
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    [Test]
    [Arguments(null, "s", "1", "r", "startUrl")]
    [Arguments("", "s", "1", "r", "startUrl")]
    [Arguments("u", "s", null, "r", "accountId")]
    [Arguments("u", "s", "", "r", "accountId")]
    [Arguments("u", "s", "1", null, "roleName")]
    [Arguments("u", "s", "1", "", "roleName")]
    [Arguments("u", "", "1", "r", "sessionName")]
    public async Task Constructor_MissingSetting_ThrowsArgumentException(string? start, string? sessionName, string? accountId, string? roleName, string parameter)
    {
        // Arrange

        // Act
        var act = () => new SsoCredentialsProvider(start!, SsoRegion, accountId!, roleName!, sessionName);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithParameterName(parameter);
    }

    [Test]
    public async Task Constructor_EmptySessionName_ThrowsArgumentExceptionSayingWhatNullMeans()
    {
        // Arrange

        // Act
        var act = () => new SsoCredentialsProvider(StartUrl, SsoRegion, "123456789012", "ReadOnly", string.Empty);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessage("The session name must not be empty; null means the older form without a session. (Parameter 'sessionName')", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_NullRegion_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        var act = () => new SsoCredentialsProvider(StartUrl, null!, "123456789012", "ReadOnly");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("ssoRegion");
    }

    [Test]
    [Arguments(null)]
    [Arguments(Session)]
    public async Task Constructor_ValidSettings_CreatesAProviderThatReadsNothingYet(string? sessionName)
    {
        // Arrange

        // Act
        var provider = new SsoCredentialsProvider(StartUrl, SsoRegion, "123456789012", "ReadOnly", sessionName);

        // Assert
        await Assert.That(provider).IsAssignableTo<IRefreshableCredentialsProvider>();
    }

    [Test]
    [Arguments(Session, SessionKey)]
    [Arguments(null, StartUrlKey)]
    public async Task GetCredentialsAsync_CachedToken_IsReadFromTheFileOfBotocore(string? sessionName, string key)
    {
        // Arrange
        var world = new World(sessionName);
        world.Files[world.TokenPath(key)] = Token(Now.AddHours(8));

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId}|{credentials.SecretAccessKey}|{credentials.SessionToken}|{credentials.Expiration:O}")
            .IsEqualTo($"{AccessKeyId}|{SecretAccessKey}|{SessionToken}|{Now.AddHours(1):O}");
    }

    [Test]
    public async Task GetCredentialsAsync_Request_IsTheGetRoleCredentialsOfBotocore()
    {
        // Arrange (what botocore's sso client sends for get_role_credentials(roleName="Admin Role", accountId="123456789012", accessToken=...))
        var world = new World(Session, roleName: "Admin Role");
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8), accessToken: "the-access-token");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = world.Handler.Requests[0];
        await Assert.That($"{request.Method} {request.RequestUri!.AbsoluteUri} {request.Headers["x-amz-sso_bearer_token"]} {request.Headers.ContainsKey("Authorization")}")
            .IsEqualTo("GET https://portal.sso.eu-central-1.amazonaws.com/federation/credentials?role_name=Admin%20Role&account_id=123456789012 the-access-token False");
    }

    [Test]
    [Arguments("AWS_ENDPOINT_URL_SSO")]
    [Arguments("AWS_ENDPOINT_URL")]
    public async Task GetCredentialsAsync_EndpointInTheEnvironment_SendsThere(string variable)
    {
        // Arrange
        var world = new World(Session, variables: [(variable, "http://localhost:4566")]);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.GetLeftPart(UriPartial.Path)).IsEqualTo("http://localhost:4566/federation/credentials");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointVariableIsInvalid_FailsNamingTheVariable()
    {
        // Arrange
        var world = new World(Session, variables: [("AWS_ENDPOINT_URL_SSO", "not a url")]);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("AWS_ENDPOINT_URL_SSO environment variable is not a valid endpoint", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(Session, "The cached SSO token of the sso-session 'my-sso' does not exist. Run `aws sso login --sso-session my-sso` to sign in again.")]
    [Arguments(null, "The cached SSO token of the start URL https://my-sso-portal.awsapps.com/start does not exist. Run `aws sso login` with a profile whose sso_start_url is https://my-sso-portal.awsapps.com/start to sign in again.")]
    public async Task GetCredentialsAsync_NoCachedToken_SaysToSignIn(string? sessionName, string message)
    {
        // Arrange
        var world = new World(sessionName);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    [Arguments(Session, "The cached SSO token of the sso-session 'my-sso' does not exist. Run `aws sso login --sso-session my-sso` to sign in again.")]
    [Arguments(null, "The cached SSO token of the start URL https://my-sso-portal.awsapps.com/start does not exist. Run `aws sso login` with a profile whose sso_start_url is https://my-sso-portal.awsapps.com/start to sign in again.")]
    public async Task GetCredentialsAsync_PublicConstructorAndNoCachedToken_SaysToSignIn(string? sessionName, string message)
    {
        // Arrange (a home folder without a token cache)
        using var home = new TempDirectory();
        using var environment = new ScopedEnvironmentVariables(("USERPROFILE", home.Path), ("HOME", home.Path));
        var provider = new SsoCredentialsProvider(StartUrl, SsoRegion, "123456789012", "ReadOnly", sessionName);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("not json")]
    [Arguments("""{"expiresAt": "2026-11-03T20:00:00Z"}""")]
    [Arguments("""{"accessToken": "", "expiresAt": "2026-11-03T20:00:00Z"}""")]
    [Arguments("""{"accessToken": "at"}""")]
    [Arguments("""{"accessToken": "at", "expiresAt": "later"}""")]
    public async Task GetCredentialsAsync_TokenNotValid_SaysToSignIn(string content)
    {
        // Arrange
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = content;

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The cached SSO token of the sso-session 'my-sso' is not valid. Run `aws sso login --sso-session my-sso` to sign in again.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenCannotBeRead_SaysWhy()
    {
        // Arrange
        var world = new World(Session, readFile: _ => throw new UnauthorizedAccessException("Access to the path is denied."));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The cached SSO token of the sso-session 'my-sso' cannot be read: access denied.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_LegacyTokenExpired_SaysToSignInWithoutAsking()
    {
        // Arrange (botocore saves the call to the portal for an expired token of the older form)
        var world = new World(null);
        world.Files[world.TokenPath(StartUrlKey)] = Token(Now);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.StartsWith("The cached SSO token of the start URL https://my-sso-portal.awsapps.com/start has expired. Run `aws sso login`", StringComparison.Ordinal), world.Handler.Requests.Count))
            .IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_LegacyTokenNearItsEnd_IsUsedWithoutARefresh()
    {
        // Arrange (a token of the older form has no registration to refresh with)
        var world = new World(null);
        world.Files[world.TokenPath(StartUrlKey)] = Token(Now.AddMinutes(5), refreshToken: "rt");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Select(request => request.RequestUri!.AbsolutePath)).IsEquivalentTo(["/federation/credentials"]);
    }

    [Test]
    public async Task GetCredentialsAsync_SessionTokenWithFifteenMinutesLeft_IsUsedWithoutARefresh()
    {
        // Arrange (botocore refreshes a token with less than fifteen minutes left)
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(15), refreshToken: "rt");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests.Select(request => request.RequestUri!.AbsolutePath)).IsEquivalentTo(["/federation/credentials"]);
    }

    [Test]
    public async Task GetCredentialsAsync_SessionTokenNearItsEnd_RefreshesItAndWritesItBack()
    {
        // Arrange
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Written[world.TokenPath(SessionKey)]).IsEqualTo(
            $$"""{"startUrl":"{{StartUrl}}","region":"eu-central-1","accessToken":"new-access","expiresAt":"2026-10-31T20:34:56Z","clientId":"client-id","clientSecret":"client-secret","registrationExpiresAt":"2027-01-31T08:00:00Z","refreshToken":"new-refresh"}""");
    }

    [Test]
    public async Task GetCredentialsAsync_SessionTokenRefreshed_SendsTheCreateTokenOfBotocoreAndUsesTheNewToken()
    {
        // Arrange
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var (create, get) = (world.Handler.Requests[0], world.Handler.Requests[1]);
        await Assert.That($"{create.Method} {create.RequestUri} {create.Headers["Content-Type"]} {create.BodyText} {get.Headers["x-amz-sso_bearer_token"]}")
            .IsEqualTo("""POST https://oidc.eu-central-1.amazonaws.com/token application/json; charset=utf-8 {"grantType":"refresh_token","clientId":"client-id","clientSecret":"client-secret","refreshToken":"old-refresh"} new-access""");
    }

    [Test]
    public async Task GetCredentialsAsync_SessionTokenRefreshed_LogsItAtDebugLevel()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(string.Join('\n', logs.Entries.Select(entry => $"{entry.Level} {entry.EventId.Id} {entry.Message}")))
            .IsEqualTo("Debug 29 The cached SSO token of the sso-session 'my-sso' was refreshed.");
    }

    [Test]
    public async Task GetCredentialsAsync_HttpEndpointsOnRemoteHosts_WarnsOnceForBoth()
    {
        // Arrange: emulators in docker-compose, reached by their service names; the token is renewed first, so both endpoints are used
        using var logs = new CapturingLoggerFactory(LogLevel.Warning);
        var world = new World(Session, logs: logs, variables: [("AWS_ENDPOINT_URL_SSO", "http://sso-emulator:4566"), ("AWS_ENDPOINT_URL_SSO_OIDC", "http://oidc-emulator:4566")]);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((world.Handler.Requests.Count, logs.Entries.Select(static entry => (entry.EventId.Id, entry.Level, entry.Message)).Single())).IsEqualTo((
            2,
            (31, LogLevel.Warning, "The IAM Identity Center endpoint oidc-emulator is reached over plain http. The access token, the client secret and the credentials travel unencrypted.")));
    }

    [Test]
    [Arguments("http://localhost:4566")]
    [Arguments("http://[::1]:4566")]
    [Arguments("https://sso.example.com")]
    public async Task GetCredentialsAsync_HttpsOrLoopbackEndpoints_LogsNoWarning(string endpoint)
    {
        // Arrange
        using var logs = new CapturingLoggerFactory(LogLevel.Warning);
        var world = new World(Session, logs: logs, variables: [("AWS_ENDPOINT_URL_SSO", endpoint), ("AWS_ENDPOINT_URL_SSO_OIDC", endpoint)]);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((world.Handler.Requests.Count, logs.Entries.Count)).IsEqualTo((2, 0));
    }

    [Test]
    [Arguments("""{"accessToken":"new-access","tokenType":"Bearer","expiresIn":3600}""")]
    [Arguments("""{"accessToken":"new-access","tokenType":"Bearer","expiresIn":3600,"refreshToken":5}""")]
    public async Task GetCredentialsAsync_CreateTokenWithoutRefreshToken_SavesNoneAsBotocoreDoes(string body)
    {
        // Arrange
        var world = new World(Session, createToken: _ => new StubResponse { Body = body });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Written[world.TokenPath(SessionKey)]).DoesNotContain("refreshToken", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenWrittenBackCannotBeSaved_WarnsAndUsesTheNewTokenAllTheSame()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs, writeFile: (_, _) => throw new IOException("disk full"));
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "old-refresh");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((logs.Entries.Single().EventId.Id, logs.Entries.Single().Message, world.Handler.Requests[1].Headers["x-amz-sso_bearer_token"]))
            .IsEqualTo((28, "The refreshed SSO token of the sso-session 'my-sso' could not be written to the token cache: I/O error. It is used all the same, and refreshed again at the next fetch.", "new-access"));
    }

    [Test]
    [Arguments("""{"refreshToken":"rt","clientSecret":"cs","registrationExpiresAt":"2027-01-31T08:00:00Z"}""")]
    [Arguments("""{"refreshToken":"rt","clientId":"ci","registrationExpiresAt":"2027-01-31T08:00:00Z"}""")]
    [Arguments("""{"clientId":"ci","clientSecret":"cs","registrationExpiresAt":"2027-01-31T08:00:00Z"}""")]
    [Arguments("""{"refreshToken":"rt","clientId":"ci","clientSecret":"cs"}""")]
    [Arguments("""{"refreshToken":"rt","clientId":"ci","clientSecret":"cs","registrationExpiresAt":"soon"}""")]
    public async Task GetCredentialsAsync_NoRegistrationToRefreshWith_UsesTheCachedTokenWithoutAsking(string registration)
    {
        // Arrange (botocore's _refresh_access_token gives up, logging at info level only)
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs);
        world.Files[world.TokenPath(SessionKey)] = $$"""{"accessToken":"at","expiresAt":"{{Timestamp(Now.AddMinutes(14))}}",{{registration[1..]}}""";

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((world.Handler.Requests.Count, logs.Entries.Count)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_RegistrationExpired_WarnsAndUsesTheCachedToken()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "rt", registrationExpiresAt: Now);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((logs.Entries.Single().Message, world.Handler.Requests.Count))
            .IsEqualTo(("Refreshing the cached SSO token of the sso-session 'my-sso' failed: the registration of the client has expired. The cached token is used while it lasts.", 1));
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"secret description"}""", "HTTP 400 invalid_grant")]
    [Arguments(HttpStatusCode.BadRequest, """{"error":7}""", "HTTP 400")]
    [Arguments(HttpStatusCode.BadRequest, "[]", "HTTP 400")]
    [Arguments(HttpStatusCode.Unauthorized, "not json", "HTTP 401")]
    [Arguments(HttpStatusCode.OK, """{"tokenType":"Bearer"}""", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, """{"accessToken":1,"expiresIn":3600}""", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, """{"accessToken":"a","expiresIn":"3600"}""", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, "not json", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, "[1]", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, """[{"accessToken":"a","expiresIn":3600}]""", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, """{"meta":{"accessToken":"a","expiresIn":3600}}""", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.OK, """{"accessToken":"a","expiresIn":3600,""", "the response does not contain accessToken and expiresIn")]
    [Arguments(HttpStatusCode.BadRequest, """{"accessToken":"a","expiresIn":3600}""", "HTTP 400")]
    public async Task GetCredentialsAsync_CreateTokenFails_WarnsWithTheReasonAndUsesTheCachedToken(HttpStatusCode status, string body, string reason)
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs, createToken: _ => new StubResponse { StatusCode = status, Body = body });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "rt", accessToken: "cached-access");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((logs.Entries.Single().Message, world.Handler.Requests[^1].Headers["x-amz-sso_bearer_token"]))
            .IsEqualTo(($"Refreshing the cached SSO token of the sso-session 'my-sso' failed: {reason}. The cached token is used while it lasts.", "cached-access"));
    }

    [Test]
    public async Task GetCredentialsAsync_CreateTokenUnreachable_RetriesThreeTimesThenWarns()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs, createToken: _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no dns"));
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "rt");

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(2);
        await fetch;

        // Assert
        await Assert.That((logs.Entries.Single().Message, world.Handler.Requests.Count(request => request.RequestUri!.AbsolutePath == "/token")))
            .IsEqualTo(("Refreshing the cached SSO token of the sso-session 'my-sso' failed: failed (NameResolutionError). The cached token is used while it lasts.", 3));
    }

    [Test]
    public async Task GetCredentialsAsync_SessionTokenExpiredAndRefreshFails_SaysToSignIn()
    {
        // Arrange
        var world = new World(Session, createToken: _ => new StubResponse { StatusCode = HttpStatusCode.BadRequest, Body = """{"error":"invalid_grant"}""" });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(-1), refreshToken: "rt");

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The cached SSO token of the sso-session 'my-sso' has expired, and it could not be refreshed. Run `aws sso login --sso-session my-sso` to sign in again.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SessionTokenExpiredAndRefreshWorks_ReturnsCredentials()
    {
        // Arrange (the refresh token outlives the access token, which is what it is for)
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddDays(-1), refreshToken: "rt");

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_PortalOfAwsEndpointUrlRejects_SaysWhichVariableToSetForDynamoDb()
    {
        // Arrange (AWS_ENDPOINT_URL meant for an emulator of DynamoDB sends the portal call there too)
        var world = new World(Session, variables: [("AWS_ENDPOINT_URL", "http://localhost:8000")], portal: _ => new StubResponse { StatusCode = HttpStatusCode.BadRequest, Body = "{}" });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo(
            "The SSO portal did not return credentials for the role ReadOnly in the account 123456789012: InvalidRequestException: the portal refused the request. The endpoint of SSO came from AWS_ENDPOINT_URL, which sends the calls of every AWS service there, SSO included; to send only DynamoDB to an emulator, set AWS_ENDPOINT_URL_DYNAMODB instead.");
    }

    [Test]
    public async Task GetCredentialsAsync_OidcEndpointInTheEnvironment_RefreshesThere()
    {
        // Arrange
        var world = new World(Session, variables: [("AWS_ENDPOINT_URL_SSO_OIDC", "https://oidc.internal.example")]);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(14), refreshToken: "rt");

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://oidc.internal.example/token");
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, "InvalidRequestException: the portal refused the request")]
    [Arguments(HttpStatusCode.Unauthorized, "UnauthorizedException: the token of the sso-session 'my-sso' has expired or was revoked. Run `aws sso login --sso-session my-sso` to sign in again.")]
    [Arguments(HttpStatusCode.Forbidden, "ForbiddenException: the user may not use the role ReadOnly in the account 123456789012")]
    [Arguments(HttpStatusCode.NotFound, "ResourceNotFoundException: the user has no role ReadOnly in the account 123456789012")]
    [Arguments(HttpStatusCode.Conflict, "HTTP 409")]
    public async Task GetCredentialsAsync_PortalRejects_FailsNamingTheRoleAndWhy(HttpStatusCode status, string description)
    {
        // Arrange
        var world = new World(Session, portal: _ => new StubResponse { StatusCode = status, Body = """{"message":"secret text"}""" });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"The SSO portal did not return credentials for the role ReadOnly in the account 123456789012: {description}.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(HttpStatusCode.TooManyRequests, "HTTP 429")]
    [Arguments(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task GetCredentialsAsync_PortalFailsThreeTimes_FailsWithTheLastAttempt(HttpStatusCode status, string lastFailure)
    {
        // Arrange
        var world = new World(Session, portal: _ => new StubResponse { StatusCode = status, Body = string.Empty });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(2);
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(() => fetch);

        // Assert
        await Assert.That((exception.Message, world.Handler.Requests.Count))
            .IsEqualTo(($"The SSO portal at portal.sso.eu-central-1.amazonaws.com did not return credentials in 3 attempts. The last attempt: {lastFailure}.", 3));
    }

    [Test]
    public async Task GetCredentialsAsync_PortalFailsOnceThenAnswers_ReturnsCredentials()
    {
        // Arrange
        var answers = new ConcurrentQueue<StubResponse>([new StubResponse { StatusCode = HttpStatusCode.ServiceUnavailable, Body = string.Empty }, RoleCredentials(Now.AddHours(1))]);
        var world = new World(Session, portal: _ => answers.TryDequeue(out var answer) ? answer : throw new InvalidOperationException());
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var fetch = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await world.AdvanceThroughPausesAsync(1);
        var credentials = await fetch;

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    [Arguments("not json", "the response is not a JSON object")]
    [Arguments("[]", "the response is not a JSON object")]
    [Arguments("{}", "the response does not contain roleCredentials with accessKeyId, secretAccessKey, sessionToken, expiration")]
    [Arguments("""{"roleCredentials":null}""", "the response does not contain roleCredentials with accessKeyId, secretAccessKey, sessionToken, expiration")]
    [Arguments("""{"other":{"accessKeyId":"a","secretAccessKey":"s","sessionToken":"t","expiration":1700000000000}}""", "the response does not contain roleCredentials with accessKeyId, secretAccessKey, sessionToken, expiration")]
    [Arguments("""{"other":{"a":1},"roleCredentials":{"accessKeyId":"a","secretAccessKey":"s","sessionToken":"t","expiration":"1700000000000"}}""", "the response does not contain roleCredentials with expiration")]
    [Arguments("""{"roleCredentials":{"accessKeyId":"a","secretAccessKey":"s","sessionToken":"t","expiration":1.5}}""", "the response does not contain roleCredentials with expiration")]
    [Arguments("""{"roleCredentials":{"accessKeyId":"a","secretAccessKey":"s","sessionToken":"t","expiration":253402300800000}}""", "the response does not contain roleCredentials with expiration")]
    [Arguments("""{"roleCredentials":{"accessKeyId":"a","secretAccessKey":"s","sessionToken":"t","expiration":-62135596800001}}""", "the response does not contain roleCredentials with expiration")]
    [Arguments("""{"roleCredentials":{"accessKeyId":1,"secretAccessKey":{"x":""},"sessionToken":null,"expiration":1700000000000,"extra":[1]}}""", "the response does not contain roleCredentials with accessKeyId, secretAccessKey, sessionToken")]
    public async Task GetCredentialsAsync_PortalAnswersWithoutCredentials_FailsNamingWhatIsMissing(string body, string problem)
    {
        // Arrange
        var world = new World(Session, portal: _ => new StubResponse { Body = body });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage($"The SSO portal returned an unusable response: {problem}.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_PortalAnswersTheLastMillisecondATimeCanHold_ReturnsCredentialsThatExpireThen()
    {
        // Arrange
        var world = new World(Session, portal: _ => new StubResponse
        {
            Body = """{"roleCredentials":{"accessKeyId":"a","secretAccessKey":"s","sessionToken":"t","expiration":253402300799999}}""",
        });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(253402300799999));
    }

    [Test]
    public async Task GetCredentialsAsync_PortalAnswersAStringThatIsNotUtf8_FailsAsNoJsonObject()
    {
        // Arrange
        byte[] body = [.. """{"roleCredentials":{"accessKeyId":" """u8.ToArray()[..^1], 0xC3, 0x28, .. "\"}}"u8.ToArray()];
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));
        var files = new Dictionary<string, string> { [Path.Combine(Home, ".aws", "sso", "cache", SessionKey + ".json")] = Token(Now.AddHours(8)) };
        var dependencies = new CredentialsDependencies(Env(("USERPROFILE", Home)), path => files.GetValueOrDefault(path), (_, _) => { }, CredentialProcess.Runner, handler, new ManualTimeProvider(Now), NullLoggerFactory.Instance);
        var provider = new SsoCredentialsProvider(new SsoCredentialsProvider.Settings(StartUrl, SsoRegion, "123456789012", "ReadOnly", Session, "Sign in."), dependencies);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("the response is not a JSON object", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_CreateTokenAnswersAStringThatIsNotUtf8_WarnsAndUsesTheCachedToken()
    {
        // Arrange
        byte[] created = [.. """{"accessToken":" """u8.ToArray()[..^1], 0xC3, 0x28, .. "\"}"u8.ToArray()];
        using var logs = new CapturingLoggerFactory();
        using var handler = new DelegateHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/token"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(created) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RoleCredentials(Now.AddHours(1)).Body) }));
        var files = new Dictionary<string, string> { [Path.Combine(Home, ".aws", "sso", "cache", SessionKey + ".json")] = Token(Now.AddMinutes(14), refreshToken: "rt") };
        var dependencies = new CredentialsDependencies(Env(("USERPROFILE", Home)), path => files.GetValueOrDefault(path), (_, _) => { }, CredentialProcess.Runner, handler, new ManualTimeProvider(Now), logs);
        var provider = new SsoCredentialsProvider(new SsoCredentialsProvider.Settings(StartUrl, SsoRegion, "123456789012", "ReadOnly", Session, "Sign in."), dependencies);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single().Message).Contains("the response does not contain accessToken and expiresIn", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(480)]
    [Arguments(14)]
    public async Task GetCredentialsAsync_AnswerComesLater_DoesNotResumeOnTheContextOfTheCaller(int minutesLeft)
    {
        // Arrange (with 14 minutes left the token is refreshed first, so the wait for CreateToken is the first that suspends)
        var answersReady = new TaskCompletionSource();
        var world = new World(Session, answersReady: answersReady.Task);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddMinutes(minutesLeft), refreshToken: "rt");
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask());

        // Act
        answersReady.SetResult();
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwiceWhileValid_AsksThePortalOnce()
    {
        // Arrange
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), world.Handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_InTheMandatoryPeriod_ReadsTheTokenAgainAndAsksAgain()
    {
        // Arrange (a new `aws sso login` in between is picked up)
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8), accessToken: "first");
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8), accessToken: "second");
        world.Time.Advance(TimeSpan.FromMinutes(51));

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[1].Headers["x-amz-sso_bearer_token"]).IsEqualTo("second");
    }

    [Test]
    public async Task TryInvalidate_Current_AsksThePortalAgain()
    {
        // Arrange
        var world = new World(Session);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = world.Provider.TryInvalidate(rejected);
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, world.Handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task TryInvalidate_Current_LogsTheRoleWhoseCredentialsItDrops()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(Session, logs: logs);
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8));
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        world.Provider.TryInvalidate(rejected);

        // Assert
        await Assert.That(logs.Entries.Single().Message)
            .IsEqualTo("DynamoDB rejected the credentials from the SSO role ReadOnly in the account 123456789012 as expired. They are dropped and fetched again.");
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new SsoCredentialsProvider(StartUrl, SsoRegion, "123456789012", "ReadOnly");

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    [Test]
    public async Task GetCredentialsAsync_Failure_ShowsNoToken()
    {
        // Arrange
        var world = new World(Session, portal: _ => new StubResponse { StatusCode = HttpStatusCode.Unauthorized, Body = string.Empty });
        world.Files[world.TokenPath(SessionKey)] = Token(Now.AddHours(8), accessToken: "secret-access-token");

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).DoesNotContain("secret-access-token", StringComparison.Ordinal);
    }

    internal static string Token(DateTimeOffset expiresAt, string accessToken = "the-access-token", string? refreshToken = null, DateTimeOffset? registrationExpiresAt = null) =>
        refreshToken is null
            ? $$"""{"startUrl":"{{StartUrl}}","region":"eu-central-1","accessToken":"{{accessToken}}","expiresAt":"{{Timestamp(expiresAt)}}"}"""
            : $$"""{"startUrl":"{{StartUrl}}","region":"eu-central-1","accessToken":"{{accessToken}}","expiresAt":"{{Timestamp(expiresAt)}}","clientId":"client-id","clientSecret":"client-secret","registrationExpiresAt":"{{(registrationExpiresAt is { } at ? Timestamp(at) : "2027-01-31T08:00:00Z")}}","refreshToken":"{{refreshToken}}"}""";

    internal static StubResponse RoleCredentials(DateTimeOffset expiration) => new()
    {
        Body = $$$"""{"roleCredentials":{"accessKeyId":"{{{AccessKeyId}}}","secretAccessKey":"{{{SecretAccessKey}}}","sessionToken":"{{{SessionToken}}}","expiration":{{{expiration.ToUnixTimeMilliseconds()}}}}}""",
    };

    /// <summary>A machine with a home folder, the token cache of the AWS CLI, the portal and the OIDC service.</summary>
    private sealed class World
    {
        public World(
            string? sessionName,
            string roleName = "ReadOnly",
            (string Name, string Value)[]? variables = null,
            ReadTextFile? readFile = null,
            WriteTextFile? writeFile = null,
            CapturingLoggerFactory? logs = null,
            Func<RecordedRequest, StubResponse>? portal = null,
            Func<RecordedRequest, StubResponse>? createToken = null,
            Task? answersReady = null)
        {
            portal ??= _ => RoleCredentials(Time.GetUtcNow().AddHours(1));
            createToken ??= _ => new StubResponse { Body = """{"accessToken":"new-access","tokenType":"Bearer","expiresIn":28800,"refreshToken":"new-refresh"}""" };
            Handler = new StubHandler(async (request, _) =>
            {
                await (answersReady ?? Task.CompletedTask).ConfigureAwait(false);
                return request.RequestUri!.AbsolutePath == "/token" ? createToken(request) : portal(request);
            });
            var dependencies = new CredentialsDependencies(
                Env([("USERPROFILE", Home), .. variables ?? []]),
                readFile ?? (path => Files.GetValueOrDefault(path)),
                writeFile ?? ((path, content) => Written[path] = content),
                CredentialProcess.Runner,
                Handler,
                Time,
                (Microsoft.Extensions.Logging.ILoggerFactory?)logs ?? NullLoggerFactory.Instance);
            var signIn = sessionName is null
                ? $"Run `aws sso login` with a profile whose sso_start_url is {StartUrl} to sign in again."
                : $"Run `aws sso login --sso-session {sessionName}` to sign in again.";
            Provider = new SsoCredentialsProvider(new SsoCredentialsProvider.Settings(StartUrl, SsoRegion, "123456789012", roleName, sessionName, signIn), dependencies);
        }

        public ManualTimeProvider Time { get; } = new(Now);

        public ConcurrentDictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, string> Written { get; } = new(StringComparer.Ordinal);

        public StubHandler Handler { get; }

        public SsoCredentialsProvider Provider { get; }

        public string TokenPath(string key) => Path.Combine(Home, ".aws", "sso", "cache", key + ".json");

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
