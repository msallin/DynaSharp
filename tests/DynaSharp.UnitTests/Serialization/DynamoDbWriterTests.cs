using System.Globalization;
using System.Numerics;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DynamoDbWriterTests
{
    /// <summary>Text that is not valid UTF-16, which <c>Utf8JsonWriter</c> would turn into U+FFFD: a surrogate that has no partner, wherever it stands.</summary>
    /// <returns>The texts.</returns>
    public static IEnumerable<string> LoneSurrogates() =>
    [
        "\uD800",
        "\uDC00",
        "a\uD800",
        "\uD800a",
        "a\uDC00b",
        "\uDC00\uD800",
        "\uD83D\uD83D\uDE00",
        "😀\uD83D",
        "😀\uDE00",
    ];

    public static IEnumerable<Func<(Action<DynamoDbWriter> Write, string Expected)>> Numbers()
    {
        yield return () => (static w => w.WriteNumber(42), """{"N":"42"}""");
        yield return () => (static w => w.WriteNumber(byte.MaxValue), """{"N":"255"}""");
        yield return () => (static w => w.WriteNumber(long.MinValue), """{"N":"-9223372036854775808"}""");
        yield return () => (static w => w.WriteNumber(ulong.MaxValue), """{"N":"18446744073709551615"}""");
        yield return () => (static w => w.WriteNumber(Int128.MinValue), """{"N":"-170141183460469231731687303715884105728"}""");
        yield return () => (static w => w.WriteNumber(1.50m), """{"N":"1.50"}""");
        yield return () => (static w => w.WriteNumber(decimal.MinValue), """{"N":"-79228162514264337593543950335"}""");
        yield return () => (static w => w.WriteNumber(0.1), """{"N":"0.1"}""");
        yield return () => (static w => w.WriteNumber(1e21), """{"N":"1E+21"}""");
        yield return () => (static w => w.WriteNumber(double.MinValue), """{"N":"-1.7976931348623157E+308"}""");
        yield return () => (static w => w.WriteNumber(0.1f), """{"N":"0.1"}""");
        yield return () => (static w => w.WriteNumber((Half)0.5), """{"N":"0.5"}""");
    }

    public static IEnumerable<Func<Action<DynamoDbWriter>>> NotFiniteNumbers()
    {
        yield return () => static w => w.WriteNumber(double.NaN);
        yield return () => static w => w.WriteNumber(double.PositiveInfinity);
        yield return () => static w => w.WriteNumber(float.NegativeInfinity);
        yield return () => static w => w.WriteNumber(Half.NaN);
        yield return () => static w => w.WriteNumberSetElement(double.NaN);
        yield return () => static w => w.WriteNumberSetElement(float.PositiveInfinity);
    }

    [Test]
    public async Task WriteString_Value_WritesStringDescriptor()
    {
        // Arrange
        const string value = "Hello";

        // Act
        var json = TestJson.Write(w => w.WriteString(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"Hello"}""");
    }

    [Test]
    public async Task WriteString_Empty_WritesEmptyString()
    {
        // Arrange
        var value = string.Empty;

        // Act
        var json = TestJson.Write(w => w.WriteString(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":""}""");
    }

    [Test]
    public async Task WriteString_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act
        var act = () => TestJson.Write(w => w.WriteString(value!));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteString_CharactersJsonDoesNotRequireToEscape_WritesThemUnescaped()
    {
        // Arrange
        const string value = "<a href='x'>&+é€";

        // Act
        var json = TestJson.Write(w => w.WriteString(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"<a href='x'>&+é€"}""");
    }

    [Test]
    public async Task WriteString_QuoteBackslashAndControlCharacter_EscapesThem()
    {
        // Arrange
        const string value = "\"\\\u0001";

        // Act
        var json = TestJson.Write(w => w.WriteString(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"\"\\\u0001"}""");
    }

    [Test]
    public async Task WriteString_Utf8_WritesStringDescriptor()
    {
        // Arrange
        var value = "Grüezi"u8.ToArray();

        // Act
        var json = TestJson.Write(w => w.WriteString(value.AsSpan()));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"Grüezi"}""");
    }

    [Test]
    public async Task WriteString_InvalidUtf8_WritesReplacementCharacter()
    {
        // Arrange
        byte[] value = [0x61, 0xFF];

        // Act
        var json = TestJson.Write(w => w.WriteString(value.AsSpan()));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"a\uFFFD"}""");
    }

    [Test]
    [MethodDataSource(nameof(Numbers))]
    public async Task WriteNumber_BuiltInNumberType_WritesInvariantDefaultFormat(Action<DynamoDbWriter> write, string expected)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(write);

        // Act
        var json = TestJson.Write(write);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task WriteNumber_CurrentCultureUsesDecimalComma_WritesDecimalPoint()
    {
        // Arrange
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-CH");
        string json;

        // Act
        try
        {
            json = TestJson.Write(static w => w.WriteNumber(1234.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"1234.5"}""");
    }

    [Test]
    [MethodDataSource(nameof(NotFiniteNumbers))]
    public async Task WriteNumber_NotFinite_ThrowsArgumentOutOfRangeException(Action<DynamoDbWriter> write)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(write);

        // Act
        var act = () => TestJson.Write(write);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining("DynamoDB cannot store NaN or infinity.", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteNumber_NotFinite_NamesValueParameter()
    {
        // Arrange
        var value = double.NaN;

        // Act
        var act = () => TestJson.Write(w => w.WriteNumber(value));

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Test]
    [Arguments(99)]
    [Arguments(130)]
    [Arguments(300)]
    public async Task WriteNumber_BigIntegerLongerThanStackBuffer_WritesAllDigits(int exponent)
    {
        // Arrange
        var value = BigInteger.Pow(10, exponent);

        // Act
        var json = TestJson.Write(w => w.WriteNumber(value));

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"N":"1{{new string('0', exponent)}}"}""");
    }

    [Test]
    public async Task WriteNumber_Text_WritesTextUnchanged()
    {
        // Arrange
        const string value = "-1.5E-10";

        // Act
        var json = TestJson.Write(w => w.WriteNumber(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"-1.5E-10"}""");
    }

    [Test]
    public async Task WriteNumber_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act
        var act = () => TestJson.Write(w => w.WriteNumber(value!));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteNumber_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var value = string.Empty;

        // Act
        var act = () => TestJson.Write(w => w.WriteNumber(value));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteBinary_Bytes_WritesBase64()
    {
        // Arrange
        var value = "this text is base64-encoded"u8.ToArray();

        // Act
        var json = TestJson.Write(w => w.WriteBinary(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"B":"dGhpcyB0ZXh0IGlzIGJhc2U2NC1lbmNvZGVk"}""");
    }

    [Test]
    public async Task WriteBinary_NoBytes_WritesEmptyBase64()
    {
        // Arrange
        byte[] value = [];

        // Act
        var json = TestJson.Write(w => w.WriteBinary(value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"B":""}""");
    }

    [Test]
    [Arguments(true, """{"BOOL":true}""")]
    [Arguments(false, """{"BOOL":false}""")]
    public async Task WriteBool_Value_WritesBoolDescriptor(bool value, string expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var json = TestJson.Write(w => w.WriteBool(value));

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task WriteNull_Always_WritesNullTrue()
    {
        // Arrange (nothing to arrange)

        // Act
        var json = TestJson.Write(static w => w.WriteNull());

        // Assert
        await Assert.That(json).IsEqualTo("""{"NULL":true}""");
    }

    [Test]
    public async Task WriteStartList_ElementsOfDifferentTypes_WritesListOfAttributeValues()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartList();
            w.WriteString("Cookies");
            w.WriteString("Coffee");
            w.WriteNumber(3.14159);
            w.WriteEndList();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"S":"Cookies"},{"S":"Coffee"},{"N":"3.14159"}]}""");
    }

    [Test]
    public async Task WriteStartList_NoElements_WritesEmptyList()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartList();
            w.WriteEndList();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[]}""");
    }

    [Test]
    public async Task WriteStartMap_NamesAsUtf8AndString_WritesMapOfAttributeValues()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartMap();
            w.WriteAttributeName("Name"u8);
            w.WriteString("Joe");
            w.WriteAttributeName("Age");
            w.WriteNumber(35);
            w.WriteEndMap();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"M":{"Name":{"S":"Joe"},"Age":{"N":"35"}}}""");
    }

    [Test]
    public async Task WriteStartMap_NestedInList_WritesNestedValues()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartList();
            w.WriteStartMap();
            w.WriteAttributeName("tags");
            w.WriteStartList();
            w.WriteNull();
            w.WriteEndList();
            w.WriteEndMap();
            w.WriteStartMap();
            w.WriteEndMap();
            w.WriteEndList();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"M":{"tags":{"L":[{"NULL":true}]}}},{"M":{}}]}""");
    }

    [Test]
    public async Task WriteStartStringSet_Elements_WritesStringSet()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartStringSet();
            w.WriteStringSetElement("Giraffe");
            w.WriteStringSetElement("Hippo");
            w.WriteStringSetElement("Zebra");
            w.WriteEndSet();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["Giraffe","Hippo","Zebra"]}""");
    }

    [Test]
    public async Task WriteStringSetElement_Utf8_WritesTheSameElement()
    {
        // Arrange
        // Act
        var json = TestJson.Write(static w =>
        {
            w.WriteStartStringSet();
            w.WriteStringSetElement("Zürich"u8);
            w.WriteEndSet();
        });

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["Zürich"]}""");
    }

    [Test]
    public async Task WriteStringSetElement_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act
        var act = () => TestJson.Write(w =>
        {
            w.WriteStartStringSet();
            w.WriteStringSetElement(value!);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    [MethodDataSource(nameof(LoneSurrogates))]
    public async Task WriteStringSetElement_LoneSurrogate_ThrowsArgumentException(string value)
    {
        // Arrange
        // Act
        var act = () => TestJson.Write(w =>
        {
            w.WriteStartStringSet();
            w.WriteStringSetElement(value);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteStringSetElement_SurrogatePairs_WritesThem()
    {
        // Arrange
        // Act
        var json = TestJson.Write(static w =>
        {
            w.WriteStartStringSet();
            w.WriteStringSetElement("a😀b😀");
            w.WriteEndSet();
        });

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["a\uD83D\uDE00b\uD83D\uDE00"]}""");
    }

    [Test]
    [MethodDataSource(nameof(LoneSurrogates))]
    public async Task WriteString_LoneSurrogate_ThrowsArgumentException(string value)
    {
        // Arrange
        // Act
        var act = () => TestJson.Write(w => w.WriteString(value));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("value");
    }

    [Test]
    [MethodDataSource(nameof(LoneSurrogates))]
    public async Task WriteString_LoneSurrogateInsideAList_ThrowsArgumentException(string value)
    {
        // Arrange
        // Act
        var act = () => TestJson.Write(w =>
        {
            w.WriteStartList();
            w.WriteString(value);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task WriteString_SurrogatePairs_WritesThem()
    {
        // Arrange
        // Act
        var json = TestJson.Write(static w => w.WriteString("a😀b😀"));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"a\uD83D\uDE00b\uD83D\uDE00"}""");
    }

    [Test]
    public async Task WriteAttributeName_LoneSurrogate_ThrowsArgumentException()
    {
        // Arrange
        // Act
        var act = () => TestJson.Write(static w =>
        {
            w.WriteStartItem();
            w.WriteAttributeName("a\uD800");
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task WriteStartNumberSet_NumbersAndText_WritesNumberSet()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartNumberSet();
            w.WriteNumberSetElement(42.2);
            w.WriteNumberSetElement("-19");
            w.WriteNumberSetElement(7.5m);
            w.WriteNumberSetElement(BigInteger.Pow(10, 70));
            w.WriteEndSet();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"NS":["42.2","-19","7.5","1{{new string('0', 70)}}"]}""");
    }

    [Test]
    public async Task WriteNumberSetElement_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act
        var act = () => TestJson.Write(w =>
        {
            w.WriteStartNumberSet();
            w.WriteNumberSetElement(value!);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteNumberSetElement_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var value = string.Empty;

        // Act
        var act = () => TestJson.Write(w =>
        {
            w.WriteStartNumberSet();
            w.WriteNumberSetElement(value);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteStartBinarySet_Elements_WritesBase64Set()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartBinarySet();
            w.WriteBinarySetElement("Sunny"u8);
            w.WriteBinarySetElement("Rainy"u8);
            w.WriteBinarySetElement("Snowy"u8);
            w.WriteEndSet();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"BS":["U3Vubnk=","UmFpbnk=","U25vd3k="]}""");
    }

    [Test]
    public async Task WriteStartItem_Attributes_WritesObjectWithoutTypeDescriptor()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartItem();
            w.WriteAttributeName("pk"u8);
            w.WriteString("USER#42");
            w.WriteAttributeName("roles"u8);
            w.WriteStartStringSet();
            w.WriteStringSetElement("admin");
            w.WriteEndSet();
            w.WriteEndItem();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"USER#42"},"roles":{"SS":["admin"]}}""");
    }

    [Test]
    public async Task WriteAttributeName_NameWithQuote_EscapesIt()
    {
        // Arrange
        const string name = "a\"b";

        // Act
        var json = TestJson.Write(w =>
        {
            w.WriteStartItem();
            w.WriteAttributeName(name);
            w.WriteBool(true);
            w.WriteEndItem();
        });

        // Assert
        await Assert.That(json).IsEqualTo("""{"a\"b":{"BOOL":true}}""");
    }

    [Test]
    public async Task WriteAttributeName_NullName_ThrowsArgumentNullException()
    {
        // Arrange
        string? name = null;

        // Act
        var act = () => TestJson.Write(w =>
        {
            w.WriteStartItem();
            w.WriteAttributeName(name!);
        });

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task WriteString_InsideItemWithoutAttributeName_ThrowsInvalidOperationException()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.WriteStartItem();
            w.WriteString("orphan");
        }

        // Act
        var act = () => TestJson.Write(Write);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Json_Always_ReturnsTheWrappedJsonWriter()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var json = WireJson.CreateWriter(buffer);

        // Act
        var wrapped = new DynamoDbWriter(json).Json;

        // Assert
        await Assert.That(wrapped).IsSameReferenceAs(json);
    }

    [Test]
    public async Task Json_EnvelopeAroundItem_WritesRequestBody()
    {
        // Arrange
        static void Write(DynamoDbWriter w)
        {
            w.Json.WriteStartObject();
            w.Json.WriteString("TableName"u8, "Music");
            w.Json.WritePropertyName("Key"u8);
            w.WriteStartItem();
            w.WriteAttributeName("Artist"u8);
            w.WriteString("Acme Band");
            w.WriteEndItem();
            w.Json.WriteEndObject();
        }

        // Act
        var json = TestJson.Write(Write);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Music","Key":{"Artist":{"S":"Acme Band"}}}""");
    }
}
