using System.Text;
using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>What botocore has no vector for, because it fails there or never looks: bodies that are no document, and the limits of the parser.</summary>
public sealed class StsDocumentTests
{
    [Test]
    [Arguments("")]
    [Arguments("not xml at all")]
    [Arguments("{\"Credentials\":{}}")]
    [Arguments("<html><body>Bad Gateway<br></body></html>")]
    [Arguments("<AssumeRoleWithWebIdentityResponse><AssumeRoleWithWebIdentityResult>")]
    [Arguments("""<?xml version="1.0" encoding="no-such-encoding"?><a/>""")]
    public async Task TryParse_NotAWellFormedDocument_ReturnsFalse(string body)
    {
        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out _);

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_AssumeRoleResponse_ReadsTheCredentials()
    {
        // Arrange (the sample of the API reference of AssumeRole)
        var body = AssumeRoleCredentialsProviderTests.StsSuccess("ASIAIOSFODNN7EXAMPLE", new DateTimeOffset(2019, 11, 9, 13, 34, 41, TimeSpan.Zero));

        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out var document);

        // Assert
        await Assert.That($"{parsed} {document.Credentials.AccessKeyId} {document.Credentials.Expiration} {document.ErrorCode}").IsEqualTo("True ASIAIOSFODNN7EXAMPLE 2019-11-09T13:34:41Z ");
    }

    [Test]
    public async Task TryParse_CredentialsUnderAnotherResult_AreNotRead()
    {
        // Arrange (only the results of the two calls hold credentials)
        var body = "<GetSessionTokenResponse><GetSessionTokenResult><Credentials><AccessKeyId>a</AccessKeyId></Credentials></GetSessionTokenResult></GetSessionTokenResponse>";

        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out var document);

        // Assert
        await Assert.That((parsed, document.Credentials.AccessKeyId)).IsEqualTo((true, null));
    }

    [Test]
    public async Task TryParse_InvalidUtf8_ReturnsFalse()
    {
        // Arrange
        byte[] body = [.. "<a>"u8, 0xC3, 0x28, .. "</a>"u8];

        // Act
        var parsed = StsDocument.TryParse(body, out _);

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_DocumentTypeDefinition_IsRefused()
    {
        // Arrange (entities of a DTD could expand to gigabytes, or fetch from the network, on behalf of whoever sits between here and STS)
        const string body = """<!DOCTYPE a [<!ENTITY b "c">]><AssumeRoleWithWebIdentityResponse><Error><Code>&b;</Code></Error></AssumeRoleWithWebIdentityResponse>""";

        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out _);

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_ElementInsideAValue_ReturnsFalse()
    {
        // Arrange
        const string body = "<R><AssumeRoleWithWebIdentityResult><Credentials><AccessKeyId><b>x</b></AccessKeyId></Credentials></AssumeRoleWithWebIdentityResult></R>";

        // Act
        var parsed = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out _);

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_CodeOutsideOfError_IsNotAnError()
    {
        // Arrange
        const string body = "<R><Other><Code>Throttling</Code></Other><Code>Throttling</Code></R>";

        // Act
        _ = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out var document);

        // Assert
        await Assert.That(document.ErrorCode).IsNull();
    }

    [Test]
    public async Task TryParse_CredentialsOutsideOfTheResult_AreNotRead()
    {
        // Arrange
        const string body = "<R><Other><Credentials><AccessKeyId>x</AccessKeyId></Credentials></Other></R>";

        // Act
        _ = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out var document);

        // Assert
        await Assert.That(document.Credentials.AccessKeyId).IsNull();
    }

    [Test]
    public async Task TryParse_SiblingOfCredentialsWithTheSameFields_IsNotRead()
    {
        // Arrange
        const string body = "<R><AssumeRoleWithWebIdentityResult><Other><AccessKeyId>x</AccessKeyId></Other></AssumeRoleWithWebIdentityResult></R>";

        // Act
        _ = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out var document);

        // Assert
        await Assert.That(document.Credentials.AccessKeyId).IsNull();
    }

    [Test]
    public async Task TryParse_MessageOfAnError_IsNotKept()
    {
        // Arrange (the message may quote the request, and the record is printed by whoever logs it)
        const string body = "<ErrorResponse><Error><Code>ValidationError</Code><Message>value 'the-token' is invalid</Message></Error></ErrorResponse>";

        // Act
        _ = StsDocument.TryParse(Encoding.UTF8.GetBytes(body), out var document);

        // Assert
        await Assert.That(document.ToString()).DoesNotContain("the-token", StringComparison.Ordinal);
    }
}
