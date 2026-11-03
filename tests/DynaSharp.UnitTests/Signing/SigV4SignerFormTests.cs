using System.Security.Cryptography;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Signing;

namespace DynaSharp.UnitTests.Signing;

/// <summary>
/// The forms of the Query protocol that <c>AssumeRole</c> sends. The expected headers are what botocore 1.42's <c>SigV4Auth</c> adds to the same
/// request at the same time: <c>AWSRequest(method="POST", url=..., data=Body, headers={"Content-Type": "application/x-www-form-urlencoded; charset=utf-8"})</c>,
/// service <c>sts</c>, region <c>eu-west-1</c>, with <c>botocore.auth.get_current_datetime</c> patched to 2026-10-31 12:34:56.
/// </summary>
public sealed class SigV4SignerFormTests
{
    // The body that botocore's sts client serializes for assume_role(RoleArn="arn:aws:iam::123456789012:role/orders reader",
    // RoleSessionName="dev@corp", DurationSeconds=3600, ExternalId="ext-id/1").
    private const string Body = "Action=AssumeRole&Version=2011-06-15&RoleArn=arn%3Aaws%3Aiam%3A%3A123456789012%3Arole%2Forders+reader&RoleSessionName=dev%40corp&DurationSeconds=3600&ExternalId=ext-id%2F1";
    private const string AccessKeyId = "AKIDEXAMPLE";
    private const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private const string SessionToken = "IQoJb3JpZ2luX2VjEEM/Example==";

    private static readonly DateTimeOffset Time = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    [Test]
    [Arguments("sts.eu-west-1.amazonaws.com", null, "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20261031/eu-west-1/sts/aws4_request, SignedHeaders=content-type;host;x-amz-date, Signature=668be12614b523dc63473bea92b1a1d2be49b8a0ad1900373987c52b0cead44d")]
    [Arguments("sts.eu-west-1.amazonaws.com", SessionToken, "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20261031/eu-west-1/sts/aws4_request, SignedHeaders=content-type;host;x-amz-date;x-amz-security-token, Signature=bc93c35e970bbff77ff30175cce3a6fb98b3ac4e054435dcde3416b53d1e6e9a")]
    [Arguments("127.0.0.1:8443", null, "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20261031/eu-west-1/sts/aws4_request, SignedHeaders=content-type;host;x-amz-date, Signature=b6074c853e919c0d9318b9c9fea393a2f22310b8019a5eefc164fc17de47c3ec")]
    public async Task SignForm_BotocoreVector_SetsAuthorizationHeaderLikeBotocore(string authority, string? sessionToken, string expected)
    {
        // Arrange
        var signer = new SigV4Signer("eu-west-1", "sts");
        using var request = CreateRequest(authority);

        // Act
        signer.SignForm(request, authority, SHA256.HashData(Encoding.UTF8.GetBytes(Body)), new AwsCredentials(AccessKeyId, SecretAccessKey, sessionToken), Time);

        // Assert
        await Assert.That(request.Headers.GetValues("Authorization").Single()).IsEqualTo(expected);
    }

    [Test]
    public async Task SignForm_TemporaryCredentials_SetsTheDateAndTheTokenHeaders()
    {
        // Arrange
        var signer = new SigV4Signer("eu-west-1", "sts");
        using var request = CreateRequest("sts.eu-west-1.amazonaws.com");

        // Act
        signer.SignForm(request, "sts.eu-west-1.amazonaws.com", SHA256.HashData(Encoding.UTF8.GetBytes(Body)), new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken), Time);

        // Assert
        await Assert.That($"{request.Headers.GetValues("X-Amz-Date").Single()} {request.Headers.GetValues("X-Amz-Security-Token").Single()}")
            .IsEqualTo($"20261031T123456Z {SessionToken}");
    }

    [Test]
    public async Task SignForm_HashOfAnotherLength_ThrowsArgumentException()
    {
        // Arrange
        var signer = new SigV4Signer("eu-west-1", "sts");
        using var request = CreateRequest("sts.eu-west-1.amazonaws.com");

        // Act
        var act = () => signer.SignForm(request, "sts.eu-west-1.amazonaws.com", new byte[31], new AwsCredentials(AccessKeyId, SecretAccessKey), Time);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("payloadSha256");
    }

    [Test]
    public async Task SignForm_ContentType_IsWhatStringContentRendersForAForm()
    {
        // Arrange (the signature covers the content type as the signer spells it, so the request must send exactly that)
        using var request = CreateRequest("sts.eu-west-1.amazonaws.com");

        // Act
        var contentType = request.Content!.Headers.ContentType!.ToString();

        // Assert
        await Assert.That(contentType).IsEqualTo("application/x-www-form-urlencoded; charset=utf-8");
    }

    private static HttpRequestMessage CreateRequest(string authority) => new(HttpMethod.Post, new Uri($"https://{authority}/"))
    {
        Content = new StringContent(Body, Encoding.UTF8, "application/x-www-form-urlencoded"),
    };
}
