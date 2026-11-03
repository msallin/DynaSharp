using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class SsoTokenTests
{
    [Test]
    public async Task Parse_FileOfASession_ReadsEveryMember()
    {
        // Arrange (what `aws sso login --sso-session` caches)
        const string json = """
            {"startUrl": "https://my-sso-portal.awsapps.com/start", "region": "us-east-1", "accessToken": "at", "expiresAt": "2026-11-03T20:00:00Z",
             "clientId": "ci", "clientSecret": "cs", "registrationExpiresAt": "2027-01-31T08:00:00Z", "refreshToken": "rt"}
            """;

        // Act
        var token = SsoToken.Parse(json);

        // Assert
        await Assert.That(token).IsEqualTo(new SsoToken("https://my-sso-portal.awsapps.com/start", "us-east-1", "at", "2026-11-03T20:00:00Z", "ci", "cs", "2027-01-31T08:00:00Z", "rt"));
    }

    [Test]
    public async Task Parse_MembersOfAnotherTypeAndUnknownMembers_CountAsMissing()
    {
        // Arrange
        const string json = """{"accessToken": 1, "expiresAt": null, "other": {"a": [1, 2]}, "refreshToken": "rt"}""";

        // Act
        var token = SsoToken.Parse(json);

        // Assert
        await Assert.That(token).IsEqualTo(new SsoToken(null, null, null, null, null, null, null, "rt"));
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("\"text\"")]
    [Arguments("""{"accessToken": "at" """)]
    public async Task Parse_NoJsonObject_ReturnsNull(string json)
    {
        // Arrange

        // Act
        var token = SsoToken.Parse(json);

        // Assert
        await Assert.That(token).IsNull();
    }

    [Test]
    [Arguments("2026-11-03T20:00:00Z", "2026-11-03T20:00:00.0000000+00:00")]
    [Arguments("2026-11-03T20:00:00UTC", "2026-11-03T20:00:00.0000000+00:00")]
    [Arguments("2026-11-03T22:00:00+02:00", "2026-11-03T20:00:00.0000000+00:00")]
    [Arguments("2026-11-03T20:00:00", "2026-11-03T20:00:00.0000000+00:00")]
    public async Task ParseTime_FormsOfTheCliAndOfBotocore_AreUtc(string text, string expected)
    {
        // Arrange

        // Act
        var time = SsoToken.ParseTime(text);

        // Assert
        await Assert.That(time?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("tomorrow")]
    [Arguments("UTC")]
    public async Task ParseTime_NoTime_ReturnsNull(string? text)
    {
        // Arrange

        // Act
        var time = SsoToken.ParseTime(text);

        // Assert
        await Assert.That(time).IsNull();
    }

    [Test]
    public async Task ToJson_RefreshedToken_WritesTheMembersInTheOrderOfBotocore()
    {
        // Arrange
        var token = new SsoToken("https://s.example/start", "eu-west-1", "at", "2026-11-03T20:00:00Z", "ci", "cs", "2027-01-31T08:00:00Z", "rt");

        // Act
        var json = token.ToJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"startUrl":"https://s.example/start","region":"eu-west-1","accessToken":"at","expiresAt":"2026-11-03T20:00:00Z","clientId":"ci","clientSecret":"cs","registrationExpiresAt":"2027-01-31T08:00:00Z","refreshToken":"rt"}""");
    }

    [Test]
    public async Task ToJson_MembersNotSet_AreLeftOut()
    {
        // Arrange (botocore saves no refreshToken when CreateToken returns none)
        var token = new SsoToken(null, null, "at", "2026-11-03T20:00:00Z", null, null, null, null);

        // Act
        var json = token.ToJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"accessToken":"at","expiresAt":"2026-11-03T20:00:00Z"}""");
    }

    [Test]
    public async Task ToJson_ThenParse_GivesTheSameToken()
    {
        // Arrange (a token of base64 with the characters that JSON escapes)
        var token = new SsoToken("https://s.example/start", "eu-west-1", "a+b/c=", "2026-11-03T20:00:00Z", "ci", "c\"s", "2027-01-31T08:00:00Z", "r<t>");

        // Act
        var parsed = SsoToken.Parse(token.ToJson());

        // Assert
        await Assert.That(parsed).IsEqualTo(token);
    }

    [Test]
    public async Task ToString_Token_ShowsNoSecret()
    {
        // Arrange
        var token = new SsoToken(null, null, "secret-access-token", null, null, null, null, "secret-refresh-token");

        // Act
        var text = token.ToString();

        // Assert
        await Assert.That(text).IsEqualTo(nameof(SsoToken));
    }
}
