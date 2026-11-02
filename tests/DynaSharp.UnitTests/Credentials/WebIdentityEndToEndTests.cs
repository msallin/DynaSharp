using System.Globalization;
using System.Net;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Generators;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// The way <see cref="WebIdentityCredentialsProvider"/> goes when it succeeds, over a real socket: <c>AWS_ENDPOINT_URL_STS</c> leads to an STS on this machine, the shared HTTP client of the credential endpoints
/// sends what botocore sends, and a DynamoDB request is signed with what comes back. Everything that crosses the socket comes from <c>sts_vectors.json</c>, which botocore wrote, so the serializer of this
/// library is not what the test compares with.
/// </summary>
public sealed class WebIdentityEndToEndTests
{
    private const string TokenFile = "/var/run/secrets/token";

    // Earlier than every expiration of the vectors, so that the credentials are good when they arrive.
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly AwsRegion Region = AwsRegion.FromName("eu-west-1");

    private static StsVector Sample => Response("DocumentationSample");

    private static StsVector Refreshed => Response("Refreshed");

    private static StsVector Request => StsVectors.Rows("requests").Single(static vector => vector.Text("name") == "IrsaJwt");

    [Test]
    public async Task GetCredentialsAsync_StsOnLoopback_ReturnsWhatBotocoreParsed()
    {
        // Arrange
        using var sts = new LoopbackHttpServer(Sample.Text("body"));
        var provider = Provider(sts.Endpoint, new ManualTimeProvider(Now));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo((Sample.Text("access_key_id"), Sample.Text("secret_access_key"), Sample.Text("session_token"), ExpirationOf(Sample)));
    }

    [Test]
    public async Task GetCredentialsAsync_StsOnLoopback_SendsWhatBotocoreSends()
    {
        // Arrange
        using var sts = new LoopbackHttpServer(Sample.Text("body"));
        var provider = Provider(sts.Endpoint, new ManualTimeProvider(Now));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = sts.Requests.Single();
        await Assert.That((request.RequestLine, request.Headers.Contains($"Content-Type: {Request.Text("content_type")}"), request.Body)).IsEqualTo(("POST / HTTP/1.1", true, Request.Text("body")));
    }

    [Test]
    public async Task GetCredentialsAsync_CredentialsAboutToExpire_FetchesTheNextOnes()
    {
        // Arrange (ten minutes before the expiration a caller waits for the fetch, so nothing runs in the background here)
        using var sts = new LoopbackHttpServer(Sample.Text("body"), Refreshed.Text("body"));
        var time = new ManualTimeProvider(Now);
        var provider = Provider(sts.Endpoint, time);
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(ExpirationOf(Sample) - TimeSpan.FromMinutes(9) - Now);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SessionToken, credentials.Expiration, sts.Requests.Count))
            .IsEqualTo((Refreshed.Text("access_key_id"), Refreshed.Text("session_token"), ExpirationOf(Refreshed), 2));
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task SendAsync_EnvironmentSelectsWebIdentity_SignsWithTheSessionTokenOfEachFetch()
    {
        // Arrange (the variables of the process, the default chain and a file on disk, as a pod has them. The legacy mode would pick the global endpoint if the variable did not come first)
        using var sts = new LoopbackHttpServer(Sample.Text("body"), Refreshed.Text("body"));
        using var directory = new TempDirectory();
        directory.Write("token", Request.Text("token"));
        using var environment = new ScopedEnvironmentVariables(
            ("AWS_ENDPOINT_URL_STS", sts.Endpoint.AbsoluteUri),
            ("AWS_ENDPOINT_URL", null),
            ("AWS_STS_REGIONAL_ENDPOINTS", "legacy"),
            ("AWS_WEB_IDENTITY_TOKEN_FILE", directory.Combine("token")),
            ("AWS_ROLE_ARN", Request.Text("role_arn")),
            ("AWS_ROLE_SESSION_NAME", null),
            ("AWS_ACCESS_KEY_ID", null),
            ("AWS_SECRET_ACCESS_KEY", null),
            ("AWS_SESSION_TOKEN", null));
        using var dynamoDb = new StubHandler(static _ => new StubResponse { Body = "{}" });
        var time = new ManualTimeProvider(Now);
        using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = Region, HttpHandler = dynamoDb, TimeProvider = time, MaxAttempts = 1 });
        var table = client.Table("Shop", "pk");

        // Act
        await table.Get("a").ItemAsync();
        time.Advance(ExpirationOf(Sample) - TimeSpan.FromMinutes(9) - Now);
        await table.Get("a").ItemAsync();

        // Assert
        var signed = dynamoDb.Requests.Select(static request => (request.Headers["X-Amz-Security-Token"], request.Headers["Authorization"].Split('/')[0].Split('=')[1])).ToList();
        await Assert.That(signed).IsEquivalentTo([(Sample.Text("session_token"), Sample.Text("access_key_id")), (Refreshed.Text("session_token"), Refreshed.Text("access_key_id"))]);
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task GetCredentialsAsync_DefaultProxyOfTheProcess_IsNotUsedForLoopback()
    {
        // Arrange (a client reads the default proxy at its first request, and HTTP_PROXY answers for loopback too, so what stands here is what that variable leaves)
        using var proxy = new LoopbackHttpServer();
        using var sts = new LoopbackHttpServer(Sample.Text("body"));
        var provider = Provider(sts.Endpoint, new ManualTimeProvider(Now));
        var previous = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = new EveryRequestProxy(proxy.Endpoint);

        // Act
        try
        {
            await provider.GetCredentialsAsync(CancellationToken.None);
        }
        finally
        {
            HttpClient.DefaultProxy = previous;
        }

        // Assert
        await Assert.That((proxy.Requests.Count, sts.Requests.Count)).IsEqualTo((0, 1));
    }

    private static StsVector Response(string name) => StsVectors.Rows("responses").Single(vector => vector.Text("name") == name);

    private static DateTimeOffset ExpirationOf(StsVector vector) => DateTimeOffset.Parse(vector.Text("expiration"), CultureInfo.InvariantCulture);

    // No handler, so that it sends over the client of the credential endpoints.
    private static WebIdentityCredentialsProvider Provider(Uri sts, ManualTimeProvider time) => new(
        Env(
            ("AWS_ENDPOINT_URL_STS", sts.AbsoluteUri),
            ("AWS_WEB_IDENTITY_TOKEN_FILE", TokenFile),
            ("AWS_ROLE_ARN", Request.Text("role_arn")),
            ("AWS_ROLE_SESSION_NAME", Request.Text("role_session_name"))),
        Files((TokenFile, Request.Text("token"))),
        null,
        time,
        NullLogger.Instance,
        Region);

    /// <summary>A proxy for every request, as <c>HTTP_PROXY</c> without <c>NO_PROXY</c> makes it. <see cref="WebProxy"/> would not say so for a local address.</summary>
    /// <param name="address">The proxy.</param>
    private sealed class EveryRequestProxy(Uri address) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => address;

        public bool IsBypassed(Uri host) => false;
    }
}
