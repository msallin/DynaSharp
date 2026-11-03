using System.Net;
using System.Net.Sockets;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Signing;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Signing;

public sealed class SigV4SignerTests
{
    public static IEnumerable<string> VectorNames() => SigV4Vectors.All.Select(vector => vector.Name);

    public static IEnumerable<string> VectorNamesWithSessionToken() =>
        SigV4Vectors.All.Where(vector => vector.SessionToken is not null).Select(vector => vector.Name);

    [Test]
    [MethodDataSource(nameof(VectorNames))]
    public async Task Sign_BotocoreVector_SetsAuthorizationHeaderLikeBotocore(string name)
    {
        // Arrange
        var vector = SigV4Vectors.Get(name);
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(HeaderValue(request, "Authorization")).IsEqualTo(vector.ExpectedAuthorization);
    }

    [Test]
    [MethodDataSource(nameof(VectorNames))]
    public async Task Sign_BotocoreVector_SetsAmzDateHeaderLikeBotocore(string name)
    {
        // Arrange
        var vector = SigV4Vectors.Get(name);
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(HeaderValue(request, "X-Amz-Date")).IsEqualTo(vector.ExpectedAmzDate);
    }

    [Test]
    [MethodDataSource(nameof(VectorNamesWithSessionToken))]
    public async Task Sign_CredentialsWithSessionToken_SetsSecurityTokenHeader(string name)
    {
        // Arrange
        var vector = SigV4Vectors.Get(name);
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(HeaderValue(request, "X-Amz-Security-Token")).IsEqualTo(vector.SessionToken);
    }

