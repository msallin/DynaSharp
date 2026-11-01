using System.Net;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class ContainerCredentialsProviderTests
{
    // An ECS task: the relative URI contains the id of the credentials, which must not end up in a message or a log.
    private const string CredentialId = "12345678-90ab-cdef-1234-567890abcdef";
    private const string RelativeUri = "/v2/credentials/" + CredentialId;

    private static readonly Func<string, string?> EcsEnvironment = Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", RelativeUri));

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_CreatesProviderForTheProcessEnvironment()
    {
        // Arrange (constructing neither reads the environment nor calls the network)

        // Act
        var provider = new ContainerCredentialsProvider();

        // Assert
        await Assert.That(provider).IsAssignableTo<ICredentialsProvider>();
    }

    [Test]
    public async Task GetCredentialsAsync_NeitherVariableSet_ThrowsCredentialsUnavailableExceptionWithoutARequest()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env());

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the container credentials endpoint: neither AWS_CONTAINER_CREDENTIALS_RELATIVE_URI nor AWS_CONTAINER_CREDENTIALS_FULL_URI is set.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_NeitherVariableSet_SendsNoRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env());
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var requests = handler.Requests.Count;

        // Assert
        await Assert.That(requests).IsEqualTo(0);
    }

    [Test]
    public async Task TryGetCredentialsAsync_EmptyVariables_CountAsUnset()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", string.Empty), ("AWS_CONTAINER_CREDENTIALS_FULL_URI", string.Empty)));

        // Act
        var outcome = await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Reason).IsEqualTo("neither AWS_CONTAINER_CREDENTIALS_RELATIVE_URI nor AWS_CONTAINER_CREDENTIALS_FULL_URI is set");
    }

    [Test]
    public async Task GetCredentialsAsync_EcsSampleResponse_ReturnsAllFields()
    {
        // Arrange (the example output of the ECS documentation, with an expiration in the future)
        var time = new ManualTimeProvider(new DateTimeOffset(2021, 1, 15, 18, 51, 53, TimeSpan.Zero));
        const string sample = """{"RoleArn":"arn:aws:iam::123456789012:role/SSMTaskRole-SSMFargateTaskIAMRole-DASWWSF2WGD6","AccessKeyId":"AKIAIOSFODNN7EXAMPLE","SecretAccessKey":"wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY","Token":"IQoJb3JpZ2luX2VjEEM/Example==","Expiration":"2021-01-16T00:51:53Z"}""";
        using var handler = new StubHandler(_ => StubResponse.Json(sample));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo(("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "IQoJb3JpZ2luX2VjEEM/Example==", new DateTimeOffset(2021, 1, 16, 0, 51, 53, TimeSpan.Zero)));
    }

    [Test]
    public async Task GetCredentialsAsync_RelativeUri_GetsFromTheEcsAddress()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((handler.Requests.Single().Method, handler.Requests.Single().RequestUri!.AbsoluteUri))
            .IsEqualTo((HttpMethod.Get, "http://169.254.170.2/v2/credentials/" + CredentialId));
    }

    [Test]
    public async Task GetCredentialsAsync_RelativeUriWithQuery_KeepsTheQuery()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/get-credentials?a=1")), time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("http://169.254.170.2/get-credentials?a=1");
    }

    [Test]
    public async Task GetCredentialsAsync_Request_AsksForJson()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Accept"]).IsEqualTo("application/json");
    }

    [Test]
    public async Task GetCredentialsAsync_NoToken_SendsNoAuthorizationHeader()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers.ContainsKey("Authorization")).IsFalse();
    }

    [Test]
    public async Task GetCredentialsAsync_RelativeAndFullUri_RelativeWins()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/relative"), ("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost:8080/full"));
        var provider = Create(handler, environment, time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("http://169.254.170.2/relative");
    }

    [Test]
    public async Task GetCredentialsAsync_FullUriWithTokenFile_SendsTheTokenOfTheFile()
    {
        // Arrange (the settings that EKS Pod Identity puts into a pod, from the EKS documentation)
        const string tokenFile = "/var/run/secrets/pods.eks.amazonaws.com/serviceaccount/eks-pod-identity-token";
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var environment = Env(
            ("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://169.254.170.23/v1/credentials"),
            ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", tokenFile));
        var provider = Create(handler, environment, time, Files((tokenFile, "eyJhbGciOiJSUzI1NiIs.pod-identity-token")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((handler.Requests.Single().RequestUri!.AbsoluteUri, handler.Requests.Single().Headers["Authorization"]))
            .IsEqualTo(("http://169.254.170.23/v1/credentials", "eyJhbGciOiJSUzI1NiIs.pod-identity-token"));
    }

    [Test]
    public async Task GetCredentialsAsync_TokenInVariable_SendsIt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/get-credential"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN", "Basic abcd"));
        var provider = Create(handler, environment, time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).IsEqualTo("Basic abcd");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileAndTokenVariable_TheFileWins()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var environment = Env(
            ("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/get-credential"),
            ("AWS_CONTAINER_AUTHORIZATION_TOKEN", "from-variable"),
            ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/token"));
        var provider = Create(handler, environment, time, Files(("/token", "from-file")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).IsEqualTo("from-file");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileWithTrailingNewline_SendsTheTrimmedToken()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/token"));
        var provider = Create(handler, environment, time, Files(("/token", "  token-value \r\n")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).IsEqualTo("token-value");
    }

    [Test]
    public async Task GetCredentialsAsync_EmptyTokenVariable_SendsNoAuthorizationHeader()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN", string.Empty), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", string.Empty));
        var provider = Create(handler, environment, time);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers.ContainsKey("Authorization")).IsFalse();
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileRotates_EveryFetchReadsTheFileAgain()
    {
        // Arrange (EKS rewrites the token file before it expires)
        var time = new ManualTimeProvider();
        var token = "token-1";
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(1))));
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/token"));
        var provider = Create(handler, environment, time, path => path == "/token" ? token : null);
        await provider.GetCredentialsAsync(CancellationToken.None);
        token = "token-2";
        time.Advance(TimeSpan.FromMinutes(50));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(string.Join(',', handler.Requests.Select(request => request.Headers["Authorization"]))).IsEqualTo("token-1,token-2");
    }

    [Test]
    [Arguments("http://169.254.170.2/v2/credentials/x")]
    [Arguments("http://169.254.170.23/v1/credentials")]
    [Arguments("http://[fd00:ec2::23]/v1/credentials")]
    [Arguments("http://localhost:8080/creds")]
    [Arguments("http://LOCALHOST/creds")]
    [Arguments("http://127.0.0.1:1234/creds")]
    [Arguments("http://127.5.5.5/creds")]
    [Arguments("http://[::1]:8080/creds")]
    [Arguments("https://credentials.example.com/creds")]
    [Arguments("https://169.254.169.254/creds")]
    [Arguments("https://10.0.0.5:8443/creds")]
    public async Task GetCredentialsAsync_AllowedFullUri_Fetches(string endpoint)
    {
        // Arrange (the rule of botocore: https always, http only for loopback and the container endpoint addresses)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", endpoint)), time);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    [Arguments("http://example.com/creds", "example.com")]
    [Arguments("http://169.254.169.254/latest/meta-data", "169.254.169.254")]
    [Arguments("http://169.254.170.3/creds", "169.254.170.3")]
    [Arguments("http://10.0.0.5/creds", "10.0.0.5")]
    [Arguments("http://localhost.evil.example/creds", "localhost.evil.example")]
    [Arguments("http://[fd00:ec2::254]/creds", "fd00:ec2::254")]
    [Arguments("http://[2001:db8::1]/creds", "2001:db8::1")]
    public async Task GetCredentialsAsync_DisallowedHostOverHttp_FailsNamingTheHostWithoutARequest(string endpoint, string host)
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", endpoint)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining($"points to the host '{host}', which is not allowed over http", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_DisallowedHostOverHttp_MessageHasTheHostButNotThePathOrTheQuery()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", $"http://example.com/{CredentialId}?token=token-value-1")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.Contains("'example.com'", StringComparison.Ordinal), new[] { CredentialId, "token-value-1" }.Any(secret => exception.Message.Contains(secret, StringComparison.Ordinal)))).IsEqualTo((true, false));
    }

    [Test]
    public async Task GetCredentialsAsync_DisallowedHostOverHttp_MessageListsTheAddressesThatAreAllowed()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://example.com/creds")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).EndsWith("Use https, a loopback address, or one of 169.254.170.2, 169.254.170.23, fd00:ec2::23.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("not a url")]
    [Arguments("/relative/path")]
    [Arguments("ftp://localhost/creds")]
    [Arguments("file:///etc/passwd")]
    public async Task GetCredentialsAsync_FullUriThatIsNoHttpUrl_FailsWithoutEchoingIt(string endpoint)
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", endpoint)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("AWS_CONTAINER_CREDENTIALS_FULL_URI must be an absolute http or https URL.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_FullUriWithUserInfo_FailsWithoutEchoingThePassword()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://user:hunter2@localhost/creds")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("AWS_CONTAINER_CREDENTIALS_FULL_URI must not contain user info.");
    }

    [Test]
    [Arguments("v2/credentials")]
    [Arguments("http://169.254.170.2/v2")]
    [Arguments(".evil.example/x")]
    public async Task GetCredentialsAsync_RelativeUriWithoutLeadingSlash_Fails(string relative)
    {
        // Arrange (a relative URI must not be able to change the host)
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", relative)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI must be a path that starts with '/'.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileMissing_FailsWithoutARequest()
    {
        // Arrange
        using var handler = new StubHandler();
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/missing"));
        var provider = Create(handler, environment);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("The file named by AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE does not exist.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileUnreadable_FailsWithTheCauseAsInnerException()
    {
        // Arrange
        using var handler = new StubHandler();
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/token"));
        var provider = Create(handler, environment, files: _ => throw new UnauthorizedAccessException("Access to the path '/token' is denied."));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, exception.InnerException is UnauthorizedAccessException))
            .IsEqualTo(("The file named by AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE cannot be read: access denied.", true));
    }

    [Test]
    [Arguments("")]
    [Arguments("  \r\n")]
    public async Task GetCredentialsAsync_TokenFileEmpty_Fails(string content)
    {
        // Arrange
        using var handler = new StubHandler();
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/token"));
        var provider = Create(handler, environment, files: Files(("/token", content)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("The file named by AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE is empty.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TokenFileWithLineBreakInside_FailsWithoutEchoingTheToken()
    {
        // Arrange
        using var handler = new StubHandler();
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", "/token"));
        var provider = Create(handler, environment, files: Files(("/token", "secret-part-1\nsecret-part-2")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The authorization token must not contain line breaks.");
    }

    [Test]
    public async Task GetCredentialsAsync_TokenVariableWithLineBreak_Fails()
    {
        // Arrange
        using var handler = new StubHandler();
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://localhost/c"), ("AWS_CONTAINER_AUTHORIZATION_TOKEN", "abc\r\nX-Injected: 1"));
        var provider = Create(handler, environment);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("The authorization token must not contain line breaks.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwiceWithLongValidity_ReturnsTheSameInstanceWithOneRequest()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_FifteenMinutesBeforeExpiry_KeepsTheCredentialsAndOneMinuteLaterRefreshes()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var counter = 0;
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(1), accessKeyId: $"ASIAKEY{counter++}")));
        var provider = Create(handler, EcsEnvironment, time);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(45));
        var atTheBoundary = await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));

        // The stub completes at once, so the refresh this call starts is done when it returns.
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var refreshed = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, atTheBoundary), refreshed.AccessKeyId, handler.Requests.Count)).IsEqualTo((true, "ASIAKEY1", 2));
    }

    [Test]
    public async Task GetCredentialsAsync_AdvisoryRefreshFails_KeepsTheCredentialsAndLogsAWarning()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(1))));
        handler.DefaultResponse = StubResponse.Json("service unavailable", HttpStatusCode.ServiceUnavailable);
        var provider = Create(handler, EcsEnvironment, time, logger: logs.CreateLogger("test"));
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // 12 minutes are left, so the refresh still ends in the advisory period after the seconds that the retries of the fetch take
        time.Advance(TimeSpan.FromMinutes(48));

        // Act
        var refresh = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 2);
        var duringFailure = await refresh;

        // Assert
        await Assert.That((ReferenceEquals(first, duringFailure), logs.Entries.Select(entry => (entry.EventId.Id, entry.Level)).Single())).IsEqualTo((true, (21, LogLevel.Warning)));
    }

    [Test]
    public async Task GetCredentialsAsync_AdvisoryRefreshFails_WarningHasNoSecretAndNoUri()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(1))));
        handler.DefaultResponse = StubResponse.Json("service unavailable", HttpStatusCode.ServiceUnavailable);
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", RelativeUri), ("AWS_CONTAINER_AUTHORIZATION_TOKEN", "token-value-1"));
        var provider = Create(handler, environment, time, logger: logs.CreateLogger("test"));
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(48));
        var refresh = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 2);
        await refresh;

        // Act
        var logged = string.Join('\n', logs.Entries.Select(entry => entry.Message));

        // Assert
        await Assert.That(new[] { SecretAccessKey, SessionToken, "token-value-1", CredentialId }.Any(secret => logged.Contains(secret, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task GetCredentialsAsync_MandatoryRefreshFails_ThrowsCredentialsUnavailableException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(1))));
        handler.DefaultResponse = StubResponse.Json("service unavailable", HttpStatusCode.ServiceUnavailable);
        var provider = Create(handler, EcsEnvironment, time);
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(51));

        // Act
        var refresh = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 2);
        var exception = await refresh;

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The container credentials endpoint at 169.254.170.2 did not return credentials in 3 attempts. The last attempt: HTTP 503.");
    }

    [Test]
    public async Task GetCredentialsAsync_FirstFetchFailsThenEndpointRecovers_NextCallSucceeds()
    {
        // Arrange (the failure is not cached)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.DefaultResponse = StubResponse.Json("no", HttpStatusCode.InternalServerError);
        var provider = Create(handler, EcsEnvironment, time);
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 1);
        await failing;
        handler.DefaultResponse = StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6)));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_StatusOtherThan200ThenSuccess_RetriesAfterOneSecond()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("bad gateway", HttpStatusCode.BadGateway));
        handler.Enqueue(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await WaitForTimersAsync(time, 2);
        var requestsBeforeTheDelay = handler.Requests.Count;
        time.Advance(TimeSpan.FromSeconds(1));
        var credentials = await fetch;

        // Assert
        await Assert.That((requestsBeforeTheDelay, handler.Requests.Count, credentials.AccessKeyId)).IsEqualTo((1, 2, AccessKeyId));
    }

    [Test]
    public async Task GetCredentialsAsync_FailedAttempt_WaitsAFullSecondBeforeTheNextOne()
    {
        // Arrange (999 ms are not enough: the retry waits a full second on the configured clock)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("bad gateway", HttpStatusCode.BadGateway));
        handler.Enqueue(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await WaitForTimersAsync(time, 2);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(999));

        // Assert
        await Assert.That((fetch.IsCompleted, handler.Requests.Count)).IsEqualTo((false, 1));
        time.Advance(TimeSpan.FromMilliseconds(1));
        await fetch;
    }

    [Test]
    [Arguments("<html><body>Bad Gateway</body></html>")]
    [Arguments("[]")]
    [Arguments("")]
    public async Task GetCredentialsAsync_ResponseThatIsNoJsonObject_IsRetried(string body)
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(body));
        handler.Enqueue(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await AdvanceThroughRetriesAsync(time, retries: 1, firstTimer: 1);
        var credentials = await fetch;

        // Assert
        await Assert.That((credentials.AccessKeyId, handler.Requests.Count)).IsEqualTo((AccessKeyId, 2));
    }

    [Test]
    public async Task GetCredentialsAsync_ResponseIsNoJsonObject_IsRetriedAndFailsAfterThreeAttemptsSayingSo()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("<html>gateway</html>") };
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        var fetch = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 1);
        var exception = await fetch;

        // Assert
        await Assert.That((handler.Requests.Count, exception.Message)).IsEqualTo((3, "The container credentials endpoint at 169.254.170.2 did not return credentials in 3 attempts. The last attempt: the response is not a JSON object."));
    }

    [Test]
    public async Task GetCredentialsAsync_ConnectionRefused_IsRetriedAndFailsAfterThreeAttempts()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var attempts = 0;
        using var handler = new StubHandler(_ =>
        {
            attempts++;
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (169.254.170.2:80)");
        });
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        var fetch = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 1);
        var exception = await fetch;

        // Assert
        await Assert.That((attempts, exception.Message)).IsEqualTo((3, "The container credentials endpoint at 169.254.170.2 did not return credentials in 3 attempts. The last attempt: failed (ConnectionError)."));
    }

    [Test]
    public async Task GetCredentialsAsync_AttemptTakesLongerThanTwoSeconds_TimesOutAndIsRetried()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6)));
        });
        var provider = Create(handler, EcsEnvironment, time);
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await started.Task;

        // Act
        time.Advance(TimeSpan.FromSeconds(2));
        await WaitForTimersAsync(time, 2);
        time.Advance(TimeSpan.FromSeconds(1));
        var credentials = await fetch;

        // Assert
        await Assert.That((credentials.AccessKeyId, attempts)).IsEqualTo((AccessKeyId, 2));
    }

    [Test]
    public async Task GetCredentialsAsync_AttemptNotYetTwoSeconds_KeepsWaiting()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<StubResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            return await release.Task.WaitAsync(cancellationToken);
        });
        var provider = Create(handler, EcsEnvironment, time);
        var fetch = provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        await started.Task;

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1999));
        release.SetResult(StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6))));
        var credentials = await fetch;

        // Assert
        await Assert.That((credentials.AccessKeyId, handler.Requests.Count)).IsEqualTo((AccessKeyId, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ResponseTooLarge_IsAFailureOfTheAttempt()
    {
        // Arrange (a credentials document is about 1.5 KB; the client reads at most 64 KB)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(new string('x', 100_000)));
        var provider = Create(handler, EcsEnvironment, time);

        // Act
        var fetch = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 1);
        var exception = await fetch;

        // Assert
        await Assert.That(exception.Message).StartsWith("The container credentials endpoint at 169.254.170.2 did not return credentials in 3 attempts. The last attempt: failed (", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"AccessKeyId":"a"}""", "the response does not contain SecretAccessKey, Token, Expiration")]
    [Arguments("""{"AccessKeyId":"a","SecretAccessKey":"s","Token":"t","Expiration":"yesterday"}""", "the Expiration of the response is not a valid timestamp")]
    [Arguments("""{"code":"AccessDenied","message":"task is not allowed"}""", "the response does not contain AccessKeyId, SecretAccessKey, Token, Expiration")]
    public async Task GetCredentialsAsync_ResponseWithoutAllFields_FailsAtOnceWithoutRetry(string body, string problem)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(body));
        var provider = Create(handler, EcsEnvironment);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, handler.Requests.Count)).IsEqualTo(($"The container credentials endpoint returned an unusable response: {problem}.", 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ResponseWithoutAllFields_MessageDoesNotContainTheValuesThatWereThere()
    {
        // Arrange (the token is missing, the keys are not)
        var body = $$"""{"AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}","Expiration":"2030-01-01T00:00:00Z"}""";
        using var handler = new StubHandler(_ => StubResponse.Json(body));
        var provider = Create(handler, EcsEnvironment);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(new[] { AccessKeyId, SecretAccessKey, CredentialId }.Any(secret => exception.Message.Contains(secret, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task GetCredentialsAsync_FailedAttempts_MessageDoesNotContainTheUriOrTheToken()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json("denied", HttpStatusCode.Forbidden));
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", RelativeUri), ("AWS_CONTAINER_AUTHORIZATION_TOKEN", "token-value-1"));
        var provider = Create(handler, environment, time);
        var fetch = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        await AdvanceThroughRetriesAsync(time, retries: 2, firstTimer: 1);

        // Act
        var exception = await fetch;

        // Assert
        await Assert.That(new[] { CredentialId, "token-value-1", "denied" }.Any(secret => exception.Message.Contains(secret, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task GetCredentialsAsync_ManyCallersAtTheSameTime_ShareOneRequest()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6)));
        });
        var provider = Create(handler, EcsEnvironment, time);
        var callers = Enumerable.Range(0, 8).Select(_ => provider.GetCredentialsAsync(CancellationToken.None).AsTask()).ToArray();
        await started.Task;

        // Act
        release.SetResult();
        var results = await Task.WhenAll(callers);

        // Assert
        await Assert.That((results.Distinct().Count(), handler.Requests.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_OneCallerCancels_TheOthersStillGetTheCredentials()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(6)));
        });
        var provider = Create(handler, EcsEnvironment, time);
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
        await Assert.That((credentials.AccessKeyId, handler.Requests.Count)).IsEqualTo((AccessKeyId, 1));
    }

    [Test]
    public async Task TryGetCredentialsAsync_BrokenConfiguration_Throws()
    {
        // Arrange (a source that applies but is misconfigured stops the default chain)
        using var handler = new StubHandler();
        var provider = Create(handler, Env(("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://example.com/c")));

        // Act
        var act = async () => await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    public async Task TryInvalidate_TheCurrentCredentials_ReturnsTrueAndTheNextCallFetches()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(ContainerJson(time.GetUtcNow().AddHours(1))));
        var provider = Create(handler, EcsEnvironment, time);
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var next = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, ReferenceEquals(next, rejected), handler.Requests.Count)).IsEqualTo((true, false, 2));
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = Create(handler, EcsEnvironment);

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    private static ContainerCredentialsProvider Create(
        HttpMessageHandler handler,
        Func<string, string?> environment,
        ManualTimeProvider? time = null,
        ReadTextFile? files = null,
        ILogger? logger = null) =>
        new(environment, files ?? NoFiles, handler, time ?? new ManualTimeProvider(), logger ?? NullLogger.Instance);

    // Every failed attempt is followed by a one-second delay on the clock of the provider, which the test has to let pass. The
    // timers are created in this order: attempt 1, delay 1, attempt 2, delay 2, ... so the n-th delay is timer firstTimer + 2n - 1.
    private static async Task AdvanceThroughRetriesAsync(ManualTimeProvider time, int retries, int firstTimer)
    {
        for (var retry = 1; retry <= retries; retry++)
        {
            await WaitForTimersAsync(time, firstTimer + (2 * retry) - 1);
            time.Advance(TimeSpan.FromSeconds(1));
        }
    }
}
