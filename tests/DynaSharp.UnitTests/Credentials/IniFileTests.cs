using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Credentials;

public sealed class IniFileTests
{
    [Test]
    public async Task Parse_RealWorldCredentialsFile_ReadsBothProfiles()
    {
        // Arrange (the layout that "aws configure" writes, with the example keys of the AWS documentation)
        const string text = """
            [default]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY

            [dev]
            aws_access_key_id = AKIAI44QH8DHBEXAMPLE
            aws_secret_access_key = je7MtGbClwBF/2Zp9Utk/h3yCo8nvbEXAMPLEKEY
            aws_session_token = IQoJb3JpZ2luX2VjEXAMPLETOKEN
            """;

        // Act
        var sections = IniFile.Parse(text, "credentials");

        // Assert
        await Assert.That(string.Join(',', sections.Select(section => $"{section.Name}:{section.Values.Count}"))).IsEqualTo("default:2,dev:3");
    }

    [Test]
    public async Task Parse_MalformedFile_MessageNamesFileAndLine()
    {
        // Arrange
        const string text = "[default]\nregion = eu-west-1\nnot an option\n";

        // Act
        var act = () => IniFile.Parse(text, "/home/a/.aws/config");

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("'/home/a/.aws/config' is malformed at line 3", StringComparison.Ordinal);
    }

    [Test]
    public async Task Parse_MalformedFile_MessageDoesNotContainTheContent()
    {
        // Arrange (a secret on the offending line and next to it)
        const string text = "[default]\naws_secret_access_key = wJalrXUtnFEMI/K7MDENG\nwJalrXUtnFEMI/SECRETLINE\n";

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(() =>
        {
            IniFile.Parse(text, "credentials");
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(exception.Message).DoesNotContain("wJalr", StringComparison.Ordinal);
    }

    [Test]
    public async Task Parse_NestedBlockWithBadLine_MessageNamesTheLineOfTheOption()
    {
        // Arrange (botocore reports no line here; the option that starts the block is the useful place)
        const string text = "[default]\nregion = eu-west-1\ns3 =\n  a = 1\n  oops\n";

        // Act
        var act = () => IniFile.Parse(text, "config");

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("'config' is malformed at line 3", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("[a]\n[a]\n", "a section appears twice")]
    [Arguments("[a]\nx = 1\nx = 2\n", "an option appears twice in a section")]
    [Arguments("[a]\n= 1\n= 2\n", "an option appears twice in a section")]
    [Arguments("[a]\nnot an option\n", "expected 'name = value' or a [section] header")]
    [Arguments("[a]\n= 1\n", "expected 'name = value' or a [section] header")]
    [Arguments("[a]\ns3 =\n  oops\n", "a nested block contains a line that is not 'name = value'")]
    [Arguments("x = 1\n", "content before the first [section] header")]
    public async Task Parse_MalformedFile_MessageStatesWhatIsWrong(string text, string reason)
    {
        // Arrange

        // Act
        var act = () => IniFile.Parse(text, "config");

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining($": {reason}.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Parse_NestedBlockLineStartingWithEquals_ReadsAnEmptyKey()
    {
        // Arrange (the line "= 1" splits at the first character, like the line "a = 1" splits after the "a")
        const string text = "[default]\ns3 =\n  = 1\n";

        // Act
        var sections = IniFile.Parse(text, "config");

        // Assert
        await Assert.That(sections[0].Values["s3"].Block![string.Empty]).IsEqualTo("1");
    }

    [Test]
    public async Task Parse_SectionAndOptionNames_KeepsSectionCaseAndLowerCasesOptions()
    {
        // Arrange
        const string text = "[Dev]\nAWS_Region = eu-west-1\n";

        // Act
        var sections = IniFile.Parse(text, "config");

        // Assert
        await Assert.That($"{sections[0].Name}:{string.Join(',', sections[0].Values.Keys)}").IsEqualTo("Dev:aws_region");
    }

    [Test]
    public async Task Parse_EmptyText_ReturnsNoSections()
    {
        // Arrange

        // Act
        var sections = IniFile.Parse(string.Empty, "config");

        // Assert
        await Assert.That(sections).IsEmpty();
    }

    [Test]
    public async Task Parse_ByteOrderMarkLeftInTheText_IsAnError()
    {
        // Arrange (the default file reader strips the mark; botocore fails on it as well when it is not stripped)
        var text = (char)0xFEFF + "[default]\nregion = eu-west-1\n";

        // Act
        var act = () => IniFile.Parse(text, "config");

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("line 1", StringComparison.Ordinal);
    }
}
