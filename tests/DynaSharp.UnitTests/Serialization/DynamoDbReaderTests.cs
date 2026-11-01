using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DynamoDbReaderTests
{
    [Test]
    public async Task ReadStartItem_Object_StopsInsideTheItem()
    {
        // Arrange
        const string json = """{}""";

        // Act
        var hasAttribute = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            return r.TryReadAttributeName();
        });

        // Assert
        await Assert.That(hasAttribute).IsFalse();
    }

    [Test]
    public async Task ReadStartItem_Array_ThrowsJsonExceptionNamingTokenAndPosition()
    {
        // Arrange
        const string json = """[]""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            return 0;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected the start of an item ('{') but found the JSON token StartArray at byte 0.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStartItem_AfterEndOfData_ThrowsJsonException()
    {
        // Arrange
        const string json = """{}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            r.TryReadAttributeName();
            r.ReadStartItem();
            return 0;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Unexpected end of the DynamoDB JSON data.", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryReadStartItem_ItemInAnArray_ReturnsTrueAndStopsInsideTheItem()
    {
        // Arrange
        const string json = """[{"a":{"S":"1"}}]""";

        // Act
        var name = TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            return r.TryReadStartItem() && r.TryReadAttributeName() ? r.GetAttributeName() : null;
        });

        // Assert
        await Assert.That(name).IsEqualTo("a");
    }

    [Test]
    public async Task TryReadStartItem_EmptyItemInAnArray_ReturnsTrueAndTheItemEndsImmediately()
    {
        // Arrange
        const string json = "[{}]";

        // Act
        var hasAttribute = TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            r.TryReadStartItem();
            return r.TryReadAttributeName();
        });

        // Assert
        await Assert.That(hasAttribute).IsFalse();
    }

    [Test]
    public async Task TryReadStartItem_EndOfTheArray_ReturnsFalse()
    {
        // Arrange
        const string json = "[]";

        // Act
        var hasItem = TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            return r.TryReadStartItem();
        });

        // Assert
        await Assert.That(hasItem).IsFalse();
    }

    [Test]
    public async Task TryReadStartItem_AfterTheLastItem_ReturnsFalseAndConsumesTheEnd()
    {
        // Arrange
        const string json = """[{},{}]""";

        // Act
        var count = TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            var items = 0;
            while (r.TryReadStartItem())
            {
                r.TryReadAttributeName();
                items++;
            }

            return items;
        });

        // Assert
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task TryReadStartItem_ElementThatIsNotAnObject_ThrowsJsonExceptionNamingTokenAndPosition()
    {
        // Arrange
        const string json = """[1]""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            return r.TryReadStartItem();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected the start of an item ('{') or the end of the array (']') but found the JSON token Number at byte 1.", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryReadStartItem_AfterEndOfData_ThrowsJsonException()
    {
        // Arrange
        const string json = "[]";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            r.TryReadStartItem();
            return r.TryReadStartItem();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("Unexpected end", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryReadAttributeName_Name_ReturnsTrueAndExposesName()
    {
        // Arrange
        const string json = """{"Artist":{"S":"Acme Band"}}""";

        // Act
        var name = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            return r.TryReadAttributeName() ? r.GetAttributeName() : null;
        });

        // Assert
        await Assert.That(name).IsEqualTo("Artist");
    }

    [Test]
    public async Task TryReadAttributeName_InsideArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """[{"S":"a"}]""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.Json.Read();
            return r.TryReadAttributeName();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected an attribute name or the end of an item or map ('}') but found the JSON token StartObject at byte 1.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"name":{"S":"x"}}""", true)]
    [Arguments("""{"n\u0061me":{"S":"x"}}""", true)]
    [Arguments("""{"nam":{"S":"x"}}""", false)]
    [Arguments("""{"Name":{"S":"x"}}""", false)]
    public async Task AttributeNameEquals_Name_ComparesUnescapedTextOrdinally(string json, bool expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var equal = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            r.TryReadAttributeName();
            return r.AttributeNameEquals("name"u8);
        });

        // Assert
        await Assert.That(equal).IsEqualTo(expected);
    }

    [Test]
    public async Task GetAttributeName_EscapedName_ReturnsUnescapedName()
    {
        // Arrange
        const string json = """{"caf\u00e9":{"S":"x"}}""";

        // Act
        var name = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            r.TryReadAttributeName();
            return r.GetAttributeName();
        });

        // Assert
        await Assert.That(name).IsEqualTo("café");
    }

    [Test]
    [Arguments("""{"S":"x"}""", AttributeType.String)]
    [Arguments("""{"N":"1"}""", AttributeType.Number)]
    [Arguments("""{"B":"AQ=="}""", AttributeType.Binary)]
    [Arguments("""{"BOOL":true}""", AttributeType.Boolean)]
    [Arguments("""{"BOOL":false}""", AttributeType.Boolean)]
    [Arguments("""{"NULL":true}""", AttributeType.Null)]
    [Arguments("""{"L":[]}""", AttributeType.List)]
    [Arguments("""{"M":{}}""", AttributeType.Map)]
    [Arguments("""{"SS":["a"]}""", AttributeType.StringSet)]
    [Arguments("""{"NS":["1"]}""", AttributeType.NumberSet)]
    [Arguments("""{"BS":["AQ=="]}""", AttributeType.BinarySet)]
    [Arguments("""{"\u0053":"x"}""", AttributeType.String)]
    [Arguments("""{"N\u0053":["1"]}""", AttributeType.NumberSet)]
    [Arguments("""{"\u0042\u004F\u004F\u004C":true}""", AttributeType.Boolean)]
    [Arguments("""{"\u004E\u0055\u004C\u004C":true}""", AttributeType.Null)]
    public async Task ReadStartAttribute_TypeDescriptor_ReturnsAttributeType(string json, AttributeType expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var type = TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(type).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"S":"x"}""", JsonTokenType.String)]
    [Arguments("""{"BOOL":false}""", JsonTokenType.False)]
    [Arguments("""{"L":[]}""", JsonTokenType.StartArray)]
    [Arguments("""{"M":{}}""", JsonTokenType.StartObject)]
    public async Task ReadStartAttribute_Value_StopsOnTheValue(string json, JsonTokenType expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var token = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.Json.TokenType;
        });

        // Assert
        await Assert.That(token).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"X":"x"}""", "X")]
    [Arguments("""{"s":"x"}""", "s")]
    [Arguments("""{"":"x"}""", "")]
    [Arguments("""{"SX":["x"]}""", "SX")]
    [Arguments("""{"XS":["x"]}""", "XS")]
    [Arguments("""{"SSS":["x"]}""", "SSS")]
    [Arguments("""{"BOOX":true}""", "BOOX")]
    [Arguments("""{"NULLS":true}""", "NULLS")]
    [Arguments("""{"\u0058":"x"}""", "X")]
    [Arguments("""{"\u0042\u004F\u004F\u004C\u0058":true}""", "BOOLX")]
    public async Task ReadStartAttribute_UnknownDescriptor_ThrowsJsonExceptionNamingIt(string json, string descriptor)
    {
        // Arrange
        var expected = $"Unknown DynamoDB type descriptor \"{descriptor}\" at byte 1.";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage(expected, StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStartAttribute_LongEscapedDescriptor_ThrowsJsonException()
    {
        // Arrange: unescaped, the name is longer than any descriptor and than the buffer used for unescaping.
        const string json = """{"\u0041BBBBBBBBBBBBBBBBBBBBBBBBB":"x"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Unknown DynamoDB type descriptor \"ABBBBBBBBBBBBBBBBBBBBBBBBB\" at byte 1.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStartAttribute_NotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "\"x\"";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected the start of an attribute value ('{') but found the JSON token String at byte 0.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStartAttribute_EmptyObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "{}";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected a type descriptor such as \"S\" but found the JSON token EndObject at byte 1.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"S":1}""")]
    [Arguments("""{"N":1}""")]
    [Arguments("""{"B":null}""")]
    [Arguments("""{"BOOL":"true"}""")]
    [Arguments("""{"BOOL":null}""")]
    [Arguments("""{"NULL":false}""")]
    [Arguments("""{"NULL":"true"}""")]
    [Arguments("""{"L":{}}""")]
    [Arguments("""{"M":[]}""")]
    [Arguments("""{"SS":"a"}""")]
    [Arguments("""{"NS":"1"}""")]
    [Arguments("""{"BS":"AQ=="}""")]
    public async Task ReadStartAttribute_ValueOfWrongJsonKind_ThrowsJsonException(string json)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessageContaining("is not a valid value for a DynamoDB attribute of type", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStartAttribute_NullFalse_MessageNamesTokenPositionAndType()
    {
        // Arrange
        const string json = """{"NULL":false}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => r.ReadStartAttribute());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The JSON token False at byte 8 is not a valid value for a DynamoDB attribute of type Null.", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryReadStartAttribute_Element_ReturnsTrueAndElementType()
    {
        // Arrange
        const string json = """{"L":[{"N":"3.14159"}]}""";

        // Act
        var type = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.TryReadStartAttribute(out var elementType) ? elementType : AttributeType.None;
        });

        // Assert
        await Assert.That(type).IsEqualTo(AttributeType.Number);
    }

    [Test]
    public async Task TryReadStartAttribute_EndOfList_ReturnsFalse()
    {
        // Arrange
        const string json = """{"L":[]}""";

        // Act
        var found = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.TryReadStartAttribute(out _);
        });

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task TryReadStartAttribute_EndOfList_ReturnsTypeNone()
    {
        // Arrange
        const string json = """{"L":[]}""";

        // Act
        var type = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            r.TryReadStartAttribute(out var elementType);
            return elementType;
        });

        // Assert
        await Assert.That(type).IsEqualTo(AttributeType.None);
    }

    [Test]
    public async Task TryReadStartAttribute_ElementNotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"L":["Cookies"]}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.TryReadStartAttribute(out _);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected a list element ('{') or the end of the list (']') but found the JSON token String at byte 6.", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryReadStartAttribute_ElementWithUnknownDescriptor_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"L":[{"Q":"x"}]}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.TryReadStartAttribute(out _);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessageContaining("Unknown DynamoDB type descriptor \"Q\"", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadEndAttribute_AfterValue_ConsumesClosingBrace()
    {
        // Arrange
        const string json = """{"S":"x"}""";

        // Act
        var consumed = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            r.ReadEndAttribute();
            return r.Json.BytesConsumed;
        });

        // Assert
        await Assert.That(consumed).IsEqualTo(json.Length);
    }

    [Test]
    public async Task ReadEndAttribute_SecondDescriptor_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"S":"x","N":"1"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            r.ReadEndAttribute();
            return 0;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected the end of the attribute value ('}') but found the JSON token PropertyName at byte 9.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadEndAttribute_TruncatedJson_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"S":"x" """;

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            r.ReadEndAttribute();
            return 0;
        });

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task TryReadSetElement_Elements_ReadsAllInOrder()
    {
        // Arrange
        const string json = """{"SS":["Giraffe","Hippo","Zebra"]}""";

        // Act
        var elements = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            var result = new List<string>();
            while (r.TryReadSetElement())
            {
                result.Add(r.GetString());
            }

            r.ReadEndAttribute();
            return result;
        });

        // Assert
        await Assert.That(string.Join(',', elements)).IsEqualTo("Giraffe,Hippo,Zebra");
    }

    [Test]
    public async Task TryReadSetElement_EndOfSet_ReturnsFalse()
    {
        // Arrange
        const string json = """{"NS":[]}""";

        // Act
        var found = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.TryReadSetElement();
        });

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task TryReadSetElement_ElementNotAString_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"NS":[42]}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.TryReadSetElement();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected a set element (a JSON string) or the end of the set (']') but found the JSON token Number at byte 7.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"S":"Hello"}""", "Hello")]
    [Arguments("""{"S":""}""", "")]
    [Arguments("""{"S":"caf\u00e9 \"quoted\" \ud83d\ude00"}""", "café \"quoted\" 😀")]
    public async Task GetString_StringValue_ReturnsUnescapedString(string json, string expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var value = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetString();
        });

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task GetString_BooleanValue_ThrowsInvalidOperationException()
    {
        // Arrange
        const string json = """{"BOOL":true}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetString();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments("""{"N":"42"}""", 42)]
    [Arguments("""{"N":"-19"}""", -19)]
    [Arguments("""{"N":"4\u0032"}""", 42)]
    [Arguments("""{"N":"1E+2"}""", 100)]
    [Arguments("""{"N":"2.0"}""", 2)]
    public async Task GetNumber_IntegerText_ParsesInt(string json, int expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var value = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<int>();
        });

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task GetNumber_DecimalText_KeepsAllDigits()
    {
        // Arrange
        const string json = """{"N":"12345678901234567890.123456789"}""";

        // Act
        var value = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<decimal>();
        });

        // Assert
        await Assert.That(value).IsEqualTo(12345678901234567890.123456789m);
    }

    [Test]
    public async Task GetNumber_ExponentForDecimal_Parses()
    {
        // Arrange
        const string json = """{"N":"-1.5E-10"}""";

        // Act
        var value = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<decimal>();
        });

        // Assert
        await Assert.That(value).IsEqualTo(-0.00000000015m);
    }

    [Test]
    [Arguments("""{"N":" 42"}""")]
    [Arguments("""{"N":"42 "}""")]
    public async Task GetNumber_SurroundingWhitespace_ThrowsFormatException(string json)
    {
        // Arrange (the argument)

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task GetNumber_ThousandsSeparator_ThrowsFormatException()
    {
        // Arrange
        const string json = """{"N":"1,000"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<double>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    [Arguments("""{"N":"1.5"}""")]
    [Arguments("""{"N":"99999999999"}""")]
    public async Task GetNumber_NotRepresentableAsInt_ThrowsOverflowException(string json)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task GetNumber_EscapedNotANumber_ThrowsFormatException()
    {
        // Arrange
        const string json = """{"N":"\u0041"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task GetNumber_BooleanValue_ThrowsInvalidOperationException()
    {
        // Arrange
        const string json = """{"BOOL":true}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot read a number from the JSON token True.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"N":"-1.5E-10"}""", "-1.5E-10")]
    [Arguments("""{"N":"\u0031\u0030"}""", "10")]
    public async Task GetNumberText_NumberValue_ReturnsTextAsTransferred(string json, string expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var text = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetNumberText();
        });

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"B":"dGhpcyB0ZXh0IGlzIGJhc2U2NC1lbmNvZGVk"}""", "this text is base64-encoded")]
    [Arguments("""{"B":"U3Vubnk\u003D"}""", "Sunny")]
    [Arguments("""{"B":""}""", "")]
    public async Task GetBinary_Base64_ReturnsDecodedBytes(string json, string expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var bytes = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetBinary();
        });

        // Assert
        await Assert.That(Encoding.UTF8.GetString(bytes)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetBinary_InvalidBase64_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"B":"not base64!"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetBinary();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The binary value is not valid base64.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"BOOL":true}""", true)]
    [Arguments("""{"BOOL":false}""", false)]
    public async Task GetBool_BooleanValue_ReturnsBoolean(string json, bool expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var value = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            return r.GetBool();
        });

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task SkipAttributeValue_NestedUnknownAttribute_ContinuesWithNextAttribute()
    {
        // Arrange
        const string json = """{"skip":{"M":{"x":{"L":[{"S":"1"},{"M":{}}]}}},"keep":{"S":"kept"}}""";

        // Act
        var kept = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            r.TryReadAttributeName();
            r.SkipAttributeValue();
            r.TryReadAttributeName();
            r.ReadStartAttribute();
            return r.GetString();
        });

        // Assert
        await Assert.That(kept).IsEqualTo("kept");
    }

    [Test]
    public async Task SkipAttributeValue_ListElement_ContinuesWithNextElement()
    {
        // Arrange
        const string json = """{"L":[{"SS":["a","b"]},{"S":"kept"}]}""";

        // Act
        var kept = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            r.SkipAttributeValue();
            r.TryReadStartAttribute(out _);
            return r.GetString();
        });

        // Assert
        await Assert.That(kept).IsEqualTo("kept");
    }

    [Test]
    public async Task SkipAttributeValue_ValueNotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"skip":"plain JSON"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartItem();
            r.TryReadAttributeName();
            r.SkipAttributeValue();
            return 0;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected the start of an attribute value ('{') but found the JSON token String at byte 8.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Json_ResponseEnvelope_NavigatesToTheItem()
    {
        // Arrange: GetItem response from the DynamoDB API examples (botocore examples-1.json).
        const string json = """
            {
              "ConsumedCapacity": {"CapacityUnits": 0.5, "TableName": "Music"},
              "Item": {
                "AlbumTitle": {"S": "Songs About Life"},
                "Artist": {"S": "Acme Band"},
                "SongTitle": {"S": "Happy Day"}
              }
            }
            """;

        // Act
        var artist = TestJson.Read(json, static (ref r) =>
        {
            r.Json.Read();
            string? result = null;
            while (r.Json.Read() && r.Json.TokenType == JsonTokenType.PropertyName)
            {
                if (!r.Json.ValueTextEquals("Item"u8))
                {
                    r.Json.Skip();
                    continue;
                }

                result = AttributeValueConverter.ReadItem(ref r)["Artist"].AsString();
            }

            return result;
        });

        // Assert
        await Assert.That(artist).IsEqualTo("Acme Band");
    }
}
