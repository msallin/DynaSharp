using System.Numerics;
using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

// The direct way of DynamoDbWriter must give the bytes of the JSON writer, or a body would depend on which path a value took. Every test
// writes a value both ways.
public sealed class DirectWriterTests
{
    public static IEnumerable<string> Strings()
    {
        // Every character of the first plane that JSON treats specially, and each one between plain ones.
        for (var c = 0; c < 256; c++)
        {
            yield return ((char)c).ToString();
        }

        yield return string.Empty;
        yield return "plain text with spaces";
        yield return "a\"b\\c";
        yield return "😀";
        yield return "</script>&'+";
        yield return new string('x', 5000);
    }

    [Test]
    public async Task WriteString_EveryKindOfText_IsTheSameAsTheJsonWriter()
    {
        // Arrange
        var strings = Strings().ToList();

        // Act
        var differences = strings.Where(text => Direct(writer => writer.WriteString(text)) != TestJson.Write(writer => writer.WriteString(text))).ToList();

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task WriteString_LoneSurrogate_ThrowsArgumentExceptionAsTheJsonWriterWay()
    {
        // Arrange
        // Act
        var act = () => Direct(static writer => writer.WriteString("a\uD800"));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("value");
    }

    [Test]
    public async Task WriteNumber_TextThatNeedsEscaping_IsTheSameAsTheJsonWriter()
    {
        // Arrange
        string[] numbers = ["1", "-1.5E+10", "not\"a\"number"];

        // Act
        var differences = numbers.Where(number => Direct(writer => writer.WriteNumber(number)) != TestJson.Write(writer => writer.WriteNumber(number))).ToList();

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task WriteNumber_EveryNumberType_IsTheSameAsTheJsonWriter()
    {
        // Arrange
        var expected = new[]
        {
            TestJson.Write(writer => writer.WriteNumber(int.MinValue)),
            TestJson.Write(writer => writer.WriteNumber(long.MaxValue)),
            TestJson.Write(writer => writer.WriteNumber(1249.95m)),
            TestJson.Write(writer => writer.WriteNumber(1.5E+300)),
        };

        // Act
        var actual = new[]
        {
            Direct(writer => writer.WriteNumber(int.MinValue)),
            Direct(writer => writer.WriteNumber(long.MaxValue)),
            Direct(writer => writer.WriteNumber(1249.95m)),
            Direct(writer => writer.WriteNumber(1.5E+300)),
        };

        // Assert
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task WriteNumber_TooLongForTheStack_TakesTheJsonWriter()
    {
        // Arrange
        var large = BigInteger.Pow(10, 100);

        // Act
        var json = Direct(writer => writer.WriteNumber(large));

        // Assert
        await Assert.That(json).IsEqualTo(TestJson.Write(writer => writer.WriteNumber(large)));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task WriteBool_Value_IsTheSameAsTheJsonWriter(bool value)
    {
        // Arrange
        var expected = TestJson.Write(writer => writer.WriteBool(value));

        // Act
        var json = Direct(writer => writer.WriteBool(value));

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task WriteString_InsideAList_GoesThroughTheJsonWriter()
    {
        // Arrange
        var expected = """{"L":[{"S":"a"},{"N":"1"},{"BOOL":true},{"N":"2"}]}""";

        // Act
        var json = Direct(writer =>
        {
            writer.WriteStartList();
            writer.WriteString("a");
            writer.WriteNumber(1);
            writer.WriteBool(true);
            writer.WriteNumber("2");
            writer.WriteEndList();
        });

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    private static string Direct(Action<DynamoDbWriter> write)
    {
        using var buffer = new PooledByteBuffer();
        using var json = WireJson.CreateWriter(buffer);
        write(new DynamoDbWriter(json, buffer));
        json.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
