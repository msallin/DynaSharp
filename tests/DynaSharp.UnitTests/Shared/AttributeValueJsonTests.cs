using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests;

/// <summary>The bridge between plain JSON and <see cref="AttributeValue"/>: <c>FromJson</c>, <c>ToJson</c> and <c>WriteJson</c>, and the rules of DynamoDB on the way in.</summary>
public sealed class AttributeValueJsonTests
{
    private const string DigitsMessage = "has more than the 38 significant digits that DynamoDB stores. (Parameter 'utf8Json')";

    private const string LargeMessage = "has a magnitude of 1E+126 or more, beyond the 9.9999999999999999999999999999999999999E+125 that DynamoDB stores. (Parameter 'utf8Json')";

    private const string SmallMessage = "has a magnitude below the 1E-130 that DynamoDB stores. (Parameter 'utf8Json')";

    public static IEnumerable<(string Json, string Stored)> Conversions() =>
    [
        ("""{"a":"x","b":1.50,"c":true,"d":false,"e":[1,"y"],"f":{"g":{}},"h":[],"i":""}""",
            """{"M":{"a":{"S":"x"},"b":{"N":"1.50"},"c":{"BOOL":true},"d":{"BOOL":false},"e":{"L":[{"N":"1"},{"S":"y"}]},"f":{"M":{"g":{"M":{}}}},"h":{"L":[]},"i":{"S":""}}}"""),
        ("""{"a":null,"b":1}""", """{"M":{"b":{"N":"1"}}}"""),
        ("""{"a":null}""", """{"M":{}}"""),
        ("""[null,1,[null]]""", """{"L":[{"NULL":true},{"N":"1"},{"L":[{"NULL":true}]}]}"""),
        ("null", """{"NULL":true}"""),
        ("\"x\"", """{"S":"x"}"""),
        ("-0.0", """{"N":"-0.0"}"""),
        ("1e5", """{"N":"1e5"}"""),
        ("true", """{"BOOL":true}"""),
        (""" { "a" : [ 1 , 2 ] } """, """{"M":{"a":{"L":[{"N":"1"},{"N":"2"}]}}}"""),
        ("""{"ab":"é\n","b":{"a":1},"a":2}""", """{"M":{"ab":{"S":"é\n"},"b":{"M":{"a":{"N":"1"}}},"a":{"N":"2"}}}"""),
        ("""{"costarring":1,"liquid":2}""", """{"M":{"costarring":{"N":"1"},"liquid":{"N":"2"}}}"""),
    ];

    public static IEnumerable<string> NumbersThatFit() =>
    [
        "12345678901234567890123456789012345678",
        "-1234567890123456789012345678901234567.8",
        "9.9999999999999999999999999999999999999E+125",
        "-9.9999999999999999999999999999999999999e125",
        "1E-130",
        "0.0001E-126",
        "100000000000000000000000000000000000000000",
        "1.5000000000000000000000000000000000000000000",
        "0.000000000000000000000000000000000000000000000012345678901234567890123456789012345678",
        "0E+99999999999999999999",
        "0",
    ];

    public static IEnumerable<(string Json, string Message)> NumbersBeyondTheLimits() =>
    [
        ("123456789012345678901234567890123456789", "The JSON number at $ " + DigitsMessage),
        ("1.23456789012345678901234567890123456789", "The JSON number at $ " + DigitsMessage),
        ("[0.0000123456789012345678901234567890123456789]", "The JSON number at $[0] " + DigitsMessage),
        ("1E+126", "The JSON number at $ " + LargeMessage),
        ("10E+125", "The JSON number at $ " + LargeMessage),
        ("-1E99999999999999999999", "The JSON number at $ " + LargeMessage),
        ("1E-131", "The JSON number at $ " + SmallMessage),
        ("0.1E-130", "The JSON number at $ " + SmallMessage),
        ("1E-99999999999999999999", "The JSON number at $ " + SmallMessage),
        ("""{"lines":[{"weight":2},{"weight":1E+126}]}""", "The JSON number at $.lines[1].weight " + LargeMessage),
        ("""{"a b":[0,1E999]}""", "The JSON number at $['a b'][1] " + LargeMessage),
        ("""{"it's\\":1E999}""", "The JSON number at $['it\\'s\\\\'] " + LargeMessage),
        ("""{" ":{"1a":1E999}}""", "The JSON number at $[' ']['1a'] " + LargeMessage),
        ("""{"_x9":{"é":1E999}}""", "The JSON number at $._x9['é'] " + LargeMessage),
    ];

