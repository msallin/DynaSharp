using System.Net;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// Pins the wire format of <see cref="WebIdentityCredentialsProvider"/> to botocore. The expected values are what botocore's own client sent
/// and parsed for the same input with the query protocol (see <c>generate_sts_vectors.py</c>), not what this library does.
/// </summary>
public sealed class StsVectorTests
{
    private const string TokenPath = "/token";
    private const string RoleArn = "arn:aws:iam::123456789012:role/orders";
    private const string SessionName = "dynasharp-1793000000";

    // Earlier than every expiration of the vectors, so that the clock of the test never makes credentials "already expired".
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<StsVector> Requests() => StsVectors.Rows("requests");

    public static IEnumerable<StsVector> Endpoints() => StsVectors.Rows("endpoints");

    public static IEnumerable<StsVector> ConfiguredEndpoints() => StsVectors.Rows("configured_endpoints");

    public static IEnumerable<StsVector> Responses() => StsVectors.Rows("responses");

    public static IEnumerable<StsVector> Errors() => StsVectors.Rows("errors");

    public static IEnumerable<StsVector> SessionNames() => StsVectors.Rows("session_names");

    [Test]
    [MethodDataSource(nameof(Requests))]
    public async Task GetCredentialsAsync_BotocoreRequest_SendsTheSameMethodContentTypeAndBody(StsVector vector)
    {
        // Arrange
        using var handler = Answering(Success());
        var provider = Create(handler, vector.Text("role_arn"), vector.Text("role_session_name"), vector.Text("token"));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Single();
        await Assert.That((request.Method.Method, request.Headers["Content-Type"], request.BodyText))
            .IsEqualTo((vector.Text("method"), vector.Text("content_type"), vector.Text("body")));
    }

