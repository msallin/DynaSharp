using System.Text;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Properties;
using DynaSharp.UnitTests.Serialization;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests;

/// <summary>
/// Attribute values of every type, nested in lists and maps, through the wire format and back: the reader gives the value the writer was given, the two ways of the writer
/// give the same bytes, and equality and the hash code agree with each other.
/// </summary>
public sealed class AttributeValuePropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task ReadValue_WhatWriteValueWrote_EqualsTheValue(AttributeValue value)
    {
        // Arrange (any type, sets and empty lists and maps included, nested)
        // Act
        var back = TestJson.ReadValue(TestJson.WriteValue(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task GetHashCode_ValueReadBack_IsTheHashOfTheValue(AttributeValue value)
    {
        // Arrange (a value read back shares no array or item with the one written, and a set may come back in another order)
        var back = TestJson.ReadValue(TestJson.WriteValue(value));

        // Act
        var hashes = (back.GetHashCode(), value.GetHashCode());

        // Assert
        await Assert.That(hashes.Item1).IsEqualTo(hashes.Item2);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task Write_DirectToTheBuffer_GivesTheBytesOfTheJsonWriter(AttributeValue value)
    {
        // Arrange
        var viaJson = TestJson.WriteValue(value);

        // Act
        var direct = Direct(value);

        // Assert
        await Assert.That(direct).IsEqualTo(viaJson);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task ToString_AnyValidValue_IsTheWireForm(AttributeValue value)
    {
        // Arrange
        // Act
        var text = value.ToString();

        // Assert
        await Assert.That(text).IsEqualTo(TestJson.WriteValue(value));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task FromJson_WhatToJsonGave_EqualsTheValue(JsonShapedValue value)
    {
        // Arrange (no sets and no binary, which JSON does not have, and no null in a map, which FromJson leaves out)
        // Act
        var back = AttributeValue.FromJson(value.Value.ToJson());

        // Assert
        await Assert.That(back).IsEqualTo(value.Value);
    }

    private static string Direct(AttributeValue value)
    {
        using var buffer = new PooledByteBuffer();
        using var json = WireJson.CreateWriter(buffer);
        AttributeValueConverter.Write(new DynamoDbWriter(json, buffer), value);
        json.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
