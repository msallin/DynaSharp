using System.Net;
using System.Text.RegularExpressions;
using DynaSharp.Credentials;
using DynaSharp.UnitTests.Properties;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>The encoding of a form value of AWS STS, over text of every plane: only the characters RFC 3986 leaves unreserved stay as they are, and a form decoder reads the value back.</summary>
public sealed partial class StsPropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task Encode_AnyValidText_IsUnreservedOrEncodedAndDecodesBack(string value)
    {
        // Arrange
        // Act
        var encoded = Sts.Encode(value);

        // Assert
        await Assert.That((FormCharacters().IsMatch(encoded), WebUtility.UrlDecode(encoded))).IsEqualTo((true, value));
    }

    [GeneratedRegex(@"\A([A-Za-z0-9\-._~+]|%[0-9A-F]{2})*\z")]
    private static partial Regex FormCharacters();
}
