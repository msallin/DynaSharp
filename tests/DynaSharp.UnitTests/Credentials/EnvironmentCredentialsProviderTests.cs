using DynaSharp.Credentials;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.UnitTests.Credentials;

public sealed class EnvironmentCredentialsProviderTests
{
    private const string AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private const string SessionToken = "IQoJb3JpZ2luX2VjEXAMPLETOKEN";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    [Test]
    public async Task GetCredentialsAsync_AllVariablesSet_ReturnsAccessKeyId()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_SESSION_TOKEN", SessionToken));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_AllVariablesSet_ReturnsSecretAccessKey()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_SESSION_TOKEN", SessionToken));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SecretAccessKey).IsEqualTo(SecretAccessKey);
    }

    [Test]
    public async Task GetCredentialsAsync_AllVariablesSet_ReturnsSessionToken()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_SESSION_TOKEN", SessionToken));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SessionToken).IsEqualTo(SessionToken);
    }

    [Test]
    public async Task GetCredentialsAsync_NoSessionToken_SessionTokenIsNull()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SessionToken).IsNull();
    }

    [Test]
    public async Task GetCredentialsAsync_EmptySessionToken_SessionTokenIsNull()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_SESSION_TOKEN", string.Empty));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.SessionToken).IsNull();
    }

    [Test]
    public async Task GetCredentialsAsync_Credentials_NeverExpire()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_SESSION_TOKEN", SessionToken));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsNull();
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwice_ReturnsTheSameInstance()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey));
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwice_ReadsEachVariableOnce()
    {
        // Arrange
        var reads = new List<string>();
        var variables = new Dictionary<string, string>
        {
            ["AWS_ACCESS_KEY_ID"] = AccessKeyId,
            ["AWS_SECRET_ACCESS_KEY"] = SecretAccessKey,
            ["AWS_SESSION_TOKEN"] = SessionToken,
        };
        var provider = new EnvironmentCredentialsProvider(name =>
        {
            reads.Add(name);
            return variables.GetValueOrDefault(name);
        });
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(reads).IsEquivalentTo(["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_CREDENTIAL_EXPIRATION", "AWS_SESSION_TOKEN"]);
    }

    [Test]
    public async Task GetCredentialsAsync_ExpirationSet_CarriesIt()
    {
        // Arrange (what `aws configure export-credentials --format env` writes)
        var time = new ManualTimeProvider(Now);
        var provider = Create(time, ("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_SESSION_TOKEN", SessionToken), ("AWS_CREDENTIAL_EXPIRATION", "2026-10-31T13:34:56Z"));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    [Arguments("2026-10-31T15:34:56+02:00")]
    [Arguments("2026-10-31T13:34:56")]
    [Arguments("2026-10-31T13:34:56.000Z")]
    public async Task GetCredentialsAsync_ExpirationInOtherFormsOfIso8601_IsUtc(string value)
    {
        // Arrange
        var provider = Create(new ManualTimeProvider(Now), ("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_CREDENTIAL_EXPIRATION", value));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task GetCredentialsAsync_ExpirationUnsetOrEmpty_NeverExpiresAndIsNeverReadAgain(string? value)
    {
        // Arrange
        var time = new ManualTimeProvider(Now);
        var reads = 0;
        var provider = new EnvironmentCredentialsProvider(
            name =>
            {
                Interlocked.Increment(ref reads);
                return name switch
                {
                    "AWS_ACCESS_KEY_ID" => AccessKeyId,
                    "AWS_SECRET_ACCESS_KEY" => SecretAccessKey,
                    "AWS_CREDENTIAL_EXPIRATION" => value,
                    _ => null,
                };
            },
            time,
            NullLogger.Instance);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(400));

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((first.Expiration, ReferenceEquals(first, second), reads)).IsEqualTo(((DateTimeOffset?)null, true, 4));
    }

    [Test]
    [Arguments("tomorrow")]
    [Arguments("1700000000")]
    public async Task GetCredentialsAsync_ExpirationIsNoTime_FailsNamingTheVariable(string value)
    {
        // Arrange
        var provider = Create(new ManualTimeProvider(Now), ("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_CREDENTIAL_EXPIRATION", value));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("AWS_CREDENTIAL_EXPIRATION is not an ISO 8601 time, such as 2026-11-03T12:00:00Z.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task GetCredentialsAsync_ExpirationHasPassed_FailsNamingTheVariable(int secondsFromNow)
    {
        // Arrange
        var expiration = Now.AddSeconds(secondsFromNow);
        var provider = Create(new ManualTimeProvider(Now), ("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_CREDENTIAL_EXPIRATION", CredentialsTestSupport.Timestamp(expiration)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"The credentials in the environment expired at {expiration:O}, the time in AWS_CREDENTIAL_EXPIRATION: set new ones, or check the system clock.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_NearTheExpiration_ReadsTheEnvironmentAgain()
    {
        // Arrange (a process that renews its own variables, as a sidecar or a wrapper does)
        var time = new ManualTimeProvider(Now);
        var variables = new Dictionary<string, string>
        {
            ["AWS_ACCESS_KEY_ID"] = "FIRST",
            ["AWS_SECRET_ACCESS_KEY"] = SecretAccessKey,
            ["AWS_CREDENTIAL_EXPIRATION"] = CredentialsTestSupport.Timestamp(Now.AddHours(1)),
        };
        var provider = new EnvironmentCredentialsProvider(
            name =>
            {
                lock (variables)
                {
                    return variables.GetValueOrDefault(name);
                }
            },
            time,
            NullLogger.Instance);
        await provider.GetCredentialsAsync(CancellationToken.None);
        lock (variables)
        {
            variables["AWS_ACCESS_KEY_ID"] = "SECOND";
            variables["AWS_CREDENTIAL_EXPIRATION"] = CredentialsTestSupport.Timestamp(Now.AddHours(2));
        }

        time.Advance(TimeSpan.FromMinutes(51));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.Expiration)).IsEqualTo(("SECOND", (DateTimeOffset?)Now.AddHours(2)));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpiredAndTheEnvironmentWasNotRenewed_FailsNamingTheVariable()
    {
        // Arrange
        var time = new ManualTimeProvider(Now);
        var provider = Create(time, ("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey), ("AWS_CREDENTIAL_EXPIRATION", CredentialsTestSupport.Timestamp(Now.AddHours(1))));
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(2));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("the time in AWS_CREDENTIAL_EXPIRATION", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryInvalidate_CredentialsWithExpiration_ReadsTheEnvironmentAgain()
    {
        // Arrange (the retry of a client after ExpiredTokenException)
        var time = new ManualTimeProvider(Now);
        var reads = 0;
        var provider = new EnvironmentCredentialsProvider(
            name =>
            {
                if (name == "AWS_ACCESS_KEY_ID")
                {
                    Interlocked.Increment(ref reads);
                }

                return name switch
                {
                    "AWS_ACCESS_KEY_ID" => AccessKeyId,
                    "AWS_SECRET_ACCESS_KEY" => SecretAccessKey,
                    "AWS_CREDENTIAL_EXPIRATION" => CredentialsTestSupport.Timestamp(Now.AddHours(1)),
                    _ => null,
                };
            },
            time,
            NullLogger.Instance);
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var next = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, ReferenceEquals(rejected, next), reads)).IsEqualTo((true, false, 2));
    }

    [Test]
    public async Task TryInvalidate_CredentialsWithoutExpiration_ReturnsFalseAndKeepsThem()
    {
        // Arrange (as before AWS_CREDENTIAL_EXPIRATION: the environment holds the keys once and for all)
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey));
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var next = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, ReferenceEquals(rejected, next))).IsEqualTo((false, true));
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    [Test]
    public async Task GetCredentialsAsync_VariablesChangeAfterFirstCall_KeepsOriginalCredentials()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["AWS_ACCESS_KEY_ID"] = AccessKeyId,
            ["AWS_SECRET_ACCESS_KEY"] = SecretAccessKey,
        };
        var provider = new EnvironmentCredentialsProvider(variables.GetValueOrDefault);
        await provider.GetCredentialsAsync(CancellationToken.None);
        variables["AWS_ACCESS_KEY_ID"] = "AKIAOTHEREXAMPLE";

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task Constructor_Accessor_DoesNotReadEnvironment()
    {
        // Arrange
        var reads = 0;

        // Act
        _ = new EnvironmentCredentialsProvider(_ =>
        {
            reads++;
            return null;
        });

        // Assert
        await Assert.That(reads).IsEqualTo(0);
    }

    [Test]
    public async Task GetCredentialsAsync_ConcurrentFirstCalls_AllReceiveTheSameInstance()
    {
        // Arrange
        var provider = CreateProvider(("AWS_ACCESS_KEY_ID", AccessKeyId), ("AWS_SECRET_ACCESS_KEY", SecretAccessKey));
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
    [Arguments(null, null)]
    [Arguments("", "")]
    [Arguments(null, SecretAccessKey)]
    [Arguments("", SecretAccessKey)]
    [Arguments(AccessKeyId, null)]
    [Arguments(AccessKeyId, "")]
    public async Task GetCredentialsAsync_MissingOrEmptyKeyOrSecret_ThrowsCredentialsUnavailableException(string? accessKeyId, string? secretAccessKey)
    {
        // Arrange
        var variables = new Dictionary<string, string?>
        {
            ["AWS_ACCESS_KEY_ID"] = accessKeyId,
            ["AWS_SECRET_ACCESS_KEY"] = secretAccessKey,
            ["AWS_SESSION_TOKEN"] = SessionToken,
        };
        var provider = new EnvironmentCredentialsProvider(variables.GetValueOrDefault);

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    [Arguments("AWS_ACCESS_KEY_ID")]
    [Arguments("AWS_SECRET_ACCESS_KEY")]
    [Arguments("AWS_SESSION_TOKEN")]
    public async Task GetCredentialsAsync_NothingSet_ExceptionMessageNamesVariable(string variable)
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining(variable, StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_FailureThenVariablesSet_ReadsAgainAndSucceeds()
    {
        // Arrange
        var variables = new Dictionary<string, string>();
        var provider = new EnvironmentCredentialsProvider(variables.GetValueOrDefault);
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();
        variables["AWS_ACCESS_KEY_ID"] = AccessKeyId;
        variables["AWS_SECRET_ACCESS_KEY"] = SecretAccessKey;

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_AfterFailure_ReadsEnvironmentAgain()
    {
        // Arrange
        var failures = 0;
        var provider = new EnvironmentCredentialsProvider(_ =>
        {
            failures++;
            return null;
        });
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();
        var readsAfterFirstFailure = failures;

        // Act
        await Assert.That(async () => await provider.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(failures).IsGreaterThan(readsAfterFirstFailure);
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_CreatesProviderForTheProcessEnvironment()
    {
        // Arrange (the process environment is neither read nor modified by constructing the provider)

        // Act
        var provider = new EnvironmentCredentialsProvider();

        // Assert
        await Assert.That(provider).IsAssignableTo<ICredentialsProvider>();
    }

    private static EnvironmentCredentialsProvider Create(TimeProvider time, params (string Name, string Value)[] variables) =>
        new(CredentialsTestSupport.Env(variables), time, NullLogger.Instance);

    private static EnvironmentCredentialsProvider CreateProvider(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value);
        return new EnvironmentCredentialsProvider(map.GetValueOrDefault);
    }
}