    public static IEnumerable<(string Json, string Message)> DuplicateNames() =>
    [
        ("""{"a":1,"b":2,"a":3}""", "The name at $.a is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
        ("""{"x":[{"k":1,"k":2}]}""", "The name at $.x[0].k is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
        ("""{"a":1,"a":2}""", "The name at $.a is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
        ("""{"a":null,"a":1}""", "The name at $.a is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
        ("""{"costarring":1,"liquid":2,"costarring":3}""", "The name at $.costarring is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
        ("""{"costarring":1,"liquid":2,"liquid":3}""", "The name at $.liquid is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
        ("""{"costarring":1,"x":2,"liquid":3,"liquid":4}""", "The name at $.liquid is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')"),
    ];

    public static IEnumerable<Func<(byte[] Json, string Message)>> TextThatIsNoText() =>
    [
        () => ([.. "{\"a\":\""u8, 0xFF, .. "\"}"u8], "The JSON string at $.a is not valid UTF-8, which DynamoDB cannot store. (Parameter 'utf8Json')"),
        () => ([.. "{\"x\":{\""u8, 0xC3, .. "\":1}}"u8], "A name in the JSON object at $.x is not valid UTF-8, which DynamoDB cannot store. (Parameter 'utf8Json')"),
        () => ("""[1,"\ud800"]"""u8.ToArray(), "The JSON string at $[1] is not valid UTF-16: it holds a lone surrogate, which DynamoDB cannot store. (Parameter 'utf8Json')"),
        () => ("""{"x":{"\udc00":1}}"""u8.ToArray(), "A name in the JSON object at $.x is not valid UTF-16: it holds a lone surrogate, which DynamoDB cannot store. (Parameter 'utf8Json')"),
    ];

    public static IEnumerable<(string Json, string Message)> EmptyNames() =>
    [
        ("""{"":1}""", "A name in the JSON object at $ is empty, and DynamoDB has no empty attribute names. (Parameter 'utf8Json')"),
        ("""{"a":[{"b":1,"":null}]}""", "A name in the JSON object at $.a[0] is empty, and DynamoDB has no empty attribute names. (Parameter 'utf8Json')"),
    ];

    public static IEnumerable<string> NotJson() => [string.Empty, "{\"a\":}", "1 2", "[1", "[1,]", "/* c */ 1"];

    public static IEnumerable<Func<(AttributeValue Value, string Json)>> Values() =>
    [
        () => (AttributeValue.FromMap([new("a", "x"), new("b", AttributeValue.FromNumber("1.50")), new("c", AttributeValue.Null), new("d", AttributeValue.FromList([true, false]))]),
            """{"a":"x","b":1.50,"c":null,"d":[true,false]}"""),
        () => (AttributeValue.FromNumber("12345678901234567890123456789012345678"), "12345678901234567890123456789012345678"),
        () => (AttributeValue.FromNumber("-1.5E+125"), "-1.5E+125"),
        () => (AttributeValue.FromStringSet(["a", "b"]), """["a","b"]"""),
        () => (AttributeValue.FromNumberSet(["1", "2.5"]), "[1,2.5]"),
        () => (AttributeValue.FromBinary([1, 2, 3]), "\"AQID\""),
        () => (AttributeValue.FromBinarySet([[1], [255]]), """["AQ==","/w=="]"""),
        () => (AttributeValue.FromMap([]), "{}"),
        () => (AttributeValue.FromList([]), "[]"),
        () => (AttributeValue.FromString("é\"<"), "\"é\\\"<\""),
    ];

    public static IEnumerable<string> JsonNumbers() => ["0", "-0", "0.5", "10", "1E+2", "1e-2", "1.25E10", "-12.5e0"];

    public static IEnumerable<string> NotJsonNumbers() => ["+1", ".5", "1.", "01", "-", "abc", "1x", "1.5x", "1e", "1e+", "1e2x", "1.e2"];

    [Test]
    [MethodDataSource(nameof(Conversions))]
    public async Task FromJson_Utf8_StoresTheJsonNatively(string json, string stored)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(json);

        // Act
        var value = AttributeValue.FromJson(utf8);

        // Assert
        await Assert.That(value.ToString()).IsEqualTo(stored);
    }

    [Test]
    [MethodDataSource(nameof(Conversions))]
    public async Task FromJson_Element_StoresTheJsonNatively(string json, string stored)
    {
        // Arrange
        var element = JsonElement.Parse(json);

        // Act
        var value = AttributeValue.FromJson(element);

        // Assert
        await Assert.That(value.ToString()).IsEqualTo(stored);
    }

    [Test]
    public async Task FromJson_ElementInsideADocument_TakesThatElementOnly()
    {
        // Arrange
        var document = JsonElement.Parse("""{"other":1,"label":{"to":"Bern"}}""");

        // Act
        var value = AttributeValue.FromJson(document.GetProperty("label"));

        // Assert
        await Assert.That(value.ToString()).IsEqualTo("""{"M":{"to":{"S":"Bern"}}}""");
    }

    [Test]
    public async Task FromJson_ElementParsedWithCommentsAndTrailingCommas_StoresTheValues()
    {
        // Arrange
        var element = JsonElement.Parse("[1, /* two */ 2,]", new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        // Act
        var value = AttributeValue.FromJson(element);

        // Assert
        await Assert.That(value.ToString()).IsEqualTo("""{"L":[{"N":"1"},{"N":"2"}]}""");
    }

    [Test]
    public async Task FromJson_DefaultElement_ThrowsArgumentException()
    {
        // Arrange
        // Act
        var act = () => AttributeValue.FromJson(default(JsonElement));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessage("A default JsonElement holds no JSON. (Parameter 'json')", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NumbersThatFit))]
    public async Task FromJson_NumberWithinTheLimits_KeepsItsText(string number)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(number);

        // Act
        var value = AttributeValue.FromJson(utf8);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo(number);
    }

    [Test]
    [MethodDataSource(nameof(NumbersBeyondTheLimits))]
    public async Task FromJson_NumberBeyondTheLimits_ThrowsArgumentExceptionWithThePath(string json, string message)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(json);

        // Act
        var act = () => AttributeValue.FromJson(utf8);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task FromJson_ElementBeyondTheLimits_NamesTheParameterJson()
    {
        // Arrange
        var element = JsonElement.Parse("""{"n":1E+126}""");

        // Act
        var act = () => AttributeValue.FromJson(element);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>().WithParameterName("json");
    }

    [Test]
    [Arguments(31)]
    [Arguments(32)]
    public async Task FromJson_ThirtyTwoLevels_IsStored(int containers)
    {
        // Arrange (a value at the bottom of 31 arrays is the 32nd level, and an empty array in 31 others is too)
        var json = containers == 31 ? new string('[', 31) + "1" + new string(']', 31) : new string('[', 32) + new string(']', 32);

        // Act
        var value = AttributeValue.FromJson(Encoding.UTF8.GetBytes(json));

        // Assert
        await Assert.That(value.Type).IsEqualTo(AttributeType.List);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FromJson_ThirtyThreeLevels_ThrowsArgumentExceptionWithThePath(bool emptyAtTheBottom)
    {
        // Arrange
        var json = emptyAtTheBottom ? new string('[', 33) + new string(']', 33) : """{"a":""" + new string('[', 31) + "1" + new string(']', 31) + "}";
        var path = emptyAtTheBottom ? "$" + string.Concat(Enumerable.Repeat("[0]", 32)) : "$.a" + string.Concat(Enumerable.Repeat("[0]", 31));

        // Act
        var act = () => AttributeValue.FromJson(Encoding.UTF8.GetBytes(json));

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>()
            .WithMessage($"The JSON value at {path} is nested deeper than the 32 levels that DynamoDB allows. (Parameter 'utf8Json')", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(DuplicateNames))]
    public async Task FromJson_NameTwiceInAnObject_ThrowsArgumentExceptionWithThePath(string json, string message)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(json);

        // Act
        var act = () => AttributeValue.FromJson(utf8);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(EmptyNames))]
    public async Task FromJson_EmptyName_ThrowsArgumentExceptionWithThePath(string json, string message)
    {
        // Arrange (DynamoDB Local and Dynoxide answer "Empty attribute name" for one in a map)
        var utf8 = Encoding.UTF8.GetBytes(json);

        // Act
        var act = () => AttributeValue.FromJson(utf8);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(TextThatIsNoText))]
    public async Task FromJson_TextThatIsNoText_ThrowsArgumentExceptionWithThePath(byte[] json, string message)
    {
        // Arrange
        // Act
        var act = () => AttributeValue.FromJson(json);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotJson))]
    public async Task FromJson_TextThatIsNotOneJsonValue_ThrowsJsonException(string json)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(json);