    [Test]
    public async Task Sign_LongTermCredentials_DoesNotSetSecurityTokenHeader()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(request.Headers.Contains("X-Amz-Security-Token")).IsFalse();
    }

    [Test]
    public async Task Sign_TimeWithOffset_SignsTheUtcInstant()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();
        var timeWithOffset = vector.Time.ToOffset(TimeSpan.FromHours(2));

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), timeWithOffset);

        // Assert
        await Assert.That(HeaderValue(request, "Authorization")).IsEqualTo(vector.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_ServiceOtherThanDynamoDb_UsesServiceInCredentialScope()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region, "dax");
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(HeaderValue(request, "Authorization")).Contains("Credential=AKIDEXAMPLE/20261031/eu-central-1/dax/aws4_request,");
    }

    [Test]
    public async Task Sign_SameCredentialsLaterOnTheSameDay_SignsLikeBotocore()
    {
        // Arrange
        var first = SigV4Vectors.Get("LongTermKeys");
        var later = SigV4Vectors.Get("LongTermKeysLaterSameDay");
        var signer = new SigV4Signer(first.Region);
        var credentials = first.CreateCredentials();
        using var firstRequest = first.CreateRequest();
        using var laterRequest = later.CreateRequest();
        signer.Sign(firstRequest, first.PayloadSha256, credentials, first.Time);

        // Act
        signer.Sign(laterRequest, later.PayloadSha256, credentials, later.Time);

        // Assert
        await Assert.That(HeaderValue(laterRequest, "Authorization")).IsEqualTo(later.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_SameCredentialsAndDate_AllocatesLessThanTheCallThatDerivesTheKey()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var payload = vector.PayloadSha256;
        var time = vector.Time;
        const int Repetitions = 5;

        // Act
        var (derivingBytes, cachedBytes) = LeastAllocatedBytesBySign(vector, payload, time, Repetitions);

        // Assert
        await Assert.That(cachedBytes).IsLessThan(derivingBytes);
    }

    [Test]
    public async Task Sign_NextDayWithSameCredentials_DoesNotReuseTheKeyOfThePreviousDay()
    {
        // Arrange
        var before = SigV4Vectors.Get("DateRolloverBefore");
        var after = SigV4Vectors.Get("DateRolloverAfter");
        var signer = new SigV4Signer(before.Region);
        var credentials = before.CreateCredentials();
        using var beforeRequest = before.CreateRequest();
        using var afterRequest = after.CreateRequest();
        signer.Sign(beforeRequest, before.PayloadSha256, credentials, before.Time);

        // Act
        signer.Sign(afterRequest, after.PayloadSha256, credentials, after.Time);

        // Assert
        await Assert.That(HeaderValue(afterRequest, "Authorization")).IsEqualTo(after.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_PreviousDayAfterNextDay_DerivesTheKeyOfThePreviousDayAgain()
    {
        // Arrange
        var before = SigV4Vectors.Get("DateRolloverBefore");
        var after = SigV4Vectors.Get("DateRolloverAfter");
        var signer = new SigV4Signer(before.Region);
        var credentials = before.CreateCredentials();
        using var afterRequest = after.CreateRequest();
        using var beforeRequest = before.CreateRequest();
        signer.Sign(afterRequest, after.PayloadSha256, credentials, after.Time);

        // Act
        signer.Sign(beforeRequest, before.PayloadSha256, credentials, before.Time);

        // Assert
        await Assert.That(HeaderValue(beforeRequest, "Authorization")).IsEqualTo(before.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_OtherCredentialsOnTheSameDay_DoesNotReuseTheKeyOfTheFirstCredentials()
    {
        // Arrange
        var first = SigV4Vectors.Get("LongTermKeys");
        var other = SigV4Vectors.Get("OtherCredentials");
        var signer = new SigV4Signer(first.Region);
        using var firstRequest = first.CreateRequest();
        using var otherRequest = other.CreateRequest();
        signer.Sign(firstRequest, first.PayloadSha256, first.CreateCredentials(), first.Time);

        // Act
        signer.Sign(otherRequest, other.PayloadSha256, other.CreateCredentials(), other.Time);

        // Assert
        await Assert.That(HeaderValue(otherRequest, "Authorization")).IsEqualTo(other.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_FirstCredentialsAgainAfterOtherCredentials_SignsLikeBotocore()
    {
        // Arrange
        var first = SigV4Vectors.Get("LongTermKeys");
        var other = SigV4Vectors.Get("OtherCredentials");
        var signer = new SigV4Signer(first.Region);
        var firstCredentials = first.CreateCredentials();
        using var otherRequest = other.CreateRequest();
        using var firstRequest = first.CreateRequest();
        signer.Sign(otherRequest, other.PayloadSha256, other.CreateCredentials(), other.Time);

        // Act
        signer.Sign(firstRequest, first.PayloadSha256, firstCredentials, first.Time);

        // Assert
        await Assert.That(HeaderValue(firstRequest, "Authorization")).IsEqualTo(first.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_NewInstanceWithTheSameKeysOnTheSameDay_SignsLikeBotocore()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var firstRequest = vector.CreateRequest();
        using var secondRequest = vector.CreateRequest();
        signer.Sign(firstRequest, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Act
        signer.Sign(secondRequest, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(HeaderValue(secondRequest, "Authorization")).IsEqualTo(vector.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_ConcurrentCallsAcrossDates_AllSignLikeBotocore()
    {
        // Arrange
        var before = SigV4Vectors.Get("DateRolloverBefore");
        var after = SigV4Vectors.Get("DateRolloverAfter");
        var signer = new SigV4Signer(before.Region);
        var credentials = before.CreateCredentials();
        var mismatches = 0;

        // Act
        Parallel.For(0, 400, index =>
        {
            var vector = index % 2 == 0 ? before : after;
            using var request = vector.CreateRequest();
            signer.Sign(request, vector.PayloadSha256, credentials, vector.Time);
            if (HeaderValue(request, "Authorization") != vector.ExpectedAuthorization)
            {
                Interlocked.Increment(ref mismatches);
            }
        });

        // Assert
        await Assert.That(mismatches).IsEqualTo(0);
    }

    [Test]
    public async Task Sign_TwoCallsInTheSameSecond_ShareTheDateText()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var first = vector.CreateRequest();
        using var second = vector.CreateRequest();
        signer.Sign(first, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Act
        signer.Sign(second, vector.PayloadSha256, vector.CreateCredentials(), vector.Time.AddMilliseconds(999));

        // Assert
        await Assert.That(ReferenceEquals(HeaderValue(first, "X-Amz-Date"), HeaderValue(second, "X-Amz-Date"))).IsTrue();
    }

    [Test]
    public async Task Sign_TimeLaterInTheSameSecond_SignsLikeBotocore()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var first = vector.CreateRequest();
        using var second = vector.CreateRequest();
        signer.Sign(first, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Act
        signer.Sign(second, vector.PayloadSha256, vector.CreateCredentials(), vector.Time.AddMilliseconds(999));

        // Assert
        await Assert.That(HeaderValue(second, "Authorization")).IsEqualTo(vector.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_TimeOneSecondLater_UsesTheNewDateText()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var first = vector.CreateRequest();
        using var second = vector.CreateRequest();
        signer.Sign(first, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Act
        signer.Sign(second, vector.PayloadSha256, vector.CreateCredentials(), vector.Time.AddSeconds(1));

        // Assert
        await Assert.That(HeaderValue(second, "X-Amz-Date")).IsEqualTo("20261031T123457Z");
    }

    [Test]
    public async Task Sign_RequestThatFitsTheStackBuffer_DoesNotUseThePool()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var pool = new CountingArrayPool<byte>(surplus: 64);
        var signer = new SigV4Signer(vector.Region, pool: pool);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Sign_RequestThatNeedsExactlyTheStackBufferLimit_DoesNotUseThePool()
    {
        // Arrange
        // Every character of the session token adds one byte to the request. A probe above the limit tells how
        // large a token makes the request 1024 bytes, the size of the stack buffer.
        const int ProbeTokenLength = 1000;
        const int StackBufferLength = 1024;
        var probePool = new CountingArrayPool<byte>(surplus: 64);
        var probe = SigV4Vectors.Get("LongTermKeys") with { SessionToken = new string('T', ProbeTokenLength) };
        using var probeRequest = probe.CreateRequest();
        new SigV4Signer(probe.Region, pool: probePool).Sign(probeRequest, probe.PayloadSha256, probe.CreateCredentials(), probe.Time);
        var tokenLength = ProbeTokenLength - (probePool.RentedLengths.Single() - StackBufferLength);
        var vector = probe with { SessionToken = new string('T', tokenLength) };
        var pool = new CountingArrayPool<byte>(surplus: 64);
        var signer = new SigV4Signer(vector.Region, pool: pool);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Sign_LargeRequest_RentsOneBufferFromThePool()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongSessionToken");
        var pool = new CountingArrayPool<byte>(surplus: 64);
        var signer = new SigV4Signer(vector.Region, pool: pool);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Sign_LargeRequest_ReturnsTheRentedBufferToThePool()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongSessionToken");
        var pool = new CountingArrayPool<byte>(surplus: 64);
        var signer = new SigV4Signer(vector.Region, pool: pool);
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task Sign_LargeRequestWithOversizedPooledBuffer_SignsLikeBotocore()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongSessionToken");
        var signer = new SigV4Signer(vector.Region, pool: new CountingArrayPool<byte>(surplus: 64));
        using var request = vector.CreateRequest();

        // Act
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);

        // Assert
        await Assert.That(HeaderValue(request, "Authorization")).IsEqualTo(vector.ExpectedAuthorization);
    }

    [Test]
    public async Task Sign_RequestSentThroughHttpClient_HeadersOnTheWireAreTheSignedValues()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = new LoopbackServer();
        var vector = SigV4Vectors.Get("SessionToken") with { Url = $"http://127.0.0.1:{server.Port}/" };
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();
        signer.Sign(request, vector.PayloadSha256, vector.CreateCredentials(), vector.Time);
        var signedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = $"127.0.0.1:{server.Port}",
            ["Content-Type"] = "application/x-amz-json-1.0",
            ["X-Amz-Target"] = vector.Target,
            ["X-Amz-Date"] = vector.ExpectedAmzDate,
            ["X-Amz-Security-Token"] = vector.SessionToken!,
            ["Authorization"] = HeaderValue(request, "Authorization"),
        };
        var received = server.ReceiveRequestHeadersAsync(cancellation.Token);
        using var client = new HttpClient();

        // Act
        using var response = await client.SendAsync(request, cancellation.Token);
        var wireHeaders = await received;

        // Assert
        await Assert.That(wireHeaders.Where(header => signedHeaders.ContainsKey(header.Key)).ToDictionary(StringComparer.OrdinalIgnoreCase)).IsEquivalentTo(signedHeaders);
    }

    [Test]
    [Arguments("http://localhost:8000", "localhost:8000")]
    [Arguments("http://localhost:80/", "localhost")]
    [Arguments("https://dynamodb.eu-west-1.amazonaws.com/", "dynamodb.eu-west-1.amazonaws.com")]
    [Arguments("https://Example.COM:443", "example.com")]
    [Arguments("http://127.0.0.1:8000", "127.0.0.1:8000")]
    [Arguments("http://[::1]:8000", "[::1]:8000")]
    [Arguments("http://[::1]/", "[::1]")]
    [Arguments("http://b\u00fccher.example:8000/", "xn--bcher-kva.example:8000")]
    [Arguments("https://b\u00fccher.example/", "xn--bcher-kva.example")]
    public async Task GetAuthority_Endpoint_IsTheHostWithThePortUnlessItIsTheDefaultOne(string endpoint, string expected)
    {
        // Arrange
        var uri = new Uri(endpoint);

        // Act
        var authority = SigV4Signer.GetAuthority(uri);

        // Assert
        await Assert.That(authority).IsEqualTo(expected);
    }

    [Test]
    public async Task GetAuthority_InternationalizedHost_IsWhatHttpClientSendsAsHost()
    {
        // Arrange (the name does not resolve, so the connection goes to the loopback server)
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = new LoopbackServer();
        var endpoint = new Uri($"http://b\u00fccher.example:{server.Port}/");
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Loopback, server.Port, token);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var received = server.ReceiveRequestHeadersAsync(cancellation.Token);

        // Act
        using var response = await client.SendAsync(request, cancellation.Token);
        var wireHeaders = await received;

        // Assert
        await Assert.That(wireHeaders["Host"]).IsEqualTo(SigV4Signer.GetAuthority(endpoint));
    }

    [Test]
    public async Task Sign_PayloadHashWithWrongLength_ThrowsArgumentException()
    {
        // Arrange
        var vector = SigV4Vectors.Get("LongTermKeys");
        var signer = new SigV4Signer(vector.Region);
        using var request = vector.CreateRequest();
        var credentials = vector.CreateCredentials();
        var shortHash = new byte[31];

        // Act
        var act = () => signer.Sign(request, shortHash, credentials, vector.Time);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithMessageContaining("SHA-256", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Constructor_RegionNullOrEmpty_ThrowsArgumentException(string? region)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => new SigV4Signer(region!);

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Constructor_ServiceNullOrEmpty_ThrowsArgumentException(string? service)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => new SigV4Signer("eu-central-1", service!);

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }

    private static string HeaderValue(HttpRequestMessage request, string name) => request.Headers.NonValidated[name].ToString();

    // Signs with fresh signers and credentials to get the calls that derive the signing key, then repeatedly with
    // the same ones to get the calls that use the cached key. The least allocation of each kind is returned: a
    // one-off allocation such as lazy initialization only affects single calls, not the minimum.
    //
    // Must stay synchronous: the counter belongs to the thread, and an await could continue on another one.
    private static (long Deriving, long Cached) LeastAllocatedBytesBySign(SigV4Vector vector, byte[] payload, DateTimeOffset time, int repetitions)
    {
        var deriving = long.MaxValue;
        var cached = long.MaxValue;
        for (var i = 0; i < repetitions; i++)
        {
            var signer = new SigV4Signer(vector.Region);
            var credentials = vector.CreateCredentials();
            using var first = vector.CreateRequest();
            using var second = vector.CreateRequest();
            deriving = Math.Min(deriving, AllocatedBytesBySign(signer, first, payload, credentials, time));
            cached = Math.Min(cached, AllocatedBytesBySign(signer, second, payload, credentials, time));
        }

        return (deriving, cached);
    }

    private static long AllocatedBytesBySign(SigV4Signer signer, HttpRequestMessage request, byte[] payload, AwsCredentials credentials, DateTimeOffset time)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        signer.Sign(request, payload, credentials, time);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // Accepts one connection, returns the request headers as received and answers with an empty 200.
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public LoopbackServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public async Task<Dictionary<string, string>> ReceiveRequestHeadersAsync(CancellationToken cancellationToken)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            var stream = client.GetStream();
            var received = new StringBuilder();
            var buffer = new byte[4096];
            while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), cancellationToken);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in received.ToString().Split("\r\n").Skip(1).TakeWhile(line => line.Length > 0))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                headers[line[..separator]] = line[(separator + 1)..].Trim();
            }

            return headers;
        }

        public void Dispose() => _listener.Dispose();
    }
}
