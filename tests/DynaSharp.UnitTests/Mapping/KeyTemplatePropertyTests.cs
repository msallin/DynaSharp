using DynaSharp.UnitTests.Properties;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>
/// The generated key of <c>TENANT#{Tenant}#USER#{Id}</c> over values of any text: it formats exactly the values a parse can tell apart again and parses back to them,
/// and it refuses the others (empty, a <c>#</c>, a control character, U+10FFFF) with a <see cref="KeyFormatException"/>, so no key is ever ambiguous.
/// </summary>
public sealed class KeyTemplatePropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task Format_AnyTwoValues_ParsesBackToThemOrRefusesTheOnesAKeyCannotHold(string tenant, string id)
    {
        // Arrange (text of every plane, '#' and control characters included)
        var expected = CanBeSegment(tenant) && CanBeSegment(id) ? $"TENANT#{tenant}#USER#{id} {tenant}|{id}" : nameof(KeyFormatException);

        // Act
        var outcome = FormatAndParse(tenant, id);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task TryParse_AnyText_FormatsBackToTheSameTextIfItParses(string tenant, string id, string tail)
    {
        // Arrange (text that is a key, a key with more behind it, or something close to one)
        var text = $"TENANT#{tenant}#USER#{id}{tail}";

        // Act
        var formatted = Member.Keys.Pk.TryParse(text, out var parts) ? Member.Keys.Pk.Format(parts.Tenant, parts.Id) : text;

        // Assert
        await Assert.That(formatted).IsEqualTo(text);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task Format_ValueWithALoneSurrogate_ThrowsKeyFormatException(LoneSurrogateText tenant)
    {
        // Arrange
        // Act
        var act = () => Member.Keys.Pk.Format(tenant.Value, "1");

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    // What the rules of a segment allow: some text, none of it a delimiter of the template or a control character, and not the scalar reserved for the end of a range.
    private static bool CanBeSegment(string value) =>
        value.Length > 0 && !value.Contains('#', StringComparison.Ordinal) && !value.Any(static character => character < 0x20) && !value.Contains("\U0010FFFF", StringComparison.Ordinal);

    private static string FormatAndParse(string tenant, string id)
    {
        try
        {
            var key = Member.Keys.Pk.Format(tenant, id);
            return Member.Keys.Pk.TryParse(key, out var parts) ? $"{key} {parts.Tenant}|{parts.Id}" : $"{key} does not parse";
        }
        catch (KeyFormatException)
        {
            return nameof(KeyFormatException);
        }
    }
}
