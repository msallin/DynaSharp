using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class EnvironmentCredentialsProviderTests
{
    private const string AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private const string SessionToken = "IQoJb3JpZ2luX2VjEXAMPLETOKEN";

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
        await Assert.That(reads).IsEquivalentTo(["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN"]);
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

    private static EnvironmentCredentialsProvider CreateProvider(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value);
        return new EnvironmentCredentialsProvider(map.GetValueOrDefault);
    }
}
