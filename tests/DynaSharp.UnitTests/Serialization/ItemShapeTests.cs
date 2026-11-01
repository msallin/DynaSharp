using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ItemShapeTests
{
    private const string Backslash = "\\";

    [Test]
    public async Task HashNames_SameNamesInAnotherOrder_IsEqual()
    {
        // Act
        var hashes = (ItemShape.HashNames(["pk", "sk", "total"]), ItemShape.HashNames(["total", "pk", "sk"]));

        // Assert
        await Assert.That(hashes.Item1).IsEqualTo(hashes.Item2);
    }

    [Test]
    public async Task HashNames_NamesDifferingInTheMiddle_Differs()
    {
        // Act
        var hashes = (ItemShape.HashNames(["gsi1pk"]), ItemShape.HashNames(["gsi2pk"]));

        // Assert
        await Assert.That(hashes.Item1).IsNotEqualTo(hashes.Item2);
    }

    [Test]
    [Arguments(2)]
    [Arguments(32)]
    [Arguments(33)]
    [Arguments(400)]
    public async Task CreateShared_LastNameDuplicatesTheFirst_ThrowsJsonExceptionNamingIt(int count)
    {
        // Arrange (below and above the point where the check switches to a hash set)
        var names = Enumerable.Range(0, count).Select(static i => $"name{i}").ToArray();
        names[^1] = names[0];

        // Act
        var act = () => ItemShape.CreateShared(names, 0);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The attribute name \"name0\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    public async Task CreateShared_Names_CopiesThem()
    {
        // Arrange (the caller's array is a pooled scratch array)
        string[] names = ["a", "b"];

        // Act
        var shape = ItemShape.CreateShared(names, 0);
        names[0] = "changed";

        // Assert
        await Assert.That(shape.Names[0]).IsEqualTo("a");
    }

    [Test]
    public async Task RetainedBytes_Names_CountsCharactersAndOverhead()
    {
        // Arrange
        var shape = ItemShape.CreateShared(["ab", "c"], 0);

        // Act
        var bytes = shape.RetainedBytes;

        // Assert (three characters of two bytes, and two names of 64 bytes)
        await Assert.That(bytes).IsEqualTo(134);
    }

    [Test]
    [Arguments(8)]
    [Arguments(9)]
    public async Task Find_SharedShape_FindsEveryNameAcrossTheIndexLimit(int count)
    {
        // Arrange
        var names = Enumerable.Range(0, count).Select(static i => $"name{i}").ToArray();
        var shape = ItemShape.CreateShared(names, 0);

        // Act
        var positions = names.Select(name => shape.Find(name, count)).ToArray();

        // Assert
        await Assert.That((positions.SequenceEqual(Enumerable.Range(0, count)), shape.Find("missing", count))).IsEqualTo((true, -1));
    }

    [Test]
    [Arguments(32)]
    [Arguments(33)]
    public async Task Find_PrivateShape_FindsEveryNameAcrossTheIndexLimit(int count)
    {
        // Arrange
        var shape = ItemShape.CreatePrivate(count + 1);
        for (var i = 0; i < count; i++)
        {
            shape.Append($"name{i}", i);
        }

        // Act
        var found = Enumerable.Range(0, count).All(i => shape.Find($"name{i}", count) == i);

        // Assert
        await Assert.That((found, shape.Find("missing", count))).IsEqualTo((true, -1));
    }

    [Test]
    public async Task Append_AfterTheIndexExists_GoesIntoTheIndex()
    {
        // Arrange (a lookup at 33 names has built the index)
        var shape = ItemShape.CreatePrivate(34);
        for (var i = 0; i < 33; i++)
        {
            shape.Append($"name{i}", i);
        }

        shape.Find("name0", 33);

        // Act
        shape.Append("last", 33);

        // Assert
        await Assert.That(shape.Find("last", 34)).IsEqualTo(33);
    }

    [Test]
    public async Task CopyToPrivate_SharedShape_KeepsTheNamesInALongerArrayOfItsOwn()
    {
        // Arrange
        var shared = ItemShape.CreateShared(["a", "b"], 0);

        // Act
        var copy = shared.CopyToPrivate(2, 8);

        // Assert
        await Assert.That((copy.Names.Length, copy.Names[1], copy.IsShared, shared.IsShared, ReferenceEquals(copy.Names, shared.Names))).IsEqualTo((8, "b", false, true, false));
    }

    [Test]
    public async Task TryOrder_SameNamesInOrder_CopiesTheValues()
    {
        // Arrange
        var shape = ItemShape.CreateShared(["a", "b"], 0);
        var ordered = new AttributeValue[2];

        // Act
        var result = shape.TryOrder(["a", "b"], [1, 2], ordered);

        // Assert
        await Assert.That((result, ordered[0].AsNumber(), ordered[1].AsNumber())).IsEqualTo((true, "1", "2"));
    }

    [Test]
    public async Task TryOrder_SameNamesInAnotherOrder_PutsTheValuesInTheOrderOfTheShape()
    {
        // Arrange
        var shape = ItemShape.CreateShared(["a", "b", "c"], 0);
        var ordered = new AttributeValue[3];

        // Act
        var result = shape.TryOrder(["c", "a", "b"], [3, 1, 2], ordered);

        // Assert
        await Assert.That((result, ordered[0].AsNumber(), ordered[1].AsNumber(), ordered[2].AsNumber())).IsEqualTo((true, "1", "2", "3"));
    }

    [Test]
    public async Task TryOrder_OtherCount_ReturnsFalse()
    {
        // Arrange
        var shape = ItemShape.CreateShared(["a", "b"], 0);
        var ordered = new AttributeValue[1];

        // Act
        var result = shape.TryOrder(["a"], [1], ordered);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryOrder_NameTheShapeDoesNotHave_ReturnsFalseAndLeavesNoValueBehind()
    {
        // Arrange (the next candidate gets the same array)
        var shape = ItemShape.CreateShared(["a", "b"], 0);
        var ordered = new AttributeValue[2];

        // Act
        var result = shape.TryOrder(["a", "x"], [1, 2], ordered);

        // Assert
        await Assert.That((result, ordered[0].Type, ordered[1].Type)).IsEqualTo((false, AttributeType.None, AttributeType.None));
    }

    [Test]
    [Arguments("a,a,c")]
    [Arguments("c,a,c")]
    public async Task TryOrder_NameTwiceWhichHidesAMissingOne_ReturnsFalse(string names)
    {
        // Arrange (a hash collision between the names of an item and a shape must not let a duplicate through: the counts are equal, and every name is found)
        ArgumentNullException.ThrowIfNull(names);
        var shape = ItemShape.CreateShared(["a", "b", "c"], 0);
        var ordered = new AttributeValue[3];

        // Act
        var result = shape.TryOrder(names.Split(','), [1, 2, 3], ordered);

        // Assert
        await Assert.That((result, ordered[0].Type, ordered[2].Type)).IsEqualTo((false, AttributeType.None, AttributeType.None));
    }

    [Test]
    [Arguments(63)]
    [Arguments(64)]
    [Arguments(65)]
    [Arguments(400)]
    public async Task TryOrder_LargeItemWithANameTwice_ReturnsFalse(int count)
    {
        // Arrange (the last name replaces another in reversed order, so that no position is in order)
        var names = Enumerable.Range(0, count).Select(static i => $"name{i}").ToArray();
        var shape = ItemShape.CreateShared(names, 0);
        var read = names.AsEnumerable().Reverse().ToArray();
        read[^1] = read[0];
        var values = Enumerable.Range(0, count).Select(static i => (AttributeValue)i).ToArray();

        // Act
        var result = shape.TryOrder(read, values, new AttributeValue[count]);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    [Arguments("pk", "pk", true)]
    [Arguments("pk", "sk", false)]
    [Arguments("pk", "PK", false)]
    [Arguments("pk", "pk2", false)]
    [Arguments("Größe", "Größe", true)]
    [Arguments("名前", "名前", true)]
    public async Task IsNameAt_Names_ComparesTheText(string stored, string read, bool expected)
    {
        // Arrange
        var shape = ItemShape.CreateShared([stored], 0);

        // Act
        var result = IsNameAt(shape, $"\"{read}\"");

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task IsNameAt_EscapedSpellingOfTheName_IsTrue()
    {
        // Arrange
        var shape = ItemShape.CreateShared(["ab"], 0);

        // Act
        var result = IsNameAt(shape, "\"a" + Backslash + "u0062\"");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsNameAt_RawBytesThatSpellTheNameButEscapeSomethingElse_IsFalse()
    {
        // Arrange (the stored name has two backslashes, the JSON text has them too, but that is the escape of one)
        var shape = ItemShape.CreateShared(["a" + Backslash + Backslash + "b"], 0);

        // Act
        var result = IsNameAt(shape, "\"a" + Backslash + Backslash + "b\"");

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Find_ManyThreadsOnALargeSharedShape_AllSeeTheIndex()
    {
        // Arrange (the index of a shared shape is built on first use, by whichever threads get there)
        var names = Enumerable.Range(0, 200).Select(static i => $"name{i}").ToArray();
        var shape = ItemShape.CreateShared(names, 0);
        var failures = 0;
        var threads = Math.Max(Environment.ProcessorCount, 4);
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < names.Length; i++)
            {
                if (shape.Find(names[i], names.Length) != i)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        })).ToArray();

        // Act
        Array.ForEach(workers, static worker => worker.Start());
        Array.ForEach(workers, static worker => worker.Join());

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    private static bool IsNameAt(ItemShape shape, string quotedName)
    {
        var json = Encoding.UTF8.GetBytes("{" + quotedName + ":1}");
        var reader = new Utf8JsonReader(json);
        reader.Read();
        reader.Read();
        return shape.IsNameAt(ref reader, 0);
    }
}
