using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Http;

namespace DynaSharp.Signing;

/// <summary>
/// Signs DynamoDB requests with AWS Signature Version 4, and the forms of the Query protocol that <c>AssumeRole</c> of AWS STS sends
/// (<see cref="SignForm"/>).
/// </summary>
/// <remarks>
/// DynamoDB requests are always <c>POST /</c> with a fixed header set, so the canonical request is written
/// directly as UTF-8 instead of being assembled from a generic header collection. The signed headers are
/// <c>content-type;host;x-amz-date;x-amz-target</c>, plus <c>x-amz-security-token</c> for temporary credentials.
/// <para>
/// The signing key only depends on the secret, the date and the (fixed) region and service. It is derived once per
/// credentials instance and day, and cached in an immutable entry that is replaced atomically, so one instance can
/// be shared by concurrent requests. Rotated credentials are a new instance and therefore never reuse a stale key. The text of
/// the signing time is kept the same way for as long as the clock shows the same second.
/// </para>
/// </remarks>
internal sealed class SigV4Signer
{
    private const string DateHeaderName = "X-Amz-Date";
    private const string SecurityTokenHeaderName = "X-Amz-Security-Token";
    private const string AuthorizationHeaderName = "Authorization";

    // "20261031T123456Z" and its date prefix "20261031".
    private const string AmzDateFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const int AmzDateLength = 16;
    private const int DateStampLength = 8;

    private const int Sha256Length = 32;
    private const int HexSha256Length = 2 * Sha256Length;

    // Requests that need more than this are signed in a pooled buffer instead. Without a session token a request
    // needs about 650 bytes; the session tokens of STS are 1 KB and more.
    private const int MaxStackBufferLength = 1024;

    private readonly ArrayPool<byte> _pool;
    private readonly byte[] _region;
    private readonly byte[] _service;

    // "/{region}/{service}/aws4_request": the credential scope without the leading date.
    private readonly byte[] _scopeSuffix;

    private volatile CachedSigningKey? _cachedKey;
    private volatile CachedSigningTime? _cachedTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="SigV4Signer"/> class.
    /// </summary>
    /// <param name="region">The region name that is part of the credential scope, for example <c>eu-central-1</c>.</param>
    /// <param name="service">The service name that is part of the credential scope. DynamoDB and DynamoDB Streams both use <c>dynamodb</c>.</param>
    /// <param name="pool">The pool for the buffers of requests that do not fit on the stack, or <see langword="null"/> for <see cref="ArrayPool{T}.Shared"/>. Tests pass their own.</param>
    /// <exception cref="ArgumentException"><paramref name="region"/> or <paramref name="service"/> is null or empty.</exception>
    internal SigV4Signer(string region, string service = "dynamodb", ArrayPool<byte>? pool = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(region);
        ArgumentException.ThrowIfNullOrEmpty(service);

        _pool = pool ?? ArrayPool<byte>.Shared;
        _region = Encoding.UTF8.GetBytes(region);
        _service = Encoding.UTF8.GetBytes(service);
        _scopeSuffix = Encoding.UTF8.GetBytes($"/{region}/{service}/aws4_request");
    }

    private static ReadOnlySpan<byte> Algorithm => "AWS4-HMAC-SHA256"u8;

    // Everything of the canonical request up to the value of the host header: method, path, empty query and the
    // two headers that sort before it. The value of content-type is WireProtocol.ContentType, which the pipeline sets as is.
    private static ReadOnlySpan<byte> CanonicalRequestStart => "POST\n/\n\ncontent-type:application/x-amz-json-1.0\nhost:"u8;

    private static ReadOnlySpan<byte> SignedHeaders => "content-type;host;x-amz-date;x-amz-target"u8;

    private static ReadOnlySpan<byte> SignedHeadersWithToken => "content-type;host;x-amz-date;x-amz-security-token;x-amz-target"u8;

    // The same for a form of the Query protocol of AWS STS, which has no X-Amz-Target. The content type is what StringContent renders for
    // "application/x-www-form-urlencoded" in UTF-8.
    private static ReadOnlySpan<byte> FormCanonicalRequestStart => "POST\n/\n\ncontent-type:application/x-www-form-urlencoded; charset=utf-8\nhost:"u8;

    private static ReadOnlySpan<byte> FormSignedHeaders => "content-type;host;x-amz-date"u8;

    private static ReadOnlySpan<byte> FormSignedHeadersWithToken => "content-type;host;x-amz-date;x-amz-security-token"u8;

