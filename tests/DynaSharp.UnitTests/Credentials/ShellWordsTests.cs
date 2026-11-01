using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Credentials;

public sealed class ShellWordsTests
{
    [Test]
    [Arguments("profile dev", "profile|dev")]
    [Arguments("  profile \t dev  ", "profile|dev")]
    [Arguments("profile \"my profile\"", "profile|my profile")]
    [Arguments("profile 'my profile'", "profile|my profile")]
    [Arguments("profile my\\ profile", "profile|my profile")]
    [Arguments("profile \"a\\\"b\"", "profile|a\"b")]
    [Arguments("profile \"a\\\\b\"", "profile|a\\b")]
    [Arguments("profile \"a\\nb\"", "profile|a\\nb")]
    [Arguments("profile 'a\\b'", "profile|a\\b")]
    [Arguments("profile a\"b c\"d", "profile|ab cd")]
    [Arguments("profile ''", "profile|")]
    [Arguments("", "")]
    public async Task Split_ValidText_SplitsLikeShlexInPosixMode(string text, string expected)
    {
        // Arrange (the expected words are those of Python's shlex.split, joined with a bar)

        // Act
        var words = ShellWords.Split(text);

        // Assert
        await Assert.That(string.Join('|', words!)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("profile \"dev")]
    [Arguments("profile 'dev")]
    [Arguments("profile dev\\")]
    [Arguments("profile \"dev\\")]
    public async Task Split_UnclosedQuoteOrDanglingBackslash_ReturnsNull(string text)
    {
        // Arrange (shlex.split raises ValueError for these)

        // Act
        var words = ShellWords.Split(text);

        // Assert
        await Assert.That(words).IsNull();
    }
}