        // Act
        var act = () => AttributeValue.FromJson(utf8);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task FromJson_ManyLongNamesAndLongEscapedText_KeepsEveryOne()
    {
        // Arrange (more names and longer text than the first buffers hold)
        var names = Enumerable.Range(0, 40).Select(static index => new string('n', 20) + index).ToArray();
        var escaped = string.Concat(Enumerable.Repeat("\\u00e9", 400));
        var json = "{" + string.Join(',', names.Select(static name => $"\"{name}\":1")) + $",\"{new string('x', 300)}\":{{\"t\":\"{escaped}\"}}}}";

        // Act
        var map = AttributeValue.FromJson(Encoding.UTF8.GetBytes(json)).AsMap();

        // Assert
        await Assert.That((map.Count, map[new string('x', 300)].AsMap()["t"].AsString())).IsEqualTo((41, new string('é', 400)));
    }

    [Test]
    public async Task FromJson_DuplicateAfterManyNames_IsFound()
    {
        // Arrange
        var json = "{" + string.Join(',', Enumerable.Range(0, 40).Select(static index => $"\"k{index}\":1")) + ",\"k3\":2}";

        // Act
        var act = () => AttributeValue.FromJson(Encoding.UTF8.GetBytes(json));

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>()
            .WithMessage("The name at $.k3 is in its JSON object twice, and a DynamoDB map holds a name once. (Parameter 'utf8Json')", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(Values))]
    public async Task ToJson_Value_IsThePlainJson(AttributeValue value, string json)
    {
        // Arrange
        // Act
        var element = value.ToJson();

        // Assert
        await Assert.That(element.GetRawText()).IsEqualTo(json);
    }

