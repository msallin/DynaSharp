using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Signing;
using DynaSharp.UnitTests.Properties;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Signing;

/// <summary>
/// The signer against Signature Version 4 as the specification writes it, with strings and no buffers, for any credentials, region, target and time; and a signer that caches
/// its signing key and its time against a new one for every request, over sequences that change the credentials, the second and the day.
/// </summary>
public sealed class SigV4SignerPropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Requests)])]
    public async Task Sign_AnyRequest_SignsAsTheSpecificationSays(SignedRequest signing)
    {
        // Arrange
        var signer = new SigV4Signer(signing.Region, signing.Service);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://localhost/"));

        // Act
        Sign(signer, request, signing);

        // Assert
        await Assert.That(request.Headers.NonValidated["Authorization"].ToString()).IsEqualTo(Reference(signing));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Requests)])]
    public async Task Sign_SequenceOnOneSigner_SignsAsANewSignerForEachRequest(SignedSequence sequence)
    {
        // Arrange (one signer caches the key of the last credentials and day and the text of the last second)
        var shared = new SigV4Signer("eu-central-1");

        // Act
        var cached = sequence.Signings.Select(signing => Headers(shared, signing)).ToList();
        var fresh = sequence.Signings.Select(static signing => Headers(new SigV4Signer("eu-central-1"), signing)).ToList();

        // Assert
        await Assert.That(cached).IsEquivalentTo(fresh, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static void Sign(SigV4Signer signer, HttpRequestMessage request, SignedRequest signing)
    {
        if (signing.Target is null)
        {
            signer.SignForm(request, signing.Authority, signing.PayloadSha256, signing.Credentials, signing.Time);
        }
        else
        {
            signer.Sign(request, signing.Authority, signing.Target, signing.PayloadSha256, signing.Credentials, signing.Time);
        }
    }

    private static string Headers(SigV4Signer signer, SignedRequest signing)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://localhost/"));
        Sign(signer, request, signing);
        return string.Join('\n', request.Headers.NonValidated.Select(static header => $"{header.Key}: {header.Value}"));
    }

    // https://docs.aws.amazon.com/IAM/latest/UserGuide/create-signed-request.html, step by step.
    private static string Reference(SignedRequest signing)
    {
        var utc = signing.Time.UtcDateTime;
        var amzDate = utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = amzDate[..8];
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["content-type"] = signing.Target is null ? "application/x-www-form-urlencoded; charset=utf-8" : "application/x-amz-json-1.0",
            ["host"] = signing.Authority,
            ["x-amz-date"] = amzDate,
        };
        if (signing.Credentials.SessionToken is { } token)
        {
            headers["x-amz-security-token"] = token;
        }

        if (signing.Target is { } target)
        {
            headers["x-amz-target"] = target;
        }

        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalRequest = $"POST\n/\n\n{string.Concat(headers.Select(static header => $"{header.Key}:{header.Value}\n"))}\n{signedHeaders}\n{Convert.ToHexStringLower(signing.PayloadSha256)}";
        var scope = $"{date}/{signing.Region}/{signing.Service}/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
        var key = Encoding.UTF8.GetBytes("AWS4" + signing.Credentials.SecretAccessKey);
        foreach (var part in new[] { date, signing.Region, signing.Service, "aws4_request" })
        {
            key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(part));
        }

        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));
        return $"AWS4-HMAC-SHA256 Credential={signing.Credentials.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    /// <summary>One request to sign.</summary>
    /// <param name="Region">The region.</param>
    /// <param name="Service">The service.</param>
    /// <param name="Authority">The host and port.</param>
    /// <param name="Target">The <c>X-Amz-Target</c>, or <see langword="null"/> for a form of the Query protocol.</param>
    /// <param name="PayloadSha256">The hash of the body.</param>
    /// <param name="Credentials">The credentials.</param>
    /// <param name="Time">The signing time.</param>
    public sealed record SignedRequest(string Region, string Service, string Authority, string? Target, byte[] PayloadSha256, AwsCredentials Credentials, DateTimeOffset Time)
    {
        /// <inheritdoc/>
        public override string ToString() =>
            $"{Region}/{Service} {Authority} {Target} {Convert.ToHexString(PayloadSha256)} {Credentials.AccessKeyId}:{Credentials.SecretAccessKey}:{Credentials.SessionToken} {Time:O}";
    }

    /// <summary>Requests one signer signs one after another.</summary>
    /// <param name="Signings">The requests.</param>
    public sealed record SignedSequence(SignedRequest[] Signings)
    {
        /// <inheritdoc/>
        public override string ToString() => string.Join(" ; ", Signings.AsEnumerable());
    }

    internal static class Requests
    {
        public static Arbitrary<SignedRequest> Signings() => (
            from region in Gen.Elements("eu-central-1", "us-east-1", "cn-north-1", "local")
            from service in Gen.Elements("dynamodb", "sts")
            from authority in Gen.Elements("dynamodb.eu-central-1.amazonaws.com", "localhost:8000", "127.0.0.1:8443", "[::1]:8000", "xn--bcher-kva.example")
            from target in Gen.Elements<string?>(null, "DynamoDB_20120810.PutItem", "DynamoDBStreams_20120810.GetRecords")
            from hash in Gen.ArrayOf(Gen.Choose(0, 255).Select(static value => (byte)value), 32)
            from credentials in Credentials()
            from time in Generate.DateTimeOffset
            select new SignedRequest(region, service, authority, target, hash, credentials, time)).ToArbitrary();

        public static Arbitrary<SignedSequence> Sequences() => (
            from pool in Gen.ArrayOf(Credentials(), 3)
            from start in Generate.DateTimeOffset.Where(static time => time.UtcDateTime.Year is > 1 and < 9999)
            from steps in Gen.NonEmptyListOf(Step(pool)).Resize(8)
            select new SignedSequence([.. Times(start, steps, pool)])).ToArbitrary();

        // A secret in any characters, and a token as STS hands it over: base64 text, of the length of a role session or far beyond the stack buffer of the signer.
        private static Gen<AwsCredentials> Credentials() =>
            from accessKeyId in Gen.Elements("AKIDEXAMPLE", "ASIAEXAMPLE0123456789")
            from secret in Generate.NonEmptyText
            from token in Gen.Frequency((1, Gen.Constant<string?>(null)), (1, Gen.ArrayOf(Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".ToCharArray()), 40).Select(static characters => (string?)new string(characters))), (1, Gen.ArrayOf(Gen.Elements('A', '/', '+', '='), 1000).Select(static characters => (string?)new string(characters))))
            select new AwsCredentials(accessKeyId, secret, token);

        // A step takes credentials of the pool, a copy of them (equal, but another instance), or new ones, and moves the clock by nothing, a fraction of a second,
        // some seconds, or past midnight.
        private static Gen<(int Credentials, AwsCredentials? Other, long Ticks, string? Target)> Step(AwsCredentials[] pool) =>
            from index in Gen.Choose(0, pool.Length - 1)
            from other in Gen.Frequency((4, Gen.Constant<AwsCredentials?>(null)), (1, Gen.Constant<AwsCredentials?>(new AwsCredentials(pool[index].AccessKeyId, pool[index].SecretAccessKey, pool[index].SessionToken))), (1, Credentials().Select(static credentials => (AwsCredentials?)credentials)))
            from ticks in Gen.Frequency((2, Gen.Constant(0L)), (2, Gen.Choose(1, 9_999_999).Select(static value => (long)value)), (2, Gen.Choose(1, 100).Select(static seconds => seconds * TimeSpan.TicksPerSecond)), (1, Gen.Choose(1, 3).Select(static days => days * TimeSpan.TicksPerDay)))
            from target in Gen.Elements<string?>(null, "DynamoDB_20120810.Query")
            select (index, other, ticks, target);

        private static IEnumerable<SignedRequest> Times(DateTimeOffset start, IEnumerable<(int Credentials, AwsCredentials? Other, long Ticks, string? Target)> steps, AwsCredentials[] pool)
        {
            var time = start;
            foreach (var step in steps)
            {
                time = time.UtcTicks + step.Ticks < DateTimeOffset.MaxValue.UtcTicks ? time.AddTicks(step.Ticks) : time;
                yield return new SignedRequest("eu-central-1", "dynamodb", "localhost:8000", step.Target, new byte[32], step.Other ?? pool[step.Credentials], time);
            }
        }
    }
}