    [Test]
    [MethodDataSource(nameof(Requests))]
    public async Task GetCredentialsAsync_BotocoreRequest_IsNotSignedEither(StsVector vector)
    {
        // Arrange
        using var handler = Answering(Success());
        var provider = Create(handler, vector.Text("role_arn"), vector.Text("role_session_name"), vector.Text("token"));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var signed = handler.Requests.Single().Headers.Keys.Any(static name => string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase));
        await Assert.That(signed).IsEqualTo(vector.Entry.GetProperty("signed").GetBoolean());
    }

    [Test]
    [MethodDataSource(nameof(Endpoints))]
    public async Task GetCredentialsAsync_RegionAndEndpointMode_PostsToTheUrlOfBotocore(StsVector vector)
    {
        // Arrange
        using var handler = Answering(Success());
        var mode = vector.Entry.GetProperty("mode").GetString();
        var provider = new WebIdentityCredentialsProvider(
            Env(("AWS_STS_REGIONAL_ENDPOINTS", mode ?? string.Empty), ("AWS_WEB_IDENTITY_TOKEN_FILE", TokenPath), ("AWS_ROLE_ARN", RoleArn)),
            Files((TokenPath, "token")),
            handler,
            new ManualTimeProvider(Now),
            NullLogger.Instance,
            AwsRegion.FromName(vector.Text("region")));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo(vector.Text("url"));
    }

    [Test]
    [MethodDataSource(nameof(ConfiguredEndpoints))]
    public async Task GetCredentialsAsync_ConfiguredEndpoint_PostsToTheUrlOfBotocore(StsVector vector)
    {
        // Arrange
        using var handler = Answering(Success());
        var variables = vector.Entry.GetProperty("environment").EnumerateObject().Select(static variable => (variable.Name, variable.Value.GetString()!));
        var region = vector.Entry.GetProperty("region").GetString();
        var provider = new WebIdentityCredentialsProvider(
            Env([.. variables, ("AWS_WEB_IDENTITY_TOKEN_FILE", TokenPath), ("AWS_ROLE_ARN", RoleArn)]),
            Files((TokenPath, "token")),
            handler,
            new ManualTimeProvider(Now),
            NullLogger.Instance,
            region is null ? null : AwsRegion.FromName(region));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo(vector.Text("url"));
    }

    [Test]
    [MethodDataSource(nameof(Responses))]
    public async Task TryParse_BotocoreResponse_ReadsTheFieldsThatBotocoreReads(StsVector vector)
    {
        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(vector.Text("body")), out var document);

        // Assert
        var credentials = document.Credentials;
        await Assert.That((parsed, credentials.AccessKeyId, credentials.SecretAccessKey, credentials.Token, document.ErrorCode))
            .IsEqualTo((true, vector.Text("access_key_id"), vector.Text("secret_access_key"), vector.Text("session_token"), (string?)null));
    }

    [Test]
    [MethodDataSource(nameof(Responses))]
    public async Task GetCredentialsAsync_BotocoreResponse_ExpiresWhenBotocoreSays(StsVector vector)
    {
        // Arrange
        using var handler = Answering(new StubResponse { Body = vector.Text("body") });
        var provider = Create(handler, RoleArn, SessionName, "token");

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(DateTimeOffset.Parse(vector.Text("expiration"), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    [MethodDataSource(nameof(Errors))]
    public async Task TryParse_BotocoreError_ReadsTheCodeThatBotocoreReads(StsVector vector)
    {
        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(vector.Text("body")), out var document);

        // Assert
        await Assert.That((parsed, document.ErrorCode)).IsEqualTo((true, vector.Text("code")));
    }

    [Test]
    [MethodDataSource(nameof(Errors))]
    public async Task GetCredentialsAsync_BotocoreError_RetriesWhatBotocoreRetries(StsVector vector)
    {
        // Arrange (an expired token is the one case of its own: botocore gives up, and this provider reads the file again)
        var retryable = vector.Entry.GetProperty("retryable").GetBoolean();
        var expected = retryable ? 3 : vector.Text("code") == "ExpiredTokenException" ? 2 : 1;
        var time = new ManualTimeProvider(Now);
        using var handler = Answering(new StubResponse { StatusCode = (HttpStatusCode)vector.Entry.GetProperty("status").GetInt32(), Body = vector.Text("body") });
        var provider = Create(handler, RoleArn, SessionName, "token", time);

        // Act
        var failing = TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));
        if (retryable)
        {
            await WaitForTimersAsync(time, 2);
            time.Advance(TimeSpan.FromSeconds(1));
            await WaitForTimersAsync(time, 4);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        await failing;

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(expected);
    }

    [Test]
    public async Task ModeledErrors_EveryCodeOfTheModel_HasAVector()
    {
        // Arrange
        var codes = StsVectors.Section("errors").Select(static error => error.GetProperty("code").GetString()).ToHashSet();

        // Act
        var missing = StsVectors.ModeledErrors.Where(code => !codes.Contains(code));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(SessionNames))]
    public async Task Constructor_SessionNameOfTheModel_AcceptsWhatTheModelAccepts(StsVector vector)
    {
        // Arrange
        var region = AwsRegion.FromName("eu-west-1");

        // Act
        var accepted = true;
        try
        {
            _ = new WebIdentityCredentialsProvider(TokenPath, RoleArn, region, vector.Text("name"));
        }
        catch (ArgumentException)
        {
            accepted = false;
        }

        // Assert
        await Assert.That(accepted).IsEqualTo(vector.Entry.GetProperty("valid").GetBoolean());
    }

    private static StubResponse Success() => new()
    {
        Body = $"""<AssumeRoleWithWebIdentityResponse><AssumeRoleWithWebIdentityResult><Credentials><AccessKeyId>{AccessKeyId}</AccessKeyId><SecretAccessKey>{SecretAccessKey}</SecretAccessKey><SessionToken>{SessionToken}</SessionToken><Expiration>2026-10-31T18:00:00Z</Expiration></Credentials></AssumeRoleWithWebIdentityResult></AssumeRoleWithWebIdentityResponse>""",
    };

    private static StubHandler Answering(StubResponse response) => new(_ => response);

    private static WebIdentityCredentialsProvider Create(StubHandler handler, string roleArn, string sessionName, string token, ManualTimeProvider? time = null) => new(
        Env(("AWS_WEB_IDENTITY_TOKEN_FILE", TokenPath), ("AWS_ROLE_ARN", roleArn), ("AWS_ROLE_SESSION_NAME", sessionName)),
        Files((TokenPath, token)),
        handler,
        time ?? new ManualTimeProvider(Now),
        NullLogger.Instance,
        AwsRegion.FromName("eu-west-1"));
}
