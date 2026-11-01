using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class DefaultCredentialsProviderTests
{
    private const string CredentialsPath = "/aws/credentials";
    private const string ConfigPath = "/aws/config";
    private const string EnvironmentKeyId = "AKIAENVIRONMENTEXAMPLE";
    private const string ProfileKeyId = "AKIAPROFILEEXAMPLE";
    private const string ContainerKeyId = "ASIACONTAINEREXAMPLE";
    private const string InstanceMetadataKeyId = "ASIAIMDSEXAMPLE";

    private static readonly (string Name, string Value)[] SharedFiles = [("AWS_SHARED_CREDENTIALS_FILE", CredentialsPath), ("AWS_CONFIG_FILE", ConfigPath)];

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_CreatesProviderForTheProcessEnvironment()
    {
        // Arrange (constructing neither reads the environment, nor the files, nor calls the network)

        // Act
        var provider = new DefaultCredentialsProvider();

        // Assert
        await Assert.That(provider).IsAssignableTo<ICredentialsProvider>();
    }

    [Test]
    public async Task Constructor_Seams_TouchesNeitherEnvironmentNorFilesNorNetwork()
    {
        // Arrange
        var touched = 0;
        Func<string, string?> environment = _ =>
        {
            touched++;
            return null;
        };
        ReadTextFile files = _ =>
        {
            touched++;
            return null;
        };
        using var handler = new StubHandler(_ =>
        {
            touched++;
            return StubResponse.Json("{}");
        });

        // Act
        _ = new DefaultCredentialsProvider(environment, files, handler, new ManualTimeProvider(), NullLoggerFactory.Instance, null);

        // Assert
        await Assert.That(touched).IsEqualTo(0);
    }

    [Test]
    public async Task GetCredentialsAsync_AllSourcesAvailable_TheEnvironmentWins()
    {
        // Arrange
        var world = new World(
            variables:
            [
                ("AWS_ACCESS_KEY_ID", EnvironmentKeyId),
                ("AWS_SECRET_ACCESS_KEY", SecretAccessKey),
                ("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"),
                .. SharedFiles,
            ],
            files: [(CredentialsPath, $"[default]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = s\n")]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, world.FileReads, world.Handler.Requests.Count)).IsEqualTo((EnvironmentKeyId, 0, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_NoEnvironmentCredentials_TheProfileWinsOverTheNetwork()
    {
        // Arrange
        var world = new World(
            variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles],
            files: [(CredentialsPath, $"[default]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = s\n")]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo((ProfileKeyId, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_AwsProfileSet_TheEnvironmentStillComesFirst()
    {
        // Arrange (as in the AWS SDKs: the profile does not switch the variables off)
        var world = new World(
            variables:
            [
                ("AWS_ACCESS_KEY_ID", EnvironmentKeyId),
                ("AWS_SECRET_ACCESS_KEY", SecretAccessKey),
                ("AWS_PROFILE", "dev"),
                .. SharedFiles,
            ],
            files: [(CredentialsPath, $"[dev]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = s\n")]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(EnvironmentKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_AwsProfileSet_UsesThatProfile()
    {
        // Arrange
        var world = new World(
            variables: [("AWS_PROFILE", "dev"), .. SharedFiles],
            files: [(CredentialsPath, $"[default]\naws_access_key_id = WRONG\naws_secret_access_key = s\n[dev]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = s\n")]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(ProfileKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_AwsProfileNamesAMissingProfile_FailsAndTheMetadataServiceGetsNoRequest()
    {
        // Arrange (a typo in AWS_PROFILE on an instance with a role: moving on would sign as the instance, not as the profile)
        var world = new World(
            variables: [("AWS_PROFILE", "prdo"), .. SharedFiles],
            files: [(CredentialsPath, $"[prod]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = s\n")]);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.Contains("the profile 'prdo' was not found", StringComparison.Ordinal), world.MetadataRequests)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_AwsProfileWithoutKeys_MovesOn()
    {
        // Arrange (a profile can hold the region alone, and the credentials come from the machine)
        var world = new World(variables: [("AWS_PROFILE", "dev"), .. SharedFiles], files: [(ConfigPath, "[profile dev]\nregion = eu-west-1\n")]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(InstanceMetadataKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_EmptyAwsProfile_MovesOnLikeAnUnsetOne()
    {
        // Arrange
        var world = new World(variables: [("AWS_PROFILE", string.Empty), .. SharedFiles]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(InstanceMetadataKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_NoEnvironmentAndNoProfile_TheContainerEndpointWinsOverInstanceMetadata()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, string.Join(',', world.Handler.Requests.Select(request => request.RequestUri!.Authority).Distinct()))).IsEqualTo((ContainerKeyId, "169.254.170.2"));
    }

    [Test]
    public async Task GetCredentialsAsync_OnlyInstanceMetadata_UsesIt()
    {
        // Arrange
        var world = new World(variables: SharedFiles);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Select(request => request.RequestUri!.Authority).Distinct().Single())).IsEqualTo((InstanceMetadataKeyId, "169.254.169.254"));
    }

    [Test]
    public async Task GetCredentialsAsync_PartialEnvironment_FailsInsteadOfFallingThrough()
    {
        // Arrange (only the access key is set: a configuration error, not a reason to ask the network)
        var world = new World(variables: [("AWS_ACCESS_KEY_ID", EnvironmentKeyId), .. SharedFiles]);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("AWS_SECRET_ACCESS_KEY", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_PartialEnvironment_SendsNoRequest()
    {
        // Arrange
        var world = new World(variables: [("AWS_ACCESS_KEY_ID", EnvironmentKeyId), .. SharedFiles]);
        await Assert.That(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var requests = world.Handler.Requests.Count;

        // Assert
        await Assert.That(requests).IsEqualTo(0);
    }

    [Test]
    public async Task GetCredentialsAsync_SecretWithoutAccessKey_MovesOn()
    {
        // Arrange (botocore tests the access key only)
        var world = new World(
            variables: [("AWS_SECRET_ACCESS_KEY", SecretAccessKey), .. SharedFiles],
            files: [(CredentialsPath, $"[default]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = s\n")]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(ProfileKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_MalformedSharedFile_FailsInsteadOfFallingThrough()
    {
        // Arrange (a container endpoint is available, but the file is broken)
        var world = new World(
            variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles],
            files: [(CredentialsPath, "[default]\nthis is not valid\n")]);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("'/aws/credentials' is malformed at line 2", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("role_arn = arn:aws:iam::123456789012:role/dev\nsource_profile = base", "role_arn")]
    [Arguments("sso_session = corp", "sso_session")]
    [Arguments("web_identity_token_file = /token\nrole_arn = arn:aws:iam::123456789012:role/dev", "web_identity_token_file")]
    [Arguments("credential_process = /usr/bin/creds", "credential_process")]
    public async Task GetCredentialsAsync_ProfileNeedsAnUnsupportedFeature_FailsInsteadOfUsingTheMetadataService(string settings, string setting)
    {
        // Arrange (the instance would hand out the credentials of its own role: the wrong principal)
        var world = new World(variables: SharedFiles, files: [(ConfigPath, $"[default]\n{settings}\n")]);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining($"The profile 'default' uses the setting '{setting}'", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerEndpointConfiguredButBroken_FailsInsteadOfFallingThrough()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://example.com/creds"), .. SharedFiles]);

        // Act
        var act = async () => await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("is not allowed over http", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerEndpointAnswersExpiredCredentials_FailsAndTheMetadataServiceGetsNoRequest()
    {
        // Arrange (a pod with a skewed clock on a node whose instance role answers: moving on would sign as the node)
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], containerAnswer: (time, _) => Task.FromResult(StubResponse.Json(ContainerJson(time.GetUtcNow().AddMinutes(-1), ContainerKeyId))));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.Contains("already expired", StringComparison.Ordinal), world.MetadataRequests)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerEndpointAnswersWithoutAllFields_FailsAndTheMetadataServiceGetsNoRequest()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], containerAnswer: (_, _) => Task.FromResult(StubResponse.Json($$"""{"AccessKeyId":"{{ContainerKeyId}}"}""")));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.Contains("does not contain", StringComparison.Ordinal), world.MetadataRequests)).IsEqualTo((true, 0));
    }

    [Test]
    [Arguments(500, "unavailable", "HTTP 500")]
    [Arguments(200, "not json", "not a JSON object")]
    public async Task GetCredentialsAsync_ContainerEndpointKeepsAnsweringBadly_FailsAndTheMetadataServiceGetsNoRequest(int status, string body, string expected)
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], containerAnswer: (_, _) => Task.FromResult(StubResponse.Json(body, (System.Net.HttpStatusCode)status)));
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Act
        await world.AdvanceThroughContainerRetriesAsync(attemptTakes: TimeSpan.Zero);
        var exception = await failing;

        // Assert
        await Assert.That((exception.Message.Contains(expected, StringComparison.Ordinal), world.MetadataRequests)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerEndpointRefusesConnections_FailsAndTheMetadataServiceGetsNoRequest()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], containerAnswer: (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Act
        await world.AdvanceThroughContainerRetriesAsync(attemptTakes: TimeSpan.Zero);
        var exception = await failing;

        // Assert
        await Assert.That((exception.Message.Contains("failed (ConnectionError)", StringComparison.Ordinal), world.MetadataRequests)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerEndpointTimesOut_FailsAndTheMetadataServiceGetsNoRequest()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], containerAnswer: HangAsync);
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Act
        await world.AdvanceThroughContainerRetriesAsync(attemptTakes: TimeSpan.FromSeconds(2));
        var exception = await failing;

        // Assert
        await Assert.That((exception.Message.Contains("timed out", StringComparison.Ordinal), world.MetadataRequests)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityConfiguredWithoutEnvironmentCredentials_FailsInsteadOfUsingTheNode()
    {
        // Arrange (IRSA on EKS: moving on would use the credentials of the node)
        var world = new World(variables: [("AWS_WEB_IDENTITY_TOKEN_FILE", "/var/run/secrets/eks.amazonaws.com/serviceaccount/token"), ("AWS_ROLE_ARN", "arn:aws:iam::123456789012:role/pod"), .. SharedFiles]);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message.StartsWith("AWS_WEB_IDENTITY_TOKEN_FILE is set (web identity federation", StringComparison.Ordinal), world.Handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityConfiguredWithoutEnvironmentCredentials_MessageSaysWhatToDoInstead()
    {
        // Arrange
        var world = new World(variables: [("AWS_WEB_IDENTITY_TOKEN_FILE", "/var/run/secrets/eks.amazonaws.com/serviceaccount/token"), .. SharedFiles]);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).EndsWith("Pass credentials in the client options, or use environment variables, a profile with access keys, or EKS Pod Identity.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityConfiguredWithEnvironmentCredentials_TheEnvironmentWins()
    {
        // Arrange
        var world = new World(variables: [("AWS_WEB_IDENTITY_TOKEN_FILE", "/token"), ("AWS_ACCESS_KEY_ID", EnvironmentKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), .. SharedFiles]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(EnvironmentKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_EmptyWebIdentityVariable_CountsAsUnset()
    {
        // Arrange
        var world = new World(variables: [("AWS_WEB_IDENTITY_TOKEN_FILE", string.Empty), .. SharedFiles]);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(InstanceMetadataKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_NothingAvailable_ListsWhatWasTriedAndWhyInOrder()
    {
        // Arrange (the metadata service is switched off, as on a developer machine that must not wait for it)
        var world = new World(variables: [("AWS_EC2_METADATA_DISABLED", "true"), .. SharedFiles]);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo(
            "No AWS credentials were found. Set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY, configure a profile in ~/.aws/credentials, or pass a credentials provider in the client options. " +
            "Tried, in order: environment variables (AWS_ACCESS_KEY_ID is not set or empty); " +
            "web identity token file (AWS_WEB_IDENTITY_TOKEN_FILE is not set); " +
            "shared credentials and config files (the profile 'default' was not found (credentials file '/aws/credentials' (not found), config file '/aws/config' (not found))); " +
            "container credentials endpoint (neither AWS_CONTAINER_CREDENTIALS_RELATIVE_URI nor AWS_CONTAINER_CREDENTIALS_FULL_URI is set); " +
            "EC2 instance metadata service (it is disabled by AWS_EC2_METADATA_DISABLED=true).");
    }

    [Test]
    public async Task GetCredentialsAsync_MetadataServiceUnreachable_MessageSaysWhy()
    {
        // Arrange (a machine that is not on EC2)
        var world = new World(variables: SharedFiles, refuseConnections: true);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).EndsWith("EC2 instance metadata service (the token request failed (ConnectionError)).", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_NothingAvailableThenCredentialsAppear_TheFailureIsNotCached()
    {
        // Arrange
        var world = new World(variables: [("AWS_EC2_METADATA_DISABLED", "true"), .. SharedFiles]);
        await Assert.That(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();
        world.Set("AWS_ACCESS_KEY_ID", EnvironmentKeyId);
        world.Set("AWS_SECRET_ACCESS_KEY", SecretAccessKey);

        // Act
        var credentials = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(EnvironmentKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_SourceChosen_LaterCallsStayWithItAndReturnTheSameInstance()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles]);
        var first = await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.Set("AWS_ACCESS_KEY_ID", EnvironmentKeyId);
        world.Set("AWS_SECRET_ACCESS_KEY", SecretAccessKey);

        // Act
        var second = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), world.Handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_SourceChosen_LogsItOnceAtDebugLevel()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(variables: [("AWS_ACCESS_KEY_ID", EnvironmentKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), .. SharedFiles], loggerFactory: logs);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.Category, entry.Level, entry.EventId.Id, entry.Message)).Single())
            .IsEqualTo(("DynaSharp.Credentials.DefaultCredentialsProvider", LogLevel.Debug, 20, "Credentials come from the environment variables."));
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerSourceChosen_LogsItsName()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], loggerFactory: logs);

        // Act
        await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single().Message).IsEqualTo("Credentials come from the container credentials endpoint.");
    }

    [Test]
    public async Task GetCredentialsAsync_NothingChosen_LogsNothing()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(variables: [("AWS_EC2_METADATA_DISABLED", "true"), .. SharedFiles], loggerFactory: logs);

        // Act
        await Assert.That(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(logs.Entries).IsEmpty();
    }

    [Test]
    public async Task GetCredentialsAsync_ManyThreadsAtTheSameTime_ShareOneInstanceAndLogOnce()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(variables: [("AWS_ACCESS_KEY_ID", EnvironmentKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), .. SharedFiles], loggerFactory: logs);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(
                async () =>
                {
                    start.Wait();
                    return await world.Provider.GetCredentialsAsync(CancellationToken.None);
                }))
            .ToArray();

        // Act
        start.Set();
        var results = await Task.WhenAll(tasks);

        // Assert
        await Assert.That((results.Distinct().Count(), logs.Entries.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_ContainerRefreshFails_WarningGoesToTheLoggerFactoryOfTheChain()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory(LogLevel.Warning);
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles], loggerFactory: logs);
        await world.Provider.GetCredentialsAsync(CancellationToken.None);
        world.ContainerFails = true;
        world.Time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(12));

        // Act
        var refresh = world.Provider.GetCredentialsAsync(CancellationToken.None).AsTask();
        for (var retry = 1; retry <= 2; retry++)
        {
            await WaitForTimersAsync(world.Time, 2 + (2 * retry) - 1);
            world.Time.Advance(TimeSpan.FromSeconds(1));
        }

        await refresh;

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.Category, entry.Level)).Single()).IsEqualTo(("DynaSharp.Credentials.ContainerCredentialsProvider", LogLevel.Warning));
    }

    [Test]
    public async Task GetCredentialsAsync_CancelledWhileWaitingForTheMetadataService_ThrowsOperationCanceledException()
    {
        // Arrange (the service never answers)
        var world = new World(variables: SharedFiles, hangOnTokenRequest: true);
        using var cancel = new CancellationTokenSource();
        var pending = world.Provider.GetCredentialsAsync(cancel.Token).AsTask();
        await world.TokenRequestStarted;

        // Act
        await cancel.CancelAsync();

        // Assert
        await Assert.That(async () => await pending).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetCredentialsAsync_FailureMessages_NeverContainSecrets()
    {
        // Arrange (every source is wrong in a different way; the files hold secrets that must not show up)
        var world = new World(
            variables: [("AWS_EC2_METADATA_DISABLED", "true"), ("AWS_PROFILE", "dev"), .. SharedFiles],
            files: [(CredentialsPath, $"[prod]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = {SecretAccessKey}\naws_session_token = {SessionToken}\n")]);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await world.Provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(new[] { ProfileKeyId, SecretAccessKey, SessionToken }.Any(secret => exception.Message.Contains(secret, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task TryInvalidate_ContainerSourceChosen_ReturnsTrueAndTheNextCallFetches()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles]);
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = world.Provider.TryInvalidate(rejected);
        var next = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, ReferenceEquals(next, rejected), world.Handler.Requests.Count)).IsEqualTo((true, false, 2));
    }

    [Test]
    public async Task TryInvalidate_MetadataSourceChosen_ReturnsTrue()
    {
        // Arrange
        var world = new World(variables: SharedFiles);
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = world.Provider.TryInvalidate(rejected);

        // Assert
        await Assert.That(invalidated).IsTrue();
    }

    [Test]
    public async Task TryInvalidate_EnvironmentChosen_ReturnsFalseAndKeepsTheCredentials()
    {
        // Arrange (static keys: there is nothing to fetch, and the chain does not move on to another source)
        var world = new World(variables: [("AWS_ACCESS_KEY_ID", EnvironmentKeyId), ("AWS_SECRET_ACCESS_KEY", "secret"), .. SharedFiles]);
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = world.Provider.TryInvalidate(rejected);
        var next = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, ReferenceEquals(next, rejected))).IsEqualTo((false, true));
    }

    [Test]
    public async Task TryInvalidate_ProfileChosen_ReturnsFalse()
    {
        // Arrange
        var world = new World(
            variables: SharedFiles,
            files: [(CredentialsPath, $"[default]\naws_access_key_id = {ProfileKeyId}\naws_secret_access_key = secret\n")]);
        var rejected = await world.Provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = world.Provider.TryInvalidate(rejected);

        // Assert
        await Assert.That(invalidated).IsFalse();
    }

    [Test]
    public async Task TryInvalidate_NothingChosenYet_ReturnsFalseWithoutLookingUpAnything()
    {
        // Arrange
        var world = new World(variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/x"), .. SharedFiles]);

        // Act
        var invalidated = world.Provider.TryInvalidate(new AwsCredentials("someone", "else"));

        // Assert
        await Assert.That((invalidated, world.Handler.Requests.Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var world = new World(variables: SharedFiles);

        // Act
        var act = () => world.Provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    private static async Task<StubResponse> HangAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return StubResponse.Json("{}");
    }

    /// <summary>A fake machine: environment, files and the two credential endpoints.</summary>
    private sealed class World
    {
        private readonly Dictionary<string, string> _variables;
        private readonly Dictionary<string, string> _files;
        private readonly TaskCompletionSource _tokenRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _refuseConnections;
        private readonly bool _hangOnTokenRequest;
        private readonly Func<TimeProvider, CancellationToken, Task<StubResponse>>? _containerAnswer;
        private int _fileReads;

        public World(
            (string Name, string Value)[] variables,
            (string Path, string Content)[]? files = null,
            ILoggerFactory? loggerFactory = null,
            bool refuseConnections = false,
            bool hangOnTokenRequest = false,
            Func<TimeProvider, CancellationToken, Task<StubResponse>>? containerAnswer = null)
        {
            _variables = variables.ToDictionary(v => v.Name, v => v.Value);
            _files = (files ?? []).ToDictionary(f => f.Path, f => f.Content);
            _refuseConnections = refuseConnections;
            _hangOnTokenRequest = hangOnTokenRequest;
            _containerAnswer = containerAnswer;
            Time = new ManualTimeProvider();
            Handler = new StubHandler(RespondAsync);
            Provider = new DefaultCredentialsProvider(
                GetVariable,
                path =>
                {
                    Interlocked.Increment(ref _fileReads);
                    return _files.GetValueOrDefault(path);
                },
                Handler,
                Time,
                loggerFactory ?? NullLoggerFactory.Instance,
                new Random(1));
        }

        public ManualTimeProvider Time { get; }

        public StubHandler Handler { get; }

        public DefaultCredentialsProvider Provider { get; }

        public bool ContainerFails { get; set; }

        public int FileReads => Volatile.Read(ref _fileReads);

        public int MetadataRequests => Handler.Requests.Count(request => request.RequestUri!.Authority == "169.254.169.254");

        public Task TokenRequestStarted => _tokenRequestStarted.Task;

        public void Set(string name, string value)
        {
            lock (_variables)
            {
                _variables[name] = value;
            }
        }

        /// <summary>Moves the clock through the three attempts of a first load, each with its own timer and with a timer for the pause after it.</summary>
        public async Task AdvanceThroughContainerRetriesAsync(TimeSpan attemptTakes)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await WaitForTimersAsync(Time, (2 * attempt) - 1);
                Time.Advance(attemptTakes);
                if (attempt < 3)
                {
                    await WaitForTimersAsync(Time, 2 * attempt);
                    Time.Advance(TimeSpan.FromSeconds(1));
                }
            }
        }

        private string? GetVariable(string name)
        {
            lock (_variables)
            {
                return _variables.GetValueOrDefault(name);
            }
        }

        private async Task<StubResponse> RespondAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            if (_refuseConnections)
            {
                throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
            }

            if (request.RequestUri!.Authority == "169.254.170.2")
            {
                if (_containerAnswer is { } answer)
                {
                    return await answer(Time, cancellationToken);
                }

                return ContainerFails
                    ? StubResponse.Json("unavailable", System.Net.HttpStatusCode.ServiceUnavailable)
                    : StubResponse.Json(ContainerJson(Time.GetUtcNow().AddHours(6), ContainerKeyId));
            }

            if (request.Method == HttpMethod.Put)
            {
                _tokenRequestStarted.TrySetResult();
                if (_hangOnTokenRequest)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return StubResponse.Json("token-v2");
            }

            return request.RequestUri.AbsolutePath[^1] == '/'
                ? StubResponse.Json("my-role")
                : StubResponse.Json(InstanceMetadataJson(Time.GetUtcNow().AddHours(6), InstanceMetadataKeyId));
        }
    }
}
