namespace DynaSharp.UnitTests.Operations;

public sealed class JsonComparerTests
{
    [Test]
    public async Task Differences_SameDocumentWithPropertiesInAnotherOrder_IsEmpty()
    {
        // Arrange
        const string expected = """{"a":1,"b":{"c":[1,2],"d":"x"}}""";
        const string actual = """{"b":{"d":"x","c":[1,2]},"a":1}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Differences_NumbersWithAnotherSpelling_IsEmpty()
    {
        // Arrange
        const string expected = """{"a":1.0,"b":2.5E1}""";
        const string actual = """{"a":1,"b":25}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Differences_NullTrueAndFalse_AreComparedByKind()
    {
        // Arrange
        const string expected = """[null,true,false]""";
        const string actual = """[null,true,false]""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Differences_DifferentString_NamesThePath()
    {
        // Arrange
        const string expected = """{"a":{"b":["x","y"]}}""";
        const string actual = """{"a":{"b":["x","z"]}}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("""$.a.b[1]: expected "y" but found "z".""");
    }

    [Test]
    public async Task Differences_DifferentNumber_NamesThePath()
    {
        // Arrange
        const string expected = """{"a":1}""";
        const string actual = """{"a":2}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("$.a: expected 1 but found 2.");
    }

    [Test]
    public async Task Differences_TrueAgainstFalse_NamesThePath()
    {
        // Arrange
        const string expected = """{"a":true}""";
        const string actual = """{"a":false}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("$.a: expected true but found false.");
    }

    [Test]
    public async Task Differences_MissingAndExtraProperties_ListsBothSets()
    {
        // Arrange
        const string expected = """{"a":1,"b":2}""";
        const string actual = """{"a":1,"c":2}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("$: expected the properties [a, b] but found [a, c].");
    }

    [Test]
    public async Task Differences_ArraysOfDifferentLength_ReportsTheLengths()
    {
        // Arrange
        const string expected = """{"a":[1,2]}""";
        const string actual = """{"a":[1]}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("$.a: expected 2 elements but found 1.");
    }

    [Test]
    public async Task Differences_ArraysInAnotherOrder_AreDifferent()
    {
        // Arrange
        const string expected = """[1,2]""";
        const string actual = """[2,1]""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("$[0]: expected 1 but found 2.");
    }

    [Test]
    public async Task Differences_ObjectAgainstString_ReportsTheKinds()
    {
        // Arrange
        const string expected = """{"a":{}}""";
        const string actual = """{"a":"{}"}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences).IsEqualTo("""$.a: expected {} but found "{}".""");
    }

    [Test]
    public async Task Differences_LongValue_IsShortenedInTheMessage()
    {
        // Arrange
        var expected = "{\"a\":\"" + new string('x', 300) + "\"}";
        const string actual = """{"a":"y"}""";

        // Act
        var differences = JsonComparer.Differences(expected, actual);

        // Assert
        await Assert.That(differences.Length).IsLessThan(200);
    }
}
