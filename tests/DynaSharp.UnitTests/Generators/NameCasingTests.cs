using System.Text;
using System.Text.Json;
using DynaSharp.Generators;
using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The conversions held against <see cref="JsonNamingPolicy"/>, which is what they promise to be, on vectors of the shape of a property name and on many that are not.</summary>
public sealed class NameCasingTests
{
    private const string Alphabet = "AaBbZz019_- $.ÄäÖöİıßΩω中\uD83D\u01C5\u02B0\u0301\u0903\u20DD\u2167\u00B2";

    public static IEnumerable<string> Names() =>
    [
        string.Empty, "A", "a", "Id", "ID", "id", "OrderId", "orderId", "OrderID", "IsCIAAgent", "IsOK", "URL", "URLPath", "XMLHttpRequest", "SHA512Hash", "Line2Total", "Line2", "A1", "A1B2", "AB1C",
        "_Private", "_private", "__Double", "Snake_Case", "UPPER_CASE", "Has_Under_Score", "Ünder", "ÄÖÜ", "ÄbC", "Straße", "Ωmega", "İstanbul", "Hello World", "Hello  World", " Leading", "Trailing ", "   ", "URL Value", "A B", "Ab Cd",
        "PascalCase", "camelCase", "kebab-case", "With.Dot", "With$Dollar", "X", "XY", "XYz", "xY", "x1Y", "ThisIs1Name", "ThisIsA1", "Version2Beta", "Http2Server", "HTTP2Server",
    ];

    [Test]
    [MethodDataSource(nameof(Names))]
    public async Task ToCamelCase_Name_IsWhatTheJsonPolicyGives(string name)
    {
        // Arrange
        var expected = JsonNamingPolicy.CamelCase.ConvertName(name);

        // Act
        var converted = NameCasing.ToCamelCase(name);

        // Assert
        await Assert.That(converted).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Names))]
    public async Task ToSnakeCase_Name_IsWhatTheJsonPolicyGives(string name)
    {
        // Arrange
        var expected = JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

        // Act
        var converted = NameCasing.ToSnakeCase(name);

        // Assert
        await Assert.That(converted).IsEqualTo(expected);
    }

    [Test]
    public async Task ToCamelCase_ManyStrings_IsWhatTheJsonPolicyGives()
    {
        // Arrange (a fixed sequence, so a failure can be repeated)
        var strings = RandomStrings();

        // Act
        var different = strings.Where(static text => NameCasing.ToCamelCase(text) != JsonNamingPolicy.CamelCase.ConvertName(text)).ToList();

        // Assert
        await Assert.That(different).IsEmpty();
    }

    [Test]
    public async Task ToSnakeCase_ManyStrings_IsWhatTheJsonPolicyGives()
    {
        // Arrange (a fixed sequence, so a failure can be repeated)
        var strings = RandomStrings();

        // Act
        var different = strings.Where(static text => NameCasing.ToSnakeCase(text) != JsonNamingPolicy.SnakeCaseLower.ConvertName(text)).ToList();

        // Assert
        await Assert.That(different).IsEmpty();
    }

    [Test]
    public async Task NamingPolicy_EveryName_HasTheValueOfThePublicEnum()
    {
        // Arrange
        var expected = Enum.GetValues<AttributeNaming>().Select(static value => (value.ToString(), (int)value));

        // Act
        var policies = Enum.GetValues<NamingPolicy>().Select(static value => (value.ToString(), (int)value));

        // Assert
        await Assert.That(policies).IsEquivalentTo(expected);
    }

    private static List<string> RandomStrings()
    {
        var state = 20261102u;
        var strings = new List<string>();
        for (var count = 0; count < 20000; count++)
        {
            var text = new StringBuilder();
            for (var length = Next(12); length > 0; length--)
            {
                text.Append(Alphabet[Next(Alphabet.Length)]);
            }

            strings.Add(text.ToString());
        }

        return strings;

        int Next(int limit)
        {
            state = (state * 1664525) + 1013904223;
            return (int)((state >> 8) % (uint)limit);
        }
    }
}
