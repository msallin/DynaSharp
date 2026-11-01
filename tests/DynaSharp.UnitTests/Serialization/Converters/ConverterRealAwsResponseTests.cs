using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Operations;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// The converters against attribute values recorded from real DynamoDB (<c>real_aws_responses.json</c>). The recordings hold no
/// number, boolean or null attribute, so those kinds have no real sample; strings, string and binary sets, maps and lists do.
/// </summary>
public sealed class ConverterRealAwsResponseTests
{
    [Test]
    public async Task Read_RealStringAttribute_ReturnsTheString()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_ss_only").Wire, "pk");

        // Act
        var value = ConverterRun.Read<string, StringConverter>(attribute);

        // Assert
        await Assert.That(value).IsEqualTo("esm-put-ss-only");
    }

    [Test]
    public async Task Read_RealStringSetWithEmptyMember_KeepsTheEmptyString()
    {
        // Arrange (real DynamoDB accepts the empty string as a set member)
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_ss_mixed").Wire, "attr");

        // Act
        var value = ConverterRun.Read<HashSet<string>, HashSetConverter<string, StringSetElement>>(attribute);

        // Assert
        await Assert.That(string.Join('|', value.Order(StringComparer.Ordinal))).IsEqualTo("|a");
    }

    [Test]
    public async Task Read_RealStringSetWhoseOnlyMemberIsEmpty_ReturnsOneEmptyString()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_ss_only").Wire, "attr");

        // Act
        var value = ConverterRun.Read<string[], SetArrayConverter<string, StringSetElement>>(attribute);

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual([string.Empty])).IsTrue();
    }

    [Test]
    public async Task Read_RealBinarySetWithZeroLengthMember_KeepsTheEmptyMember()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_bs_mixed").Wire, "attr");

        // Act
        var value = ConverterRun.Read<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>(attribute);

        // Assert
        await Assert.That(string.Join('|', value.Select(static bytes => Convert.ToHexString(bytes)).Order(StringComparer.Ordinal))).IsEqualTo("|01");
    }

    [Test]
    public async Task Read_RealBinarySetWhoseOnlyMemberIsEmpty_ReturnsOneEmptyArray()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_bs_only").Wire, "attr");

        // Act
        var value = ConverterRun.Read<IReadOnlySet<byte[]>, ReadOnlySetConverter<byte[], BinarySetElement>>(attribute);

        // Assert
        await Assert.That(value.Single().Length).IsEqualTo(0);
    }

    [Test]
    public async Task Read_RealStringSetInAMap_ReturnsTheSetByName()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_map_ss").Wire, "outer");

        // Act
        var value = ConverterRun.Read<IReadOnlyDictionary<string, HashSet<string>>, ReadOnlyDictionaryConverter<HashSet<string>, HashSetConverter<string, StringSetElement>>>(attribute);

        // Assert
        await Assert.That(value["inner"].Single()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Read_RealStringSetInAList_ReturnsTheSetAsAnElement()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_list_ss").Wire, "items");

        // Act
        var value = ConverterRun.Read<IReadOnlyList<HashSet<string>>, ReadOnlyListConverter<HashSet<string>, HashSetConverter<string, StringSetElement>>>(attribute);

        // Assert
        await Assert.That((value.Count, value[0].Single())).IsEqualTo((1, string.Empty));
    }

    [Test]
    public async Task Read_RealStringSetOfAQueryResponse_ReturnsTheMembers()
    {
        // Arrange (the same shapes in a query response, where the item is one of an array)
        using var document = JsonDocument.Parse(RealAwsResponses.Get("esm_query_contains_hit").Wire);
        var attribute = document.RootElement.GetProperty("Items")[0].GetProperty("tags").GetRawText();

        // Act
        var value = ConverterRun.Read<HashSet<string>, HashSetConverter<string, StringSetElement>>(attribute);

        // Assert
        await Assert.That(string.Join('|', value.Order(StringComparer.Ordinal))).IsEqualTo("|a");
    }

    [Test]
    public async Task Write_RealListOfMapsAsRead_WritesTheRecordedValue()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("proj_get_a3").Wire, "l");
        var value = ConverterRun.Read<List<Dictionary<string, string>>, ListConverter<Dictionary<string, string>, DictionaryConverter<string, StringConverter>>>(attribute);

        // Act
        var json = ConverterRun.Write<List<Dictionary<string, string>>, ListConverter<Dictionary<string, string>, DictionaryConverter<string, StringConverter>>>(value);

        // Assert (maps have no order: the recording lists y before x in its second map)
        await Assert.That(JsonComparer.Differences(attribute, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Write_RealBinarySetAsRead_WritesTheRecordedMembers()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_bs_mixed").Wire, "attr");
        var value = ConverterRun.Read<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>(attribute);

        // Act
        var json = ConverterRun.Write<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>(value);

        // Assert
        await Assert.That(ConverterRun.Members(json, "BS")).IsEquivalentTo(ConverterRun.Members(attribute, "BS"));
    }

    [Test]
    public async Task Write_RealStringSetAsRead_WritesTheRecordedMembers()
    {
        // Arrange
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("esm_put_ss_mixed").Wire, "attr");
        var value = ConverterRun.Read<HashSet<string>, HashSetConverter<string, StringSetElement>>(attribute);

        // Act
        var json = ConverterRun.Write<HashSet<string>, HashSetConverter<string, StringSetElement>>(value);

        // Assert
        await Assert.That(ConverterRun.Members(json, "SS")).IsEquivalentTo(ConverterRun.Members(attribute, "SS"));
    }
}
