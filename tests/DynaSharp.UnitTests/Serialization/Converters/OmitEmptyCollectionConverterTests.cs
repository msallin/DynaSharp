using System.Collections;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization.Converters;

/// <summary>What the handle of a collection with <see cref="DynaSharp.Mapping.OmitEmptyAttribute"/> has for a converter: the converter of the property, and an empty collection that counts as null.</summary>
public sealed class OmitEmptyCollectionConverterTests
{
    [Test]
    [Arguments(0, true)]
    [Arguments(1, false)]
    public async Task IsOmitted_List_GoesByItsCount(int count, bool expected)
    {
        // Arrange
        var list = Enumerable.Repeat("a", count).ToList();

        // Act
        var omitted = OmitEmptyCollectionConverter<List<string>, string, ListConverter<string, StringConverter>>.IsOmitted(list);

        // Assert
        await Assert.That(omitted).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(1, false)]
    public async Task IsOmitted_Array_GoesByItsLength(int length, bool expected)
    {
        // Arrange
        var array = new string[length];

        // Act
        var omitted = OmitEmptyCollectionConverter<string[], string, ArrayConverter<string, StringConverter>>.IsOmitted(array);

        // Assert
        await Assert.That(omitted).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(1, false)]
    public async Task IsOmitted_ReadOnlyList_GoesByItsCount(int count, bool expected)
    {
        // Arrange
        IReadOnlyList<string> list = new ReadOnlyOnly(count);

        // Act
        var omitted = OmitEmptyCollectionConverter<IReadOnlyList<string>, string, ReadOnlyListConverter<string, StringConverter>>.IsOmitted(list);

        // Assert
        await Assert.That(omitted).IsEqualTo(expected);
    }

    [Test]
    public async Task IsOmitted_ReadOnlyDictionary_TestsKeyAndValuePairs()
    {
        // Arrange
        IReadOnlyDictionary<string, int> empty = new Dictionary<string, int>();

        // Act
        var omitted = OmitEmptyCollectionConverter<IReadOnlyDictionary<string, int>, KeyValuePair<string, int>, ReadOnlyDictionaryConverter<int, NumberConverter<int>>>.IsOmitted(empty);

        // Assert
        await Assert.That(omitted).IsTrue();
    }

    [Test]
    public async Task IsOmitted_SomethingWithoutACount_AsksTheConverter()
    {
        // Arrange (nothing generated has such a shape, and a value that cannot answer is no reason to walk it)
        var sequence = new NoCount();

        // Act
        var omitted = OmitEmptyCollectionConverter<NoCount, string, NoCountConverter>.IsOmitted(sequence);

        // Assert
        await Assert.That(omitted).IsFalse();
    }

    [Test]
    public async Task IsOmitted_Null_AsksTheConverter()
    {
        // Arrange (With by handle passes on what it is given)
        List<string> nothing = null!;

        // Act
        var omitted = OmitEmptyCollectionConverter<List<string>, string, ListConverter<string, StringConverter>>.IsOmitted(nothing);

        // Assert
        await Assert.That(omitted).IsFalse();
    }

    [Test]
    public async Task IsOmitted_ConverterThatOmitsAFullList_LeavesItOut()
    {
        // Arrange
        List<string> list = ["a"];

        // Act
        var omitted = OmitEmptyCollectionConverter<List<string>, string, OmitsEverything>.IsOmitted(list);

        // Assert
        await Assert.That(omitted).IsTrue();
    }

    [Test]
    public async Task Write_Collection_WritesWhatItsConverterWrites()
    {
        // Arrange, Act
        var json = ConverterRun.Write<List<string>, OmitEmptyCollectionConverter<List<string>, string, ListConverter<string, StringConverter>>>(["a"]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"S":"a"}]}""");
    }

    [Test]
    public async Task Read_Collection_ReadsWhatItsConverterReads()
    {
        // Arrange, Act
        var value = ConverterRun.Read<List<string>, OmitEmptyCollectionConverter<List<string>, string, ListConverter<string, StringConverter>>>("""{"L":[{"S":"a"}]}""");

        // Assert
        await Assert.That(value).IsEquivalentTo(["a"]);
    }

    [Test]
    public async Task TryReadMissing_Set_GivesWhatItsConverterGives()
    {
        // Arrange, Act
        var found = OmitEmptyCollectionConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>.TryReadMissing(out var value);

        // Assert
        await Assert.That((found, value.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task TryReadMissing_List_HasNoValue()
    {
        // Arrange, Act
        var found = OmitEmptyCollectionConverter<List<string>, string, ListConverter<string, StringConverter>>.TryReadMissing(out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(1, false)]
    public async Task IsOmitted_Set_GoesByItsCount(int count, bool expected)
    {
        // Arrange
        var set = Enumerable.Repeat("a", count).ToHashSet();

        // Act
        var omitted = OmitEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>.IsOmitted(set);

        // Assert
        await Assert.That(omitted).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_Set_WritesWhatItsConverterWrites()
    {
        // Arrange, Act
        var json = ConverterRun.Write<HashSet<string>, OmitEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>>(["a"]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["a"]}""");
    }

    [Test]
    public async Task Read_Set_ReadsWhatItsConverterReads()
    {
        // Arrange, Act
        var value = ConverterRun.Read<HashSet<string>, OmitEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>>("""{"SS":["a"]}""");

        // Assert
        await Assert.That(value).IsEquivalentTo(["a"]);
    }

    [Test]
    public async Task TryReadMissing_SetWithAddableConverter_GivesWhatItsConverterGives()
    {
        // Arrange, Act
        var found = OmitEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>.TryReadMissing(out var value);

        // Assert
        await Assert.That((found, value.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task TryReadMissing_String_GivesWhatItsConverterGives()
    {
        // Arrange, Act
        var found = OmitEmptyConverter<StringConverter>.TryReadMissing(out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    private readonly struct NoCountConverter : IDynamoDbConverter<NoCount>
    {
        public static void Write(DynamoDbWriter writer, NoCount value) => writer.WriteNull();

        public static NoCount Read(ref DynamoDbReader reader, AttributeType type) => new();
    }

    private readonly struct OmitsEverything : IDynamoDbConverter<List<string>>
    {
        public static void Write(DynamoDbWriter writer, List<string> value) => writer.WriteNull();

        public static List<string> Read(ref DynamoDbReader reader, AttributeType type) => [];

        public static bool IsOmitted(List<string> value) => true;
    }

    /// <summary>A list that has the one interface a <c>List</c> has beside the writable ones.</summary>
    private sealed class ReadOnlyOnly(int count) : IReadOnlyList<string>
    {
        public int Count => count;

        public string this[int index] => "a";

        public IEnumerator<string> GetEnumerator() => Enumerable.Repeat("a", count).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A sequence that holds no count.</summary>
    private sealed class NoCount : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
