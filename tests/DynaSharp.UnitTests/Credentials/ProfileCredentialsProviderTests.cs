using DynaSharp.Configuration;
using DynaSharp.Credentials;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class ProfileCredentialsProviderTests
{
    private const string CredentialsPath = "/aws/credentials";
    private const string ConfigPath = "/aws/config";
    private const string Keys = $"aws_access_key_id = {AccessKeyId}\naws_secret_access_key = {SecretAccessKey}\n";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);
    private static readonly DateTimeOffset Expiration = Now.AddHours(1);

    private static readonly Func<string, string?> Defaults = Env(("AWS_SHARED_CREDENTIALS_FILE", CredentialsPath), ("AWS_CONFIG_FILE", ConfigPath));

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_CreatesProviderForTheProcessEnvironment()
    {
        // Arrange (constructing neither reads the environment nor the files)

        // Act
        var provider = new ProfileCredentialsProvider();

        // Assert
        await Assert.That(provider).IsAssignableTo<ICredentialsProvider>();
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_ProfileName_CreatesProvider()
    {
        // Arrange

        // Act
        var provider = new ProfileCredentialsProvider("dev");

        // Assert
        await Assert.That(provider).IsAssignableTo<ICredentialsProvider>();
    }

    [Test]
    public async Task Constructor_NullProfileName_ThrowsArgumentNullException()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => new ProfileCredentialsProvider(name);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("profileName");
    }

    [Test]
    public async Task Constructor_EmptyProfileName_ThrowsArgumentException()
    {
        // Arrange

        // Act
        var act = () => new ProfileCredentialsProvider(string.Empty);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("profileName");
    }

    [Test]
    public async Task GetCredentialsAsync_DefaultProfileInCredentialsFile_ReturnsItsKeys()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {credentials.SecretAccessKey} {credentials.SessionToken}").IsEqualTo($"{AccessKeyId} {SecretAccessKey} ");
    }

    [Test]
    public async Task GetCredentialsAsync_SessionToken_IsReturned()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}aws_session_token = {SessionToken}\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SessionToken).IsEqualTo(SessionToken);
    }

    [Test]
    public async Task GetCredentialsAsync_SecurityTokenAndSessionToken_SecurityTokenWins()
    {
        // Arrange (the order of botocore)
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}aws_session_token = session\naws_security_token = security\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SessionToken).IsEqualTo("security");
    }

    [Test]
    public async Task GetCredentialsAsync_EmptySecurityTokenAndSessionToken_EmptyTokenWinsAndThereIsNoToken()
    {
        // Arrange (botocore takes the first of the two options that exists, even if it is empty)
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}aws_session_token = session\naws_security_token =\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SessionToken).IsNull();
    }

    [Test]
    public async Task GetCredentialsAsync_ProfileInConfigFile_ReturnsItsKeys()
    {
        // Arrange
        var provider = Create(Defaults, (ConfigPath, $"[profile dev]\n{Keys}"), "dev");

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_AwsProfileSet_UsesThatProfile()
    {
        // Arrange
        var environment = Env(("AWS_SHARED_CREDENTIALS_FILE", CredentialsPath), ("AWS_PROFILE", "prod"));
        var provider = Create(environment, (CredentialsPath, "[default]\naws_access_key_id = DEFAULTKEY\naws_secret_access_key = x\n[prod]\n" + Keys));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_ProfileNameGiven_IgnoresAwsProfile()
    {
        // Arrange
        var environment = Env(("AWS_SHARED_CREDENTIALS_FILE", CredentialsPath), ("AWS_PROFILE", "other"));
        var provider = Create(environment, (CredentialsPath, "[dev]\n" + Keys), "dev");

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_ProfileInBothFiles_CredentialsFileKeysWinAndNothingIsMixed()
    {
        // Arrange (the token of the config file does not go with the keys of the credentials file)
        var provider = Create(
            Defaults,
            (CredentialsPath, "[default]\naws_access_key_id = FROMCREDENTIALS\naws_secret_access_key = s1\n"),
            (ConfigPath, "[default]\naws_access_key_id = FROMCONFIG\naws_secret_access_key = s2\naws_session_token = tokenfromconfig\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {credentials.SecretAccessKey} {credentials.SessionToken}").IsEqualTo("FROMCREDENTIALS s1 ");
    }

    [Test]
    public async Task GetCredentialsAsync_KeysOnlyInConfigFile_UsesThem()
    {
        // Arrange
        var provider = Create(
            Defaults,
            (CredentialsPath, "[default]\nregion = eu-west-1\n"),
            (ConfigPath, "[default]\naws_access_key_id = FROMCONFIG\naws_secret_access_key = s2\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo("FROMCONFIG");
    }

    [Test]
    public async Task GetCredentialsAsync_StaticKeysInCredentialsFileAndProcessInConfigFile_UsesTheStaticKeys()
    {
        // Arrange (botocore prefers the credentials file to credential_process)
        var provider = Create(
            Defaults,
            (CredentialsPath, "[default]\n" + Keys),
            (ConfigPath, "[default]\ncredential_process = /usr/bin/creds\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_StaticKeysInCredentialsFileAndLoginSession_UsesTheStaticKeys()
    {
        // Arrange
        var provider = Create(
            Defaults,
            (CredentialsPath, "[default]\n" + Keys),
            (ConfigPath, "[default]\nlogin_session = arn:aws:iam::123456789012:user/dev\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwice_ReturnsTheSameInstanceAndReadsTheFilesOnce()
    {
        // Arrange
        var reads = 0;
        ReadTextFile counting = path =>
        {
            Interlocked.Increment(ref reads);
            return path == CredentialsPath ? $"[default]\n{Keys}" : null;
        };
        var provider = new ProfileCredentialsProvider(Defaults, counting, null);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), reads)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task GetCredentialsAsync_FailureThenFileAppears_ReadsAgainAndSucceeds()
    {
        // Arrange
        var files = new Dictionary<string, string>();
        var provider = new ProfileCredentialsProvider(Defaults, path => files.GetValueOrDefault(path), null);
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();
        files[CredentialsPath] = $"[default]\n{Keys}";

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_ConcurrentFirstCalls_AllReceiveTheSameInstance()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}"));
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(
                async () =>
                {
                    start.Wait();
                    return await provider.GetCredentialsAsync(CancellationToken.None);
                }))
            .ToArray();

        // Act
        start.Set();
        var results = await Task.WhenAll(tasks);

        // Assert
        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task GetCredentialsAsync_CancelledToken_StillReturnsTheKeys()
    {
        // Arrange (reading the files is synchronous, so there is nothing to cancel)
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var credentials = await provider.GetCredentialsAsync(cancelled.Token);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_NoFiles_ThrowsCredentialsUnavailableExceptionNamingTheProfileAndTheFiles()
    {
        // Arrange
        var provider = Create(Defaults);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("No credentials from the shared AWS files: the profile 'default' was not found (credentials file '/aws/credentials' (not found), config file '/aws/config' (not found)).", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ProfileWithoutAccessKey_ThrowsCredentialsUnavailableException()
    {
        // Arrange
        var provider = Create(Defaults, (ConfigPath, "[default]\nregion = eu-west-1\n"));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("profile 'default' has no aws_access_key_id", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_FileCannotBeRead_ThrowsCredentialsUnavailableExceptionSayingWhy()
    {
        // Arrange
        var provider = new ProfileCredentialsProvider(Defaults, _ => throw new UnauthorizedAccessException("Access to the path '/aws/credentials' is denied."), null);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("cannot be read: access denied", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("[default]\naws_access_key_id = " + AccessKeyId + "\n", "profile 'default' is incomplete")]
    [Arguments("[default]\naws_access_key_id = " + AccessKeyId + "\naws_secret_access_key =\n", "profile 'default' is incomplete")]
    [Arguments("[default]\naws_access_key_id =\naws_secret_access_key = " + SecretAccessKey + "\n", "profile 'default' is incomplete")]
    [Arguments("[default]\naws_access_key_id =\n  nested = " + SecretAccessKey + "\naws_secret_access_key = x\n", "profile 'default' is incomplete")]
    public async Task GetCredentialsAsync_OnlyOneOfTheKeys_FailsLoudlyNamingTheProfile(string credentialsFile, string expected)
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, credentialsFile));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).Contains(expected, StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_OnlyTheAccessKey_MessageDoesNotContainTheKey()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\naws_access_key_id = {AccessKeyId}\n"));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).DoesNotContain(AccessKeyId, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("login_session = arn:aws:iam::123456789012:user/dev", "login_session")]
    public async Task GetCredentialsAsync_UnsupportedSetting_FailsLoudlyNamingTheSettingAndTheProfile(string settings, string setting)
    {
        // Arrange (the profile exists in the config file only, so nothing can fall through to static keys)
        var provider = Create(Defaults, (ConfigPath, $"[profile dev]\n{settings}\n"), "dev");

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining($"The profile 'dev' uses the setting '{setting}'", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("login_session = arn:aws:iam::123456789012:user/dev", "'login_session' (the AWS login flow)")]
    public async Task GetCredentialsAsync_UnsupportedSetting_MessageNamesTheFeature(string settings, string description)
    {
        // Arrange
        var provider = Create(Defaults, (ConfigPath, $"[profile dev]\n{settings}\n"), "dev");

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining(description, StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ProcessAndStaticKeysBothInConfigFile_RunsTheProcessAsBotocoreDoes()
    {
        // Arrange
        var runner = new ProcessCredentialsProviderTests.FakeRunner(ProcessCredentialsProviderTests.Json(Expiration, "FROMPROCESS"));
        var provider = Create(runner, (ConfigPath, $"[default]\n{Keys}credential_process = /usr/bin/creds\n"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo("FROMPROCESS");
    }

    [Test]
    public async Task GetCredentialsAsync_Process_RunsTheCommandLineOfTheProfile()
    {
        // Arrange
        var runner = new ProcessCredentialsProviderTests.FakeRunner(ProcessCredentialsProviderTests.Json(Expiration));
        var provider = Create(runner, (ConfigPath, "[profile dev]\ncredential_process = creds --profile dev\n"), "dev");

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(string.Join(' ', [runner.Runs[0].FileName, .. runner.Runs[0].Arguments])).IsEqualTo("creds --profile dev");
    }

    [Test]
    [Arguments("credential_process =")]
    [Arguments("credential_process =\n  a = b")]
    public async Task GetCredentialsAsync_ProcessEmptyOrBlock_FailsNamingTheProfile(string setting)
    {
        // Arrange
        var runner = new ProcessCredentialsProviderTests.FakeRunner(ProcessCredentialsProviderTests.Json(Expiration));
        var provider = Create(runner, (ConfigPath, $"[profile dev]\n{setting}\n"), "dev");

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("The credential_process of the profile 'dev' names no program.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ProcessFails_FailsAgainAndRunsAgainOnTheNextCall()
    {
        // Arrange (the source is chosen, and a fetch that fails is not cached)
        var runner = new ProcessCredentialsProviderTests.FakeRunner(new ProcessResult(ProcessEnd.Exited, 1, []));
        var provider = Create(runner, (ConfigPath, "[default]\ncredential_process = creds\n"));
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("exited with code 1", StringComparison.Ordinal);
        await Assert.That(runner.Runs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetCredentialsAsync_RealProgram_ReturnsWhatItWrites()
    {
        // Arrange (the way of a profile, end to end, through a program that the operating system starts)
        using var program = FakeProgram.Create(ProcessCredentialsProviderTests.Json(DateTimeOffset.UtcNow.AddHours(1)), exitCode: 0);
        var provider = new ProfileCredentialsProvider(Defaults, Files((ConfigPath, $"[default]\ncredential_process = {program.CommandLine}\n")), null);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SecretAccessKey).IsEqualTo(SecretAccessKey);
    }

    [Test]
    public async Task TryInvalidate_Process_RunsItAgainOnTheNextCall()
    {
        // Arrange
        var runner = new ProcessCredentialsProviderTests.FakeRunner(ProcessCredentialsProviderTests.Json(Expiration, "FIRST"), ProcessCredentialsProviderTests.Json(Expiration, "SECOND"));
        var provider = Create(runner, (ConfigPath, "[default]\ncredential_process = creds\n"));
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, credentials.AccessKeyId)).IsEqualTo((true, "SECOND"));
    }

    [Test]
    public async Task TryInvalidate_StaticKeys_ReturnsFalse()
    {
        // Arrange (the files hold the keys once and for all)
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}"));
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);

        // Assert
        await Assert.That(invalidated).IsFalse();
    }

    [Test]
    public async Task TryInvalidate_BeforeTheFirstCall_ReturnsFalse()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}"));

        // Act
        var invalidated = provider.TryInvalidate(new AwsCredentials(AccessKeyId, SecretAccessKey));

        // Assert
        await Assert.That(invalidated).IsFalse();
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = Create(Defaults);

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    [Test]
    public async Task GetCredentialsAsync_SourceChosen_LogsTheProfileAndTheSourceOnceAtDebugLevel()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var runner = new ProcessCredentialsProviderTests.FakeRunner(ProcessCredentialsProviderTests.Json(Expiration));
        var dependencies = ProcessCredentialsProviderTests.Dependencies(runner, new ManualTimeProvider(Now)) with
        {
            GetEnvironmentVariable = Defaults,
            ReadFile = Files((ConfigPath, "[profile dev]\ncredential_process = creds\n")),
            LoggerFactory = logs,
        };
        var provider = new ProfileCredentialsProvider(dependencies, "dev");

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(string.Join('\n', logs.Entries.Select(entry => $"{entry.Level} {entry.EventId.Id} {entry.Message}")))
            .IsEqualTo("Debug 26 The profile 'dev' gets its credentials from its credential_process.");
    }

    [Test]
    public async Task GetCredentialsAsync_UnsupportedSetting_MessageSaysWhatToDoInstead()
    {
        // Arrange
        var provider = Create(Defaults, (ConfigPath, "[default]\nlogin_session = arn:aws:iam::123456789012:user/dev\n"));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("(the AWS login flow), which DynaSharp does not support. Use a profile with aws_access_key_id and aws_secret_access_key", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_MalformedFile_ThrowsCredentialsUnavailableExceptionWithFileAndLine()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}\nthis line is bad\n"));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("'/aws/credentials' is malformed at line 5", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_MalformedFile_MessageDoesNotContainTheSecretOrTheContent()
    {
        // Arrange (the secret is on the offending line)
        var provider = Create(Defaults, (CredentialsPath, $"[default]\naws_access_key_id = {AccessKeyId}\n{SecretAccessKey}\n"));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).DoesNotContain("wJalrXUtnFEMI", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_MalformedConfigFileAndValidCredentialsFile_StillFailsBecauseBotocoreParsesBoth()
    {
        // Arrange
        var provider = Create(Defaults, (CredentialsPath, $"[default]\n{Keys}"), (ConfigPath, "garbage\n"));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("'/aws/config' is malformed", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryGetCredentialsAsync_DefaultProfileNotFound_DoesNotApply()
    {
        // Arrange
        var provider = Create(Defaults);

        // Act
        var outcome = await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Kind).IsEqualTo(CredentialsOutcomeKind.DoesNotApply);
    }

    [Test]
    public async Task TryGetCredentialsAsync_ProfileOfAwsProfileNotFound_IsBroken()
    {
        // Arrange (someone asked for this profile, and the next source would be another principal)
        var environment = Env(("AWS_SHARED_CREDENTIALS_FILE", CredentialsPath), ("AWS_CONFIG_FILE", ConfigPath), ("AWS_PROFILE", "prod"));
        var provider = Create(environment);

        // Act
        var outcome = await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Kind).IsEqualTo(CredentialsOutcomeKind.Broken);
    }

    [Test]
    public async Task TryGetCredentialsAsync_ProfileOfTheConstructorNotFound_IsBroken()
    {
        // Arrange
        var provider = Create(Defaults, "prod");

        // Act
        var outcome = await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Kind).IsEqualTo(CredentialsOutcomeKind.Broken);
    }

    [Test]
    public async Task TryGetCredentialsAsync_UnsupportedSetting_Throws()
    {
        // Arrange (a broken source is an error, not a reason to try the next one)
        var provider = Create(Defaults, (ConfigPath, "[default]\nlogin_session = x\n"));

        // Act
        var act = async () => await provider.TryGetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    private static ProfileCredentialsProvider Create(ProcessCredentialsProviderTests.FakeRunner runner, (string Path, string Content) file, string? profileName = null) =>
        new(ProcessCredentialsProviderTests.Dependencies(runner, new ManualTimeProvider(Now)) with { GetEnvironmentVariable = Defaults, ReadFile = Files(file) }, profileName);

    private static ProfileCredentialsProvider Create(Func<string, string?> environment, string? profileName = null) =>
        new(environment, NoFiles, profileName);

    private static ProfileCredentialsProvider Create(Func<string, string?> environment, (string Path, string Content) file, string? profileName = null) =>
        new(environment, Files(file), profileName);

    private static ProfileCredentialsProvider Create(Func<string, string?> environment, (string Path, string Content) first, (string Path, string Content) second) =>
        new(environment, Files(first, second), null);
}
