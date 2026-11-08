using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// Pins the split of a <c>credential_process</c> to botocore. The expected words are what botocore's <c>_windows_shell_split</c> and
/// <c>shlex.split</c> return for the same text (botocore 1.42), each word in brackets and joined by <c>|</c>, and <c>&lt;null&gt;</c> for its ValueError.
/// </summary>
public sealed class CommandLineTests
{
    [Test]
    [Arguments("", "")]
    [Arguments("foo", "[foo]")]
    [Arguments("foo bar", "[foo]|[bar]")]
    [Arguments("  foo   bar  ", "[foo]|[bar]")]
    [Arguments("foo\tbar", "[foo]|[bar]")]
    [Arguments("\"foo bar\"", "[foo bar]")]
    [Arguments("\"\"", "[]")]
    [Arguments("a \"\" b", "[a]|[]|[b]")]
    [Arguments("foo\\bar", "[foo\\bar]")]
    [Arguments("foo\\\\bar", "[foo\\\\bar]")]
    [Arguments("foo\\\"bar", "[foo\"bar]")]
    [Arguments("foo\\\\\"bar baz\"", "[foo\\bar baz]")]
    [Arguments("foo\\\\\\\"bar", "[foo\\\"bar]")]
    [Arguments("\"foo\\\\\"", "[foo\\]")]
    [Arguments("foo\\", "[foo\\]")]
    [Arguments("foo\\\\", "[foo\\\\]")]
    [Arguments("\"a b\"c d", "[a bc]|[d]")]
    [Arguments("C:\\Program Files\\x.exe", "[C:\\Program]|[Files\\x.exe]")]
    [Arguments("\"C:\\Program Files\\x.exe\" --a \"b c\"", "[C:\\Program Files\\x.exe]|[--a]|[b c]")]
    [Arguments("'single quoted'", "['single]|[quoted']")]
    [Arguments("\"unclosed", "<null>")]
    [Arguments("a\\ b", "[a\\]|[b]")]
    [Arguments("\\ ", "[\\]")]
    [Arguments("say\\\"hi\\\"", "[say\"hi\"]")]
    [Arguments("a\"b c\"d", "[ab cd]")]
    [Arguments("a\\\\ b", "[a\\\\]|[b]")]
    [Arguments("a \\", "[a]|[\\]")]
    public async Task Split_Windows_ReturnsTheWordsOfBotocore(string text, string expected)
    {
        // Arrange

        // Act
        var words = CommandLine.Split(text, windows: true);

        // Assert
        await Assert.That(Describe(words)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/usr/bin/creds --profile dev", "[/usr/bin/creds]|[--profile]|[dev]")]
    [Arguments("sh -c 'echo hi'", "[sh]|[-c]|[echo hi]")]
    [Arguments("a \"b c\" d", "[a]|[b c]|[d]")]
    [Arguments("a\\ b", "[a b]")]
    [Arguments("\"unclosed", "<null>")]
    [Arguments("end\\", "<null>")]
    [Arguments("", "")]
    public async Task Split_Posix_ReturnsTheWordsOfBotocore(string text, string expected)
    {
        // Arrange

        // Act
        var words = CommandLine.Split(text, windows: false);

        // Assert
        await Assert.That(Describe(words)).IsEqualTo(expected);
    }

    private static string Describe(string[]? words) => words is null ? "<null>" : string.Join('|', words.Select(word => $"[{word}]"));
}