    /// <summary>
    /// Builds the host to sign for an endpoint: what <see cref="HttpClient"/> sends as <c>Host</c> header, which is the host plus the port when it is not the default one of the scheme.
    /// A name with non-ASCII characters goes out in punycode (<c>xn--bcher-kva.example</c>), whereas <see cref="Uri.Authority"/> keeps the Unicode form, so the signature would not match. An IPv6 address keeps its brackets.
    /// </summary>
    /// <param name="endpoint">An absolute endpoint.</param>
    /// <returns>The host and the port to pass to <see cref="Sign"/> and <see cref="SignForm"/>.</returns>
    internal static string GetAuthority(Uri endpoint)
    {
        // IdnHost has no brackets around an IPv6 address, Host has.
        var host = endpoint.HostNameType == UriHostNameType.Dns ? endpoint.IdnHost : endpoint.Host;
        return endpoint.IsDefaultPort ? host : $"{host}:{endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Adds the <c>X-Amz-Date</c>, <c>X-Amz-Security-Token</c> (temporary credentials only) and <c>Authorization</c> headers to a
    /// DynamoDB request. The request is not checked: the pipeline builds it, and it is exactly what the canonical request below
    /// says: <c>POST /</c> without query, content type <see cref="WireProtocol.ContentType"/> without parameters, and the host and
    /// the <c>X-Amz-Target</c> that are passed in.
    /// </summary>
    /// <param name="request">A request that has not been signed: the headers are added, not replaced.</param>
    /// <param name="authority">
    /// The host plus the port when it is not the default one, which is what <see cref="HttpClient"/> sends as <c>Host</c> header,
    /// as <see cref="GetAuthority"/> returns it. This matters for DynamoDB Local (<c>http://localhost:8000</c>). The caller keeps
    /// it, because <see cref="GetAuthority"/> builds a new string on every call.
    /// </param>
    /// <param name="target">The value of the <c>X-Amz-Target</c> header of the request.</param>
    /// <param name="payloadSha256">
    /// The SHA-256 hash of the request body. The caller computes it once and reuses it for every retry attempt.
    /// </param>
    /// <param name="credentials">The credentials to sign with.</param>
    /// <param name="utcNow">The signing time. Any offset is converted to UTC.</param>
    /// <exception cref="ArgumentException"><paramref name="payloadSha256"/> is not 32 bytes long.</exception>
    internal void Sign(HttpRequestMessage request, string authority, string target, ReadOnlySpan<byte> payloadSha256, AwsCredentials credentials, DateTimeOffset utcNow) =>
        SignCore(request, authority, target, payloadSha256, credentials, utcNow);

    /// <summary>
    /// Adds the same headers as <see cref="Sign"/> to a request of the Query protocol: <c>POST /</c> without query, with the form in the body and the
    /// content type <c>application/x-www-form-urlencoded; charset=utf-8</c>, which is what AWS STS takes. The signed headers are
    /// <c>content-type;host;x-amz-date</c>, plus <c>x-amz-security-token</c> for temporary credentials.
    /// </summary>
    /// <param name="request">A request that has not been signed.</param>
    /// <param name="authority">The host plus the port when it is not the default one, see <see cref="Sign"/>.</param>
    /// <param name="payloadSha256">The SHA-256 hash of the form.</param>
    /// <param name="credentials">The credentials to sign with.</param>
    /// <param name="utcNow">The signing time.</param>
    /// <exception cref="ArgumentException"><paramref name="payloadSha256"/> is not 32 bytes long.</exception>
    internal void SignForm(HttpRequestMessage request, string authority, ReadOnlySpan<byte> payloadSha256, AwsCredentials credentials, DateTimeOffset utcNow) =>
        SignCore(request, authority, null, payloadSha256, credentials, utcNow);

    private static void WriteCanonicalRequest(
        ref Utf8Writer writer,
        string host,
        ReadOnlySpan<byte> amzDate,
        string? token,
        string? target,
        ReadOnlySpan<byte> signedHeaders,
        ReadOnlySpan<byte> payloadSha256)
    {
        writer.Write(target is null ? FormCanonicalRequestStart : CanonicalRequestStart);
        writer.Write(host);
        writer.Write("\nx-amz-date:"u8);
        writer.Write(amzDate);
        if (token is not null)
        {
            writer.Write("\nx-amz-security-token:"u8);
            writer.Write(token);
        }

        if (target is not null)
        {
            writer.Write("\nx-amz-target:"u8);
            writer.Write(target);
        }

        writer.Write("\n\n"u8);
        writer.Write(signedHeaders);
        writer.Write("\n"u8);
        writer.WriteHex(payloadSha256);
    }

    // A request without a target is a form of the Query protocol.
    private void SignCore(HttpRequestMessage request, string authority, string? target, ReadOnlySpan<byte> payloadSha256, AwsCredentials credentials, DateTimeOffset utcNow)
    {
        // The one check that stays: a hash of another length would be written past the size the buffer was computed for.
        if (payloadSha256.Length != Sha256Length)
        {
            throw new ArgumentException($"The payload hash must be a SHA-256 hash of {Sha256Length} bytes.", nameof(payloadSha256));
        }

        var signingTime = GetSigningTime(utcNow);
        var authorization = CreateAuthorization(authority, target, signingTime.Utf8, payloadSha256, credentials);

        // Without validation, so that the Authorization value is not parsed into an AuthenticationHeaderValue.
        // Nothing here can be rejected: the names are request headers and the values are ours (or the token).
        var headers = request.Headers;
        headers.TryAddWithoutValidation(DateHeaderName, signingTime.Text);
        if (credentials.SessionToken is { } token)
        {
            headers.TryAddWithoutValidation(SecurityTokenHeaderName, token);
        }

        headers.TryAddWithoutValidation(AuthorizationHeaderName, authorization);
    }

    private CachedSigningTime GetSigningTime(DateTimeOffset utcNow)
    {
        var second = utcNow.UtcTicks / TimeSpan.TicksPerSecond;
        var cached = _cachedTime;
        if (cached is not null && cached.Second == second)
        {
            return cached;
        }

        Span<byte> amzDate = stackalloc byte[AmzDateLength];
        utcNow.UtcDateTime.TryFormat(amzDate, out _, AmzDateFormat, CultureInfo.InvariantCulture);
        return _cachedTime = new CachedSigningTime(second, amzDate.ToArray());
    }

    private string CreateAuthorization(string host, string? target, ReadOnlySpan<byte> amzDate, ReadOnlySpan<byte> payloadSha256, AwsCredentials credentials)
    {
        var token = credentials.SessionToken;
        var dateStamp = amzDate[..DateStampLength];
        var signingKey = GetSigningKey(credentials, dateStamp);
        var signedHeaders = (target, token) switch
        {
            (null, null) => FormSignedHeaders,
            (null, _) => FormSignedHeadersWithToken,
            (_, null) => SignedHeaders,
            _ => SignedHeadersWithToken,
        };

        // The canonical request, the string to sign and the Authorization value are written one after the other
        // into a single buffer, which is sized exactly.
        var canonicalRequestLength = (target is null ? FormCanonicalRequestStart.Length : CanonicalRequestStart.Length)
            + Encoding.UTF8.GetByteCount(host)
            + "\nx-amz-date:"u8.Length
            + AmzDateLength
            + (token is null ? 0 : "\nx-amz-security-token:"u8.Length + Encoding.UTF8.GetByteCount(token))
            + (target is null ? 0 : "\nx-amz-target:"u8.Length + Encoding.UTF8.GetByteCount(target))
            + "\n\n"u8.Length
            + signedHeaders.Length
            + "\n"u8.Length
            + HexSha256Length;
        var stringToSignLength = Algorithm.Length
            + "\n"u8.Length
            + AmzDateLength
            + "\n"u8.Length
            + DateStampLength
            + _scopeSuffix.Length
            + "\n"u8.Length
            + HexSha256Length;
        var authorizationLength = Algorithm.Length
            + " Credential="u8.Length
            + Encoding.UTF8.GetByteCount(credentials.AccessKeyId)
            + "/"u8.Length
            + DateStampLength
            + _scopeSuffix.Length
            + ", SignedHeaders="u8.Length
            + signedHeaders.Length
            + ", Signature="u8.Length
            + HexSha256Length;
        var bufferLength = canonicalRequestLength + stringToSignLength + authorizationLength;

        byte[]? rented = null;
        var buffer = bufferLength <= MaxStackBufferLength
            ? stackalloc byte[bufferLength]
            : (rented = _pool.Rent(bufferLength)).AsSpan();
        try
        {
            var writer = new Utf8Writer(buffer);
            WriteCanonicalRequest(ref writer, host, amzDate, token, target, signedHeaders, payloadSha256);

            Span<byte> hash = stackalloc byte[Sha256Length];
            SHA256.HashData(writer.TakeWritten(), hash);
            WriteStringToSign(ref writer, amzDate, dateStamp, hash);

            // The hash buffer is reused for the signature, the string to sign has been consumed by then.
            HMACSHA256.HashData(signingKey, writer.TakeWritten(), hash);
            WriteAuthorization(ref writer, credentials.AccessKeyId, dateStamp, signedHeaders, hash);
            return Encoding.UTF8.GetString(writer.TakeWritten());
        }
        finally
        {
            if (rented is not null)
            {
                _pool.Return(rented);
            }
        }
    }

    private void WriteStringToSign(ref Utf8Writer writer, ReadOnlySpan<byte> amzDate, ReadOnlySpan<byte> dateStamp, ReadOnlySpan<byte> canonicalRequestHash)
    {
        writer.Write(Algorithm);
        writer.Write("\n"u8);
        writer.Write(amzDate);
        writer.Write("\n"u8);
        writer.Write(dateStamp);
        writer.Write(_scopeSuffix);
        writer.Write("\n"u8);
        writer.WriteHex(canonicalRequestHash);
    }

    private void WriteAuthorization(ref Utf8Writer writer, string accessKeyId, ReadOnlySpan<byte> dateStamp, ReadOnlySpan<byte> signedHeaders, ReadOnlySpan<byte> signature)
    {
        writer.Write(Algorithm);
        writer.Write(" Credential="u8);
        writer.Write(accessKeyId);
        writer.Write("/"u8);
        writer.Write(dateStamp);
        writer.Write(_scopeSuffix);
        writer.Write(", SignedHeaders="u8);
        writer.Write(signedHeaders);
        writer.Write(", Signature="u8);
        writer.WriteHex(signature);
    }

    private byte[] GetSigningKey(AwsCredentials credentials, ReadOnlySpan<byte> dateStamp)
    {
        var cached = _cachedKey;
        if (cached is not null
            && ReferenceEquals(cached.Credentials, credentials)
            && dateStamp.SequenceEqual(cached.DateStamp))
        {
            return cached.Key;
        }

        var key = DeriveSigningKey(credentials.SecretAccessKey, dateStamp);
        _cachedKey = new CachedSigningKey(credentials, dateStamp.ToArray(), key);
        return key;
    }

    private byte[] DeriveSigningKey(string secretAccessKey, ReadOnlySpan<byte> dateStamp)
    {
        // Runs once per credentials instance and day, so the seed is a plain array: the secret may be any length.
        var seed = new byte["AWS4"u8.Length + Encoding.UTF8.GetByteCount(secretAccessKey)];
        var seedWriter = new Utf8Writer(seed);
        seedWriter.Write("AWS4"u8);
        seedWriter.Write(secretAccessKey);

        // HMAC chain: date, region, service, terminator. Each step keys the next one.
        Span<byte> chain = stackalloc byte[3 * Sha256Length];
        var dateKey = chain[..Sha256Length];
        var regionKey = chain.Slice(Sha256Length, Sha256Length);
        var serviceKey = chain[(2 * Sha256Length)..];
        HMACSHA256.HashData(seed, dateStamp, dateKey);
        HMACSHA256.HashData(dateKey, _region, regionKey);
        HMACSHA256.HashData(regionKey, _service, serviceKey);

        var signingKey = new byte[Sha256Length];
        HMACSHA256.HashData(serviceKey, "aws4_request"u8, signingKey);
        return signingKey;
    }

    // Appends to a buffer that the caller sized exactly. Writing past the end throws instead of truncating.
    private ref struct Utf8Writer(Span<byte> destination)
    {
        private readonly Span<byte> _destination = destination;
        private int _taken;
        private int _written;

        public void Write(ReadOnlySpan<byte> value)
        {
            value.CopyTo(_destination[_written..]);
            _written += value.Length;
        }

        public void Write(string value)
        {
            _written += Encoding.UTF8.GetBytes(value, _destination[_written..]);
        }

        public void WriteHex(ReadOnlySpan<byte> value)
        {
            var length = 2 * value.Length;
            Convert.TryToHexStringLower(value, _destination.Slice(_written, length), out _);
            _written += length;
        }

        // Returns what has been written since the last call, so that consecutive sections share one buffer.
        public ReadOnlySpan<byte> TakeWritten()
        {
            var section = _destination[_taken.._written];
            _taken = _written;
            return section;
        }
    }

    // The signing time as the header text and as UTF-8, for the second it was computed for. Immutable, replaced as a whole.
    private sealed class CachedSigningTime(long second, byte[] utf8)
    {
        public long Second { get; } = second;

        public byte[] Utf8 { get; } = utf8;

        public string Text { get; } = Encoding.ASCII.GetString(utf8);
    }

    private sealed class CachedSigningKey(AwsCredentials credentials, byte[] dateStamp, byte[] key)
    {
        public AwsCredentials Credentials { get; } = credentials;

        public byte[] DateStamp { get; } = dateStamp;

        public byte[] Key { get; } = key;
    }
}
