using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>The reader members that generated code and converters use to avoid creating strings: UTF-8 names and strings, <c>ReadAttribute</c> and <c>GetNumber</c>.</summary>
public sealed class DynamoDbReaderUtf8Tests
{
    [Test]
    public async Task GetAttributeNameUtf8_PlainName_ReturnsItsBytes()
    {
        // Arrange
        const string json = """{"héllo":{"S":"x"}}""";

        // Act
        var name = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartItem();
            reader.TryReadAttributeName();
            return Encoding.UTF8.GetString(reader.GetAttributeNameUtf8());
        });

        // Assert
        await Assert.That(name).IsEqualTo("héllo");
    }

    [Test]
    public async Task GetAttributeNameUtf8_EscapedName_ReturnsTheUnescapedBytes()
    {
        // Arrange: the name "abc" with its first character escaped, built from parts so that no tool turns the escape into a character
        var json = "{\"\\u0061bc\":{\"S\":\"x\"}}";

        // Act
        var name = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartItem();
            reader.TryReadAttributeName();
            return Encoding.UTF8.GetString(reader.GetAttributeNameUtf8());
        });

        // Assert
        await Assert.That(name).IsEqualTo("abc");
    }

    [Test]
    public async Task GetAttributeNameUtf8_NotOnAName_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => TestJson.Read("""{"a":{"S":"x"}}""", static (ref reader) =>
        {
            reader.ReadStartItem();
            return reader.GetAttributeNameUtf8().Length;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot read an attribute name from the JSON token StartObject.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetAttributeNameUtf8_KnownName_AllocatesNothing()
    {
        // Arrange
        var json = """{"Count":{"N":"1"}}"""u8.ToArray();
        ReadNames(json);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        ReadNames(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task GetStringUtf8_PlainString_ReturnsItsBytes()
    {
        // Arrange
        const string json = """{"S":"héllo"}""";

        // Act
        var text = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartAttribute();
            return Encoding.UTF8.GetString(reader.GetStringUtf8());
        });

        // Assert
        await Assert.That(text).IsEqualTo("héllo");
    }

    [Test]
    public async Task GetStringUtf8_EscapedString_ReturnsTheUnescapedBytes()
    {
        // Arrange: "aB\n" with the first two characters escaped, built from parts so that no tool turns the escapes into characters
        var json = "{\"S\":\"\\u0061\\u0042\\n\"}";

        // Act
        var text = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartAttribute();
            return Encoding.UTF8.GetString(reader.GetStringUtf8());
        });

        // Assert
        await Assert.That(text).IsEqualTo("aB\n");
    }

    [Test]
    public async Task GetStringUtf8_SetElement_ReturnsItsBytes()
    {
        // Arrange
        const string json = """{"SS":["one"]}""";

        // Act
        var text = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartAttribute();
            reader.TryReadSetElement();
            return Encoding.UTF8.GetString(reader.GetStringUtf8());
        });

        // Assert
        await Assert.That(text).IsEqualTo("one");
    }

    [Test]
    public async Task GetStringUtf8_NotOnAString_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => TestJson.Read("""{"a":{"S":"x"}}""", static (ref reader) =>
        {
            reader.ReadStartItem();
            return reader.GetStringUtf8().Length;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot read a string from the JSON token StartObject.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadAttribute_TwoAttributesOfAnItem_LeavesTheReaderAfterEachValue()
    {
        // Arrange
        const string json = """{"a":{"N":"1"},"b":{"S":"x"}}""";

        // Act
        var result = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartItem();
            reader.TryReadAttributeName();
            var a = reader.ReadAttribute<int, NumberConverter<int>>();
            reader.TryReadAttributeName();
            var b = reader.ReadAttribute<string, StringConverter>();
            var atEnd = !reader.TryReadAttributeName();
            return (a, b, atEnd);
        });

        // Assert
        await Assert.That(result).IsEqualTo((1, "x", true));
    }

    [Test]
    public async Task GetNumber_Number_ReturnsIt()
    {
        // Arrange
        // Act
        var value = TestJson.Read("""{"N":"42"}""", static (ref reader) =>
        {
            reader.ReadStartAttribute();
            return reader.GetNumber<int>();
        });

        // Assert
        await Assert.That(value).IsEqualTo(42);
    }

    [Test]
    [Arguments("""{"N":"secret-7"}""")]
    [Arguments("{\"N\":\"\\u0073ecret-7\"}")]
    public async Task GetNumber_TextThatIsNoNumber_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange (a numeric enum reads through here, so a stored value must not reach the message)
        // Act
        var act = () => TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartAttribute();
            return reader.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>().WithMessage("The text is not a number.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"N":"2147483648"}""")]
    [Arguments("""{"N":"1.5"}""")]
    public async Task GetNumber_NumberThatDoesNotFit_ThrowsOverflowExceptionWithoutTheNumber(string json)
    {
        // Arrange
        // Act
        var act = () => TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartAttribute();
            return reader.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>().WithMessage("The number does not fit the target type.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetNumber_EscapedDigits_ReturnsTheNumber()
    {
        // Arrange: "42" as JSON escapes, built from parts so that no tool turns them into characters
        var json = "{\"N\":\"\\u0034\\u0032\"}";

        // Act
        var value = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartAttribute();
            return reader.GetNumber<int>();
        });

        // Assert
        await Assert.That(value).IsEqualTo(42);
    }

    [Test]
    public async Task GetNumber_NotOnAString_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => TestJson.Read("""{"N":"1"}""", static (ref reader) =>
        {
            reader.ReadStartItem();
            return reader.GetNumber<int>();
        });

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("Cannot read a number from the JSON token StartObject.", StringComparison.Ordinal);
    }

    private static void ReadNames(byte[] json)
    {
        for (var i = 0; i < 100; i++)
        {
            var reader = new DynamoDbReader(json);
            reader.ReadStartItem();
            reader.TryReadAttributeName();
            _ = reader.GetAttributeNameUtf8().Length;
        }
    }
}
