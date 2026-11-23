using System.Text;
using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class CredentialsJsonTests
{
    [Test]
    public async Task TryRead_StringsAndNumbers_ReadsThemByTypeAndTheLastOfAName()
    {
        // Arrange
        var json = """{"a": "1", "b": 2, "c": true, "d": [1], "e": "first", "e": "last"}"""u8.ToArray();

        // Act
        var members = CredentialsJson.TryRead(json)!;

        // Assert
        await Assert.That((members.GetString("a"), members.GetString("b"), members.TryGetInt64("a", out _), members.TryGetInt64("b", out var b), b, members.GetString("c"), members.GetString("d"), members.GetString("e")))
            .IsEqualTo(("1", (string?)null, false, true, 2L, (string?)null, (string?)null, "last"));
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("\"text\"")]
    [Arguments("""{"a": "b" """)]
    public async Task TryRead_NoJsonObject_ReturnsNull(string json)
    {
        // Arrange

        // Act
        var members = CredentialsJson.TryRead(Encoding.UTF8.GetBytes(json));

        // Assert
        await Assert.That(members).IsNull();
    }

    [Test]
    public async Task TryRead_StringThatIsNotUtf8_ReturnsNull()
    {
        // Arrange
        byte[] json = [.. "{\"a\": \""u8, 0xC3, 0x28, .. "\"}"u8];

        // Act
        var members = CredentialsJson.TryRead(json);

        // Assert
        await Assert.That(members).IsNull();
    }

    [Test]
    public async Task TryRead_Within_ReadsTheMembersOfThatObjectOnly()
    {
        // Arrange
        var json = """{"a": "outer", "other": {"b": "other"}, "inner": "not an object", "inner": {"a": "inner", "c": {"d": 1}}}"""u8.ToArray();

        // Act
        var members = CredentialsJson.TryRead(json, "inner")!;

        // Assert
        await Assert.That((members.GetString("a"), members.GetString("b"), members.TryGetDouble("d", out _))).IsEqualTo(("inner", (string?)null, false));
    }

    [Test]
    [Arguments("1", true, 1d)]
    [Arguments("1.0", true, 1d)]
    [Arguments("1e0", true, 1d)]
    [Arguments("\"1\"", false, 0d)]
    public async Task TryGetDouble_Member_IsANumberOnlyWhenWrittenAsOne(string value, bool expected, double number)
    {
        // Arrange
        var members = CredentialsJson.TryRead(Encoding.UTF8.GetBytes($"{{\"n\": {value}}}"))!;

        // Act
        var found = members.TryGetDouble("n", out var actual);

        // Assert
        await Assert.That((found, actual)).IsEqualTo((expected, number));
    }

    [Test]
    [Arguments("1.5")]
    [Arguments("1e3")]
    [Arguments("99999999999999999999")]
    public async Task TryGetInt64_NoWholeNumberOfALong_ReturnsFalse(string value)
    {
        // Arrange
        var members = CredentialsJson.TryRead(Encoding.UTF8.GetBytes($"{{\"n\": {value}}}"))!;

        // Act
        var found = members.TryGetInt64("n", out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task DescribeMissing_SomeMissing_NamesThemInOrder()
    {
        // Arrange

        // Act
        var missing = CredentialsJson.DescribeMissing(("A", true), ("B", false), ("C", true));

        // Assert
        await Assert.That(missing).IsEqualTo("A, C");
    }

    [Test]
    public async Task DescribeMissing_NoneMissing_ReturnsNull()
    {
        // Arrange

        // Act
        var missing = CredentialsJson.DescribeMissing(("A", false));

        // Assert
        await Assert.That(missing).IsNull();
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
        var time = CredentialsJson.ParseTime(text);

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
        var time = CredentialsJson.ParseTime(text);

        // Assert
        await Assert.That(time).IsNull();
    }
}