    [Test]
    [MethodDataSource(nameof(Conversions))]
    public async Task ToJson_ValueFromJson_GivesTheJsonWithoutTheNullsOfObjects(string json, string stored)
    {
        // Arrange
        var value = AttributeValue.FromJson(Encoding.UTF8.GetBytes(json));

        // Act
        var back = AttributeValue.FromJson(value.ToJson());

        // Assert
        await Assert.That(back.ToString()).IsEqualTo(stored);
    }

    [Test]
    public async Task WriteJson_InsideOtherJson_WritesOneValueWithTheSettingsOfTheWriter()
    {
        // Arrange
        var value = AttributeValue.FromMap([new("a", AttributeValue.FromList([1]))]);
        using var stream = new MemoryStream();

        // Act
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("label");
            value.WriteJson(writer);
            writer.WriteEndObject();
        }

        // Assert
        await Assert.That(Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n"))
            .IsEqualTo("{\n  \"label\": {\n    \"a\": [\n      1\n    ]\n  }\n}");
    }

    [Test]
    [MethodDataSource(nameof(JsonNumbers))]
    public async Task ToJson_NumberInTheFormOfJson_IsWrittenAsItIs(string number)
    {
        // Arrange
        var value = AttributeValue.FromNumber(number);

        // Act
        var element = value.ToJson();

        // Assert
        await Assert.That(element.GetRawText()).IsEqualTo(number);
    }

    [Test]
    [MethodDataSource(nameof(NotJsonNumbers))]
    public async Task ToJson_NumberNotInTheFormOfJson_ThrowsFormatException(string number)
    {
        // Arrange
        var value = AttributeValue.FromNumberSet(["1", number]);

        // Act
        var act = () => value.ToJson();

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("A number is not in the form that JSON has for a number, such as -1.5E+3.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ToJson_StringWithALoneSurrogate_ThrowsArgumentException()
    {
        // Arrange
        var value = AttributeValue.FromString("a\ud800");

        // Act
        var act = () => value.ToJson();

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }

    [Test]
    public async Task ToJson_DefaultValue_ThrowsInvalidOperationException()
    {
        // Arrange
        var value = default(AttributeValue);

        // Act
        var act = () => value.ToJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("A default AttributeValue has no type, so it has no JSON.", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteJson_NullWriter_ThrowsArgumentNullException()
    {
        // Arrange
        AttributeValue value = "x";

        // Act
        var act = () => value.WriteJson(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Test]
    public async Task Read_NoType_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        var act = () =>
        {
            var reader = new DynamoDbReader("{}"u8);
            JsonBridge.Read(ref reader, AttributeType.None, writer);
        };

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a DynamoDB attribute type. (Parameter 'type')", StringComparison.Ordinal);
    }
}
