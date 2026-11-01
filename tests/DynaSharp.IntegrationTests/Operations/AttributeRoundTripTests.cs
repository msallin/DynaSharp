using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>
/// Every attribute type written with PutItem and read back with GetItem, including the edges of what DynamoDB accepts: empty
/// strings and binary, set members, number limits and the nesting limit. The expected values are what DynamoDB documents.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class AttributeRoundTripTests(Backend backend)
{
    // DynamoDB keeps at most 38 significant digits and normalizes numbers: 9.99...E+125 comes back as digits and zeros, 1E-130 as a
    // plain decimal. The nines and zeros below are those normalized forms.
    private static readonly string LargestMagnitude = new string('9', 38) + new string('0', 88);
    private static readonly string SmallestMagnitude = "0." + new string('0', 129) + "1";

    [Test]
    public async Task PutItem_String_RoundTrips()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, "Alice");

        // Assert
        await Assert.That(value).IsEqualTo(AttributeValue.FromString("Alice"));
    }

    [Test]
    public async Task PutItem_StringWithJsonEscapesAndSurrogatePairs_RoundTrips()
    {
        // Arrange: quotes, backslash, line breaks, a tab, non-ASCII text and a character outside the basic plane
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        const string text = "\"quoted\" back\\slash\r\nline\ttab äöü € \U0001D11E \U0001F600";

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, text);

        // Assert
        await Assert.That(value.AsString()).IsEqualTo(text);
    }

    [Test]
    public async Task PutItem_EmptyString_RoundTrips()
    {
        // Arrange (DynamoDB accepts empty strings in non-key attributes)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, string.Empty);

        // Assert
        await Assert.That(value.AsString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task PutItem_LargeString_RoundTrips()
    {
        // Arrange: 300 KB, below the 400 KB item limit
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var text = new string('x', 300 * 1024);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, text);

        // Assert
        await Assert.That(value.AsString()).IsEqualTo(text);
    }

    [Test]
    public async Task PutItem_ItemOver400KB_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var text = new string('x', 401 * 1024);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, text));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    [Arguments("0")]
    [Arguments("1")]
    [Arguments("-1")]
    [Arguments("123.456")]
    [Arguments("-0.001")]
    [Arguments("12345678901234567890123456789012345678")]
    [Arguments("0.12345678901234567890123456789012345678")]
    public async Task PutItem_Number_RoundTripsInCanonicalForm(string number)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(number));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo(number);
    }

    [Test]
    public async Task PutItem_LargestPositiveNumber_RoundTripsAsDigits()
    {
        // Arrange: 9.9999999999999999999999999999999999999E+125, the largest magnitude DynamoDB stores
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber("9.9999999999999999999999999999999999999E+125"));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo(LargestMagnitude);
    }

    [Test]
    public async Task PutItem_LargestNegativeNumber_RoundTripsAsDigits()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber("-9.9999999999999999999999999999999999999E+125"));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("-" + LargestMagnitude);
    }

    [Test]
    public async Task PutItem_SmallestPositiveNumber_RoundTripsAsDecimal()
    {
        // Arrange: 1E-130, the smallest magnitude DynamoDB stores
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber("1E-130"));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo(SmallestMagnitude);
    }

    [Test]
    public async Task PutItem_SmallestNegativeNumber_RoundTripsAsDecimal()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber("-1E-130"));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("-" + SmallestMagnitude);
    }

    [Test]
    [Arguments("1E+126")]
    [Arguments("-1E+126")]
    public async Task PutItem_NumberAboveTheLimit_ThrowsValidationException(string number)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(number)));

        // Assert
        await Assert.That(failure.Message).Contains("overflow");
    }

    [Test]
    [Arguments("1E-131")]
    [Arguments("-1E-131")]
    public async Task PutItem_NumberBelowTheLimit_ThrowsValidationException(string number)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(number)));

        // Assert
        await Assert.That(failure.Message).Contains("underflow");
    }

    [Test]
    public async Task PutItem_NumberWith39Digits_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(
            () => Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber("123456789012345678901234567890123456789")));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task PutItem_Binary_RoundTripsEveryByteValue()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var bytes = Enumerable.Range(0, 256).Select(static i => (byte)i).ToArray();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromBinary(bytes));

        // Assert
        await Assert.That(value.AsBinary().ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task PutItem_EmptyBinary_RoundTrips()
    {
        // Arrange (DynamoDB accepts empty binary values in non-key attributes)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromBinary([]));

        // Assert
        await Assert.That((value.Type, value.AsBinary().Length)).IsEqualTo((AttributeType.Binary, 0));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PutItem_Bool_RoundTrips(bool flag)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromBool(flag));

        // Assert
        await Assert.That(value.AsBool()).IsEqualTo(flag);
    }

    [Test]
    public async Task PutItem_Null_RoundTrips()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.Null);

        // Assert
        await Assert.That(value.IsNull).IsTrue();
    }

    [Test]
    public async Task PutItem_ListOfMixedTypes_RoundTripsInOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var list = AttributeValue.FromList(
        [
            "text",
            AttributeValue.FromNumber("42"),
            AttributeValue.FromBool(true),
            AttributeValue.Null,
            AttributeValue.FromBinary([1, 2, 3]),
            AttributeValue.FromList(["nested"]),
            AttributeValue.FromMap([KeyValuePair.Create<string, AttributeValue>("k", "v")]),
            AttributeValue.FromStringSet(["a", "b"]),
        ]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, list);

        // Assert
        await Assert.That(value).IsEqualTo(list);
    }

    [Test]
    public async Task PutItem_EmptyList_RoundTrips()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromList([]));

        // Assert
        await Assert.That((value.Type, value.AsList().Count)).IsEqualTo((AttributeType.List, 0));
    }

    [Test]
    public async Task PutItem_MapOfMixedTypes_RoundTrips()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var map = AttributeValue.FromMap(
        [
            KeyValuePair.Create<string, AttributeValue>("text", "Alice"),
            KeyValuePair.Create("number", AttributeValue.FromNumber("7")),
            KeyValuePair.Create("flag", AttributeValue.FromBool(false)),
            KeyValuePair.Create("nothing", AttributeValue.Null),
            KeyValuePair.Create("bytes", AttributeValue.FromBinary([9, 8, 7])),
            KeyValuePair.Create("empty", AttributeValue.FromString(string.Empty)),
            KeyValuePair.Create("sets", AttributeValue.FromNumberSet(["1", "2"])),
        ]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, map);

        // Assert
        await Assert.That(value).IsEqualTo(map);
    }

    [Test]
    public async Task PutItem_EmptyMap_RoundTrips()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromMap([]));

        // Assert
        await Assert.That((value.Type, value.AsMap().Count)).IsEqualTo((AttributeType.Map, 0));
    }

    [Test]
    public async Task PutItem_StringSet_RoundTripsIgnoringOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var set = AttributeValue.FromStringSet(["red", "green", "blue"]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, set);

        // Assert
        await Assert.That(value).IsEqualTo(set);
    }

    [Test]
    public async Task PutItem_StringSetWithEmptyMember_RoundTrips()
    {
        // Arrange: real DynamoDB accepts the empty string as a set member (recorded in the Parity Suite's captures)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var set = AttributeValue.FromStringSet([string.Empty, "a"]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, set);

        // Assert
        await Assert.That(value).IsEqualTo(set);
    }

    [Test]
    public async Task PutItem_NumberSet_RoundTripsIgnoringOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var set = AttributeValue.FromNumberSet(["1", "2.5", "-3"]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, set);

        // Assert
        await Assert.That(value).IsEqualTo(set);
    }

    [Test]
    public async Task PutItem_BinarySet_RoundTripsIgnoringOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var set = AttributeValue.FromBinarySet([[1, 2], [3], [255, 0]]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, set);

        // Assert
        await Assert.That(value).IsEqualTo(set);
    }

    [Test]
    public async Task PutItem_BinarySetWithEmptyMember_RoundTrips()
    {
        // Arrange: real DynamoDB accepts a zero-length member in a binary set (recorded in the Parity Suite's captures)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var set = AttributeValue.FromBinarySet([[], [1]]);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, set);

        // Assert
        await Assert.That(value).IsEqualTo(set);
    }

    [Test]
    public async Task PutItem_MapsNested31Levels_RoundTrips()
    {
        // Arrange: DynamoDB allows 32 levels of nesting, counting the scalar at the bottom (31 maps around a string)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var nested = NestedMaps(31);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, nested);

        // Assert
        await Assert.That(value).IsEqualTo(nested);
    }

    [Test]
    public async Task PutItem_MapsNested32Levels_ThrowsValidationException()
    {
        // Arrange: one level beyond the limit
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, NestedMaps(32)));

        // Assert
        await Assert.That(failure.Message).Contains("Nesting Levels");
    }

    [Test]
    public async Task PutItem_ListsNested31Levels_RoundTrips()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var nested = NestedLists(31);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, nested);

        // Assert
        await Assert.That(value).IsEqualTo(nested);
    }

    [Test]
    public async Task PutItem_ListsNested32Levels_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, NestedLists(32)));

        // Assert
        await Assert.That(failure.Message).Contains("Nesting Levels");
    }

    [Test]
    public async Task PutItem_ItemWithEveryAttributeType_RoundTripsAllAttributes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = "everything",
            ["s"] = "text",
            ["n"] = AttributeValue.FromNumber("3.14"),
            ["b"] = AttributeValue.FromBinary([1, 2, 3]),
            ["bool"] = AttributeValue.FromBool(true),
            ["null"] = AttributeValue.Null,
            ["l"] = AttributeValue.FromList(["x", AttributeValue.FromNumber("1")]),
            ["m"] = AttributeValue.FromMap([KeyValuePair.Create<string, AttributeValue>("inner", "value")]),
            ["ss"] = AttributeValue.FromStringSet(["a", "b"]),
            ["ns"] = AttributeValue.FromNumberSet(["1", "2"]),
            ["bs"] = AttributeValue.FromBinarySet([[1], [2]]),
            ["über name \U0001F600"] = "attribute names are text too",
        };

        // Act
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("everything"), ConsistentRead = true });

        // Assert
        await Assert.That(response.Item!.Count).IsEqualTo(item.Count);
        foreach (var (name, expected) in item)
        {
            await Assert.That(response.Item[name]).IsEqualTo(expected);
        }
    }

    private static AttributeValue NestedMaps(int maps)
    {
        AttributeValue value = "bottom";
        for (var i = 0; i < maps; i++)
        {
            value = AttributeValue.FromMap([KeyValuePair.Create("a", value)]);
        }

        return value;
    }

    private static AttributeValue NestedLists(int lists)
    {
        AttributeValue value = "bottom";
        for (var i = 0; i < lists; i++)
        {
            value = AttributeValue.FromList([value]);
        }

        return value;
    }
}
