using System.Text;
using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class CredentialsDocumentTests
{
    // The example output of the Amazon ECS documentation ("Best practices for IAM roles in Amazon ECS": curl
    // 169.254.170.2$AWS_CONTAINER_CREDENTIALS_RELATIVE_URI).
    private const string EcsSample = """
        {
          "RoleArn": "arn:aws:iam::123456789012:role/SSMTaskRole-SSMFargateTaskIAMRole-DASWWSF2WGD6",
          "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
          "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
          "Token": "IQoJb3JpZ2luX2VjEEM/Example==",
          "Expiration": "2021-01-16T00:51:53Z"
        }
        """;

    // The example output of the Amazon EC2 documentation ("Retrieve security credentials from instance metadata").
    private const string ImdsSample = """
        {
          "Code" : "Success",
          "LastUpdated" : "2012-04-26T16:39:16Z",
          "Type" : "AWS-HMAC",
          "AccessKeyId" : "ASIAIOSFODNN7EXAMPLE",
          "SecretAccessKey" : "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
          "Token" : "token",
          "Expiration" : "2017-05-17T15:09:54Z"
        }
        """;

    [Test]
    public async Task TryCreateCredentials_EcsSample_ReadsAllFields()
    {
        // Arrange
        var document = Parse(EcsSample);

        // Act
        var problem = document.TryCreateCredentials(out var credentials);

        // Assert
        await Assert.That((problem, credentials!.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo((null, "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "IQoJb3JpZ2luX2VjEEM/Example==", new DateTimeOffset(2021, 1, 16, 0, 51, 53, TimeSpan.Zero)));
    }

    [Test]
    public async Task TryCreateCredentials_InstanceMetadataSample_ReadsAllFields()
    {
        // Arrange
        var document = Parse(ImdsSample);

        // Act
        var problem = document.TryCreateCredentials(out var credentials);

        // Assert
        await Assert.That((problem, credentials!.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo((null, "ASIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "token", new DateTimeOffset(2017, 5, 17, 15, 9, 54, TimeSpan.Zero)));
    }

    [Test]
    public async Task TryParse_InstanceMetadataSample_ReadsTheCode()
    {
        // Arrange

        // Act
        var document = Parse(ImdsSample);

        // Assert
        await Assert.That(document.Code).IsEqualTo("Success");
    }

    [Test]
    public async Task TryParse_UnknownPropertiesOfAnyType_AreSkipped()
    {
        // Arrange (nested values must be skipped as a whole)
        const string json = """{"a":{"b":[1,2,{"c":"d"}]},"AccessKeyId":"id","n":null,"x":1.5,"SecretAccessKey":"secret","Token":"t","Expiration":"2021-01-16T00:51:53Z","tail":[true,false]}""";

        // Act
        var parsed = CredentialsDocument.TryParse(Encoding.UTF8.GetBytes(json), out var document);

        // Assert
        await Assert.That((parsed, document.AccessKeyId, document.SecretAccessKey)).IsEqualTo((true, "id", "secret"));
    }

    [Test]
    public async Task TryParse_FieldOfAnotherTypeWithNestedProperties_DoesNotTakeTheNestedPropertiesAsFields()
    {
        // Arrange (the nested objects have members named like the fields of the document)
        const string json = """{"AccessKeyId":{"SecretAccessKey":"nested-secret"},"Code":{"Token":"nested-token"}}""";

        // Act
        var parsed = CredentialsDocument.TryParse(Encoding.UTF8.GetBytes(json), out var document);

        // Assert
        await Assert.That((parsed, document.SecretAccessKey, document.Token)).IsEqualTo((true, (string?)null, (string?)null));
    }

    [Test]
    public async Task TryParse_PropertyNameWithEscape_IsMatchedAfterUnescaping()
    {
        // Arrange ("A" is written as A; the escape is spelled out with a backslash and a letter to survive any editor)
        var json = "{\"" + "\\" + "u0041ccessKeyId\":\"id\"}";

        // Act
        var document = Parse(json);

        // Assert
        await Assert.That(document.AccessKeyId).IsEqualTo("id");
    }

    [Test]
    [Arguments("""{"AccessKeyId":1}""")]
    [Arguments("""{"AccessKeyId":null}""")]
    [Arguments("""{"AccessKeyId":{"x":"y"}}""")]
    [Arguments("""{"AccessKeyId":["id"]}""")]
    [Arguments("""{"AccessKeyId":true}""")]
    public async Task TryParse_PropertyWithAnotherTypeThanString_CountsAsMissing(string json)
    {
        // Arrange

        // Act
        var parsed = CredentialsDocument.TryParse(Encoding.UTF8.GetBytes(json), out var document);

        // Assert
        await Assert.That((parsed, document.AccessKeyId)).IsEqualTo((true, (string?)null));
    }

    [Test]
    public async Task TryParse_StringThatIsNotValidUtf8_ReturnsFalse()
    {
        // Arrange (the bytes of {"Token":"..."} with a byte that no UTF-8 sequence has as the value)
        byte[] json = [.. "{\"Token\":\""u8, 0xFF, .. "\"}"u8];

        // Act
        var parsed = CredentialsDocument.TryParse(json, out _);

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_DuplicateProperty_LastWins()
    {
        // Arrange
        const string json = """{"AccessKeyId":"first","AccessKeyId":"second"}""";

        // Act
        var document = Parse(json);

        // Assert
        await Assert.That(document.AccessKeyId).IsEqualTo("second");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not json")]
    [Arguments("<html><body>Bad Gateway</body></html>")]
    [Arguments("[1,2]")]
    [Arguments("\"AccessKeyId\"")]
    [Arguments("null")]
    [Arguments("{")]
    [Arguments("""{"AccessKeyId":"id",""")]
    [Arguments("""{"AccessKeyId":"id""")]
    [Arguments("""{"AccessKeyId" "id"}""")]
    [Arguments("""{"AccessKeyId":"id",}""")]
    public async Task TryParse_NotAJsonObject_ReturnsFalse(string json)
    {
        // Arrange

        // Act
        var parsed = CredentialsDocument.TryParse(Encoding.UTF8.GetBytes(json), out var document);

        // Assert
        await Assert.That((parsed, document)).IsEqualTo((false, default(CredentialsDocument)));
    }

    [Test]
    public async Task ToString_FullDocument_LeavesTheSecretsOut()
    {
        // Arrange
        var document = new CredentialsDocument("access-key", "secret-key", "session-token", "2021-01-16T00:51:53Z", null);

        // Act
        var text = new StsDocument(document, null).ToString();

        // Assert
        await Assert.That((text.Contains("secret-key", StringComparison.Ordinal), text.Contains("session-token", StringComparison.Ordinal))).IsEqualTo((false, false));
    }

    [Test]
    [Arguments("""{"SecretAccessKey":"s","Token":"t","Expiration":"2021-01-16T00:51:53Z"}""", "the response does not contain AccessKeyId")]
    [Arguments("""{"AccessKeyId":"a","Token":"t","Expiration":"2021-01-16T00:51:53Z"}""", "the response does not contain SecretAccessKey")]
    [Arguments("""{"AccessKeyId":"a","SecretAccessKey":"s","Expiration":"2021-01-16T00:51:53Z"}""", "the response does not contain Token")]
    [Arguments("""{"AccessKeyId":"a","SecretAccessKey":"s","Token":"t"}""", "the response does not contain Expiration")]
    [Arguments("""{"AccessKeyId":"","SecretAccessKey":"","Token":"","Expiration":""}""", "the response does not contain AccessKeyId, SecretAccessKey, Token, Expiration")]
    [Arguments("""{"Code":"AssumeRoleUnauthorizedAccess","Message":"EC2 cannot assume the role."}""", "the response does not contain AccessKeyId, SecretAccessKey, Token, Expiration")]
    [Arguments("""{"AccessKeyId":"a","SecretAccessKey":"s","Token":"t","Expiration":"tomorrow-ish"}""", "the Expiration of the response is not a valid timestamp")]
    public async Task TryCreateCredentials_MissingOrInvalidField_NamesTheProblem(string json, string expected)
    {
        // Arrange
        var document = Parse(json);

        // Act
        var problem = document.TryCreateCredentials(out var credentials);

        // Assert
        await Assert.That((problem, credentials)).IsEqualTo((expected, (AwsCredentials?)null));
    }

    [Test]
    [Arguments("2021-01-16T00:51:53Z", 2021, 1, 16, 0, 51, 53, 0)]
    [Arguments("2021-01-16T01:51:53+01:00", 2021, 1, 16, 0, 51, 53, 0)]
    [Arguments("2021-01-16T00:51:53.5Z", 2021, 1, 16, 0, 51, 53, 500)]
    [Arguments("2021-01-16T00:51:53", 2021, 1, 16, 0, 51, 53, 0)]
    public async Task TryCreateCredentials_ExpirationFormats_AreReadAsUtc(string expiration, int year, int month, int day, int hour, int minute, int second, int millisecond)
    {
        // Arrange (a time without an offset is UTC, as the services mean it)
        var json = $$"""{"AccessKeyId":"a","SecretAccessKey":"s","Token":"t","Expiration":"{{expiration}}"}""";
        var document = Parse(json);

        // Act
        document.TryCreateCredentials(out var credentials);

        // Assert
        await Assert.That(credentials!.Expiration).IsEqualTo(new DateTimeOffset(year, month, day, hour, minute, second, millisecond, TimeSpan.Zero));
    }

    [Test]
    public async Task TryCreateCredentials_NamedTokenField_NamesItInTheMessage()
    {
        // Arrange
        var document = new CredentialsDocument("key", "secret", null, "2030-01-01T00:00:00Z", null);

        // Act
        var problem = document.TryCreateCredentials(out _, "SessionToken");

        // Assert
        await Assert.That(problem).IsEqualTo("the response does not contain SessionToken");
    }

    private static CredentialsDocument Parse(string json) =>
        CredentialsDocument.TryParse(Encoding.UTF8.GetBytes(json), out var document) ? document : throw new InvalidOperationException("The sample is not a JSON object.");
}
