using System.Text;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class ItemPageTests
{
    [Test]
    public async Task Read_EmptyObject_IsAnEmptyLastPage()
    {
        // Arrange
        const string json = "{}";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount, page.LastEvaluatedKey, page.ConsumedCapacity))
            .IsEqualTo((0, 0, 0, (IReadOnlyDictionary<string, AttributeValue>?)null, (ConsumedCapacity?)null));
    }

    [Test]
    public async Task Read_Items_ReturnsEveryItemWithItsAttributes()
    {
        // Arrange
        const string json = """{"Items":[{"pk":{"S":"a"},"n":{"N":"1"}},{"pk":{"S":"b"}}],"Count":2,"ScannedCount":2}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That((page.Items[0]["n"].AsNumber<int>(), page.Items[1]["pk"].AsString())).IsEqualTo((1, "b"));
    }

    [Test]
    public async Task Read_NoItems_ReturnsAnEmptyList()
    {
        // Arrange (a COUNT query leaves the items out, an empty page sends an empty array)
        const string json = """{"Items":[],"Count":0,"ScannedCount":50}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That(page.Items).IsEmpty();
    }

    [Test]
    public async Task Read_CountAndScannedCount_AreReadSeparately()
    {
        // Arrange
        const string json = """{"Count":3,"ScannedCount":12}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That((page.Count, page.ScannedCount)).IsEqualTo((3, 12));
    }

    [Test]
    public async Task Read_LastEvaluatedKey_ReturnsTheKey()
    {
        // Arrange
        const string json = """{"Count":0,"Items":[],"LastEvaluatedKey":{"pk":{"S":"a"},"sk":{"N":"7"}},"ScannedCount":100}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That((page.LastEvaluatedKey!["pk"].AsString(), page.LastEvaluatedKey["sk"].AsNumber<int>())).IsEqualTo(("a", 7));
    }

    [Test]
    public async Task Read_EmptyLastEvaluatedKey_IsNullSoThatNullIsTheOnlyEndOfTheResult()
    {
        // Arrange
        const string json = """{"Count":0,"LastEvaluatedKey":{},"ScannedCount":0}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That(page.LastEvaluatedKey).IsNull();
    }

    [Test]
    public async Task Read_ConsumedCapacity_ReturnsIt()
    {
        // Arrange
        const string json = """{"ConsumedCapacity":{"TableName":"Users","CapacityUnits":0.5}}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That(page.ConsumedCapacity).IsEqualTo(new ConsumedCapacity { TableName = "Users", CapacityUnits = 0.5 });
    }

    [Test]
    public async Task Read_PropertiesInAnyOrder_ReturnsTheSamePage()
    {
        // Arrange (the order of the properties is not specified, so the items cannot be presized from Count)
        const string inOrder = """{"Items":[{"pk":{"S":"a"}}],"Count":1,"ScannedCount":4,"LastEvaluatedKey":{"pk":{"S":"a"}},"ConsumedCapacity":{"CapacityUnits":1.0}}""";
        const string reversed = """{"ConsumedCapacity":{"CapacityUnits":1.0},"LastEvaluatedKey":{"pk":{"S":"a"}},"ScannedCount":4,"Count":1,"Items":[{"pk":{"S":"a"}}]}""";

        // Act
        var first = Read(inOrder);
        var second = Read(reversed);

        // Assert
        await Assert.That(ResponseJson.Describe(Page(second))).IsEqualTo(ResponseJson.Describe(Page(first)));
    }

    [Test]
    public async Task Read_UnknownProperties_AreSkipped()
    {
        // Arrange
        const string json = """{"Future":{"a":[1,{"b":null}]},"Items":[{"pk":{"S":"a"}}],"Other":"x","More":[1,2],"Flag":true,"Count":1}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That((page.Items.Count, page.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task Read_ManyItems_ReturnsAllOfThemInOrder()
    {
        // Arrange (more than the initial capacity of the builder, to make it grow more than once)
        var json = new StringBuilder("""{"Items":[""");
        for (var i = 0; i < 1000; i++)
        {
            json.Append(i == 0 ? string.Empty : ",").Append("""{"pk":{"N":""").Append('"').Append(i).Append("\"}}");
        }

        json.Append("""],"Count":1000}""");

        // Act
        var page = Read(json.ToString());

        // Assert
        await Assert.That((page.Items.Count, page.Items[0]["pk"].AsNumber<int>(), page.Items[999]["pk"].AsNumber<int>())).IsEqualTo((1000, 0, 999));
    }

    [Test]
    public async Task Read_Items_AreNotAnArrayOrListThatTheCallerCouldChange()
    {
        // Arrange
        const string json = """{"Items":[{"pk":{"S":"a"}}]}""";

        // Act
        var page = Read(json);

        // Assert
        await Assert.That(page.Items is Array or List<IReadOnlyDictionary<string, AttributeValue>> or List<Dictionary<string, AttributeValue>>).IsFalse();
    }

    [Test]
    public async Task Read_ItemsAreNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Items":{"pk":{"S":"a"}}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("Expected a JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_ItemIsNotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Items":[{"pk":{"S":"a"}},"text"]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("Expected the start of an item", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_AttributeWithUnknownTypeDescriptor_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Items":[{"pk":{"X":"a"}}]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("Unknown DynamoDB type descriptor", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_AttributeNameTwiceInAnItem_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Items":[{"pk":{"S":"a"},"pk":{"S":"b"}}]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("occurs more than once", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_TruncatedInTheMiddleOfTheItems_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Items":[{"pk":{"S":"a"}},{"pk":""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task Read_CountIsNotANumber_ThrowsInvalidOperationException()
    {
        // Arrange
        const string json = """{"Count":"many"}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments("""{"Count":1.5}""")]
    [Arguments("""{"ScannedCount":2147483648}""")]
    public async Task Read_CountOutsideInt32_ThrowsFormatException(string json)
    {
        // Arrange (see json)

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task Read_NotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "[]";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    private static QueryResponse Page(ItemPage page) => new()
    {
        Items = page.Items,
        Count = page.Count,
        ScannedCount = page.ScannedCount,
        LastEvaluatedKey = page.LastEvaluatedKey,
        ConsumedCapacity = page.ConsumedCapacity,
    };

    private static ItemPage Read(string json) => TestJson.Read(json, static (ref reader) => ItemPage.Read(ref reader));
}
