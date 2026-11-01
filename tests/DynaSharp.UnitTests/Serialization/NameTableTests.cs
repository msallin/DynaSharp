using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class NameTableTests
{
    // Written out of parts, so that no tool on the way turns the escape into the character.
    private const string Escape = "\\" + "u";
    private const string EscapedAB = "a" + Escape + "0062";

    [Test]
    public async Task GetName_NameSeenBefore_ReturnsTheSameInstance()
    {
        // Arrange
        var table = new NameTable(32);
        var first = GetNames(table, "pk");

        // Act
        var second = GetNames(table, "pk");

        // Assert
        await Assert.That(ReferenceEquals(first[0], second[0])).IsTrue();
    }

    [Test]
    public async Task GetName_NewName_ReturnsTheText()
    {
        // Arrange
        var table = new NameTable(32);

        // Act
        var names = GetNames(table, "pk", "sk", "orderId");

        // Assert
        await Assert.That(string.Join(',', names)).IsEqualTo("pk,sk,orderId");
    }

    [Test]
    public async Task GetName_EmptyName_ReturnsTheEmptyString()
    {
        // Arrange
        var table = new NameTable(32);

        // Act
        var names = GetNames(table, string.Empty, string.Empty);

        // Assert
        await Assert.That((names[0], names[1])).IsEqualTo((string.Empty, string.Empty));
    }

    [Test]
    public async Task GetName_NamesInAnotherOrder_ReturnsTheInstancesOfTheFirstPass()
    {
        // Arrange
        var table = new NameTable(32);
        var first = GetNames(table, "a", "b", "c");

        // Act
        var second = GetNames(table, "c", "a", "b");

        // Assert
        await Assert.That((ReferenceEquals(first[2], second[0]), ReferenceEquals(first[0], second[1]), ReferenceEquals(first[1], second[2]))).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task GetName_EscapedName_ReturnsTheUnescapedText()
    {
        // Arrange
        var table = new NameTable(32);

        // Act
        var names = GetNames(table, EscapedAB, EscapedAB);

        // Assert
        await Assert.That((names[0], names[1])).IsEqualTo(("ab", "ab"));
    }

    [Test]
    public async Task GetName_EscapedSpellingOfACachedName_ReturnsTheCachedInstance()
    {
        // Arrange (the plain spelling is cached first, the escaped one has other raw bytes but the same text)
        var table = new NameTable(32);
        var plain = GetNames(table, "ab");

        // Act
        var escaped = GetNames(table, EscapedAB);

        // Assert
        await Assert.That(ReferenceEquals(plain[0], escaped[0])).IsTrue();
    }

    [Test]
    public async Task GetName_PlainSpellingAfterAnEscapedOne_ReturnsTheCachedInstance()
    {
        // Arrange
        var table = new NameTable(32);
        var escaped = GetNames(table, EscapedAB);

        // Act
        var plain = GetNames(table, "ab");

        // Assert
        await Assert.That(ReferenceEquals(escaped[0], plain[0])).IsTrue();
    }

    [Test]
    public async Task GetName_EscapeThatEqualsTheRawBytesOfACachedBackslashName_ReturnsTheDecodedName()
    {
        // Arrange (the raw bytes n\ba are the letters n and a around a backspace; the name "n\ba" with a backslash is written n\\ba)
        var table = new NameTable(32);
        var backslashName = GetNames(table, @"n\\ba", @"n\\ba");

        // Act
        var backspaceName = GetNames(table, @"n\ba");

        // Assert
        await Assert.That((backslashName[0], backslashName[1], backspaceName[0])).IsEqualTo(("n\\ba", "n\\ba", "n\ba"));
    }

    [Test]
    public async Task GetName_NonAsciiName_ReturnsTheSameInstanceEachTime()
    {
        // Arrange
        var table = new NameTable(32);

        // Act
        var names = GetNames(table, "Größe", "名前", "Größe", "名前");

        // Assert
        await Assert.That((ReferenceEquals(names[0], names[2]), ReferenceEquals(names[1], names[3]), string.Join(',', names))).IsEqualTo((true, true, "Größe,名前,Größe,名前"));
    }

    [Test]
    public async Task GetName_NamesDifferingOnlyInCase_ReturnsBoth()
    {
        // Arrange
        var table = new NameTable(32);

        // Act
        var names = GetNames(table, "Größe", "größe");

        // Assert
        await Assert.That(string.Join(',', names)).IsEqualTo("Größe,größe");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(64)]
    public async Task GetName_NameUpToTheCachedLength_ReturnsTheSameInstanceEachTime(int length)
    {
        // Arrange
        var table = new NameTable(32);
        var name = new string('n', length);

        // Act
        var names = GetNames(table, name, name);

        // Assert
        await Assert.That((names[0], ReferenceEquals(names[0], names[1]))).IsEqualTo((name, true));
    }

    [Test]
    [Arguments(65)]
    [Arguments(65536)]
    public async Task GetName_NameBeyondTheCachedLength_IsReadButNotRetained(int length)
    {
        // Arrange (names up to 64 KB are valid, and a table must not keep them alive)
        var table = new NameTable(32);
        var name = new string('n', length);

        // Act
        var names = GetNames(table, name, name);

        // Assert
        await Assert.That((names[0], names[1], ReferenceEquals(names[0], names[1]))).IsEqualTo((name, name, false));
    }

    [Test]
    public async Task GetName_EscapesMakeTheRawNameTooLong_IsReadButNotRetained()
    {
        // Arrange (20 characters of 6 raw bytes each)
        var table = new NameTable(32);
        var escaped = string.Concat(Enumerable.Repeat(Escape + "0061", 20));

        // Act
        var names = GetNames(table, escaped, escaped);

        // Assert
        await Assert.That((names[0], ReferenceEquals(names[0], names[1]))).IsEqualTo((new string('a', 20), false));
    }

    [Test]
    public async Task GetName_FullTable_ReplacesTheNameAtTheHomeSlotAndKeepsTheOthers()
    {
        // Arrange (four slots, which every name may use, so the fifth name replaces exactly one of the first four)
        var table = new NameTable(NameTable.Probes);
        var firstPass = GetNames(table, "a", "b", "c", "d");
        GetNames(table, "e");

        // Act
        var secondPass = GetNames(table, "a", "b", "c", "d");

        // Assert
        await Assert.That(firstPass.Zip(secondPass, ReferenceEquals).Count(static same => same)).IsEqualTo(3);
    }

    [Test]
    public async Task GetName_MoreDistinctNamesThanSlots_ReturnsEveryNameCorrectly()
    {
        // Arrange
        var table = new NameTable(32);
        var expected = Enumerable.Range(0, 500).Select(i => $"attribute{i}").ToArray();

        // Act
        var firstPass = GetNames(table, expected);
        var secondPass = GetNames(table, expected);

        // Assert
        await Assert.That((firstPass.SequenceEqual(expected), secondPass.SequenceEqual(expected))).IsEqualTo((true, true));
    }

    [Test]
    public async Task GetName_AttackerChosenNames_RetainsNoMoreThanTheSlots()
    {
        // Arrange (a stored item can carry any names; retained are the names that come back as the same instance)
        const int slots = 64;
        var table = new NameTable(slots);
        var names = Enumerable.Range(0, 20_000).Select(i => $"attacker{i}").ToArray();
        var firstPass = GetNames(table, names);

        // Act
        var secondPass = GetNames(table, names);

        // Assert (a table that kept everything would hand out the 20000 strings of the first pass again)
        await Assert.That(firstPass.Zip(secondPass, ReferenceEquals).Count(static same => same)).IsLessThanOrEqualTo(slots);
    }

    [Test]
    public async Task Hash_SameTextInBothEncodings_IsEqual()
    {
        // Arrange (shapes hash the decoded text, so the hash must not depend on how the name arrived)
        const string name = "Größe";
        var table = new NameTable(32);

        // Act
        var decoded = GetNames(table, "Gr" + Escape + "00f6" + Escape + "00df" + "e")[0];

        // Assert
        await Assert.That(NameTable.Hash(decoded)).IsEqualTo(NameTable.Hash(name));
    }

    // Reads each name as the property name of a JSON object, the way the table sees names in a response. The names are the raw JSON
    // text between the quotes, so escapes are written out.
    private static string[] GetNames(NameTable table, params string[] rawNames)
    {
        var json = Encoding.UTF8.GetBytes("{" + string.Join(',', rawNames.Select(name => $"\"{name}\":1")) + "}");
        var reader = new Utf8JsonReader(json);
        var names = new List<string>();
        reader.Read();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            names.Add(table.GetName(ref reader));
            reader.Read();
        }

        return [.. names];
    }
}
