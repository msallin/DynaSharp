using System.Buffers;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

public sealed class PlaceholderMapsTests
{
    [Test]
    [Arguments("status", "#n0", """{"#n0":"status"}""")]
    [Arguments("address.city", "#n0.#n1", """{"#n0":"address","#n1":"city"}""")]
    [Arguments("lines[2].sku", "#n0[2].#n1", """{"#n0":"lines","#n1":"sku"}""")]
    [Arguments("matrix[0][1]", "#n0[0][1]", """{"#n0":"matrix"}""")]
    [Arguments("a[10].b[20].c", "#n0[10].#n1[20].#n2", """{"#n0":"a","#n1":"b","#n2":"c"}""")]
    [Arguments("a.a.a", "#n0.#n0.#n0", """{"#n0":"a"}""")]
    public async Task AppendPath_ValidPath_WritesPlaceholders(string path, string expected, string names)
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendPath(text, path);

        // Assert
        await Assert.That((Text(text), Map(maps, "ExpressionAttributeNames"))).IsEqualTo((expected, names));
    }

    [Test]
    [Arguments("a..b")]
    [Arguments(".a")]
    [Arguments("a.")]
    [Arguments("[1]")]
    [Arguments("a[")]
    [Arguments("a[]")]
    [Arguments("a[x]")]
    [Arguments("a[1")]
    [Arguments("a[-1]")]
    [Arguments("a[1]b")]
    [Arguments("a[1]..b")]
    [Arguments("")]
    public async Task AppendPath_InvalidPath_ThrowsNamingIt(string path)
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            maps.AppendPath(text, path);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains($"\"{path}\"");
    }

    [Test]
    public async Task AppendPath_NullPath_Throws()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() =>
        {
            maps.AppendPath(text, null!);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("path");
    }

    [Test]
    public async Task AppendPath_NameThatNeedsEscaping_EncodesIt()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendPath(text, "a\"b");

        // Assert
        await Assert.That(Map(maps, "ExpressionAttributeNames")).IsEqualTo("""{"#n0":"a\"b"}""");
    }

    [Test]
    public async Task AppendPath_SameEscapedNameTwice_SharesThePlaceholder()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendPath(text, "é\"");
        maps.AppendPath(text, "é\"");

        // Assert
        await Assert.That(Text(text)).IsEqualTo("#n0#n0");
    }

    [Test]
    public async Task AppendPath_NamesThatDifferByCase_GetTwoPlaceholders()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendPath(text, "name");
        maps.AppendPath(text, "Name");
        maps.AppendPath(text, "nam");

        // Assert
        await Assert.That(Text(text)).IsEqualTo("#n0#n1#n2");
    }

    [Test]
    public async Task AppendPath_MoreNamesThanTheFirstCapacity_Grows()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        for (var i = 0; i < 40; i++)
        {
            maps.AppendPath(text, "name" + i);
        }

        maps.AppendPath(text, "name39");

        // Assert
        await Assert.That(Text(text)).EndsWith("#n39#n39");
    }

    [Test]
    public async Task AppendValue_EqualValues_ShareAPlaceholder()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendValue<string, StringConverter>(text, "open");
        maps.AppendValue<AttributeValue, AttributeValueConverter>(text, "open");
        maps.AppendValue<int, NumberConverter<int>>(text, 1);
        maps.AppendValue<long, NumberConverter<long>>(text, 1);

        // Assert
        await Assert.That((Text(text), Map(maps, "ExpressionAttributeValues"))).IsEqualTo((":v0:v0:v1:v1", """{":v0":{"S":"open"},":v1":{"N":"1"}}"""));
    }

    [Test]
    public async Task AppendValue_EqualValuesOfOtherTypes_GetTwoPlaceholders()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendValue<string, StringConverter>(text, "1");
        maps.AppendValue<int, NumberConverter<int>>(text, 1);

        // Assert
        await Assert.That(Text(text)).IsEqualTo(":v0:v1");
    }

    [Test]
    public async Task AppendValue_MoreValuesThanTheFirstCapacity_Grows()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        for (var i = 0; i < 40; i++)
        {
            maps.AppendValue<int, NumberConverter<int>>(text, i);
        }

        maps.AppendValue<int, NumberConverter<int>>(text, 39);

        // Assert
        await Assert.That(Text(text)).EndsWith(":v39:v39");
    }

    [Test]
    public async Task AppendValue_ValueThatNeedsEscaping_TakesTheWriter()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendValue<string, StringConverter>(text, "plain");
        maps.AppendValue<string, StringConverter>(text, "a\"b");
        maps.AppendValue<string, StringConverter>(text, "plain again");

        // Assert
        await Assert.That(Map(maps, "ExpressionAttributeValues")).IsEqualTo("""{":v0":{"S":"plain"},":v1":{"S":"a\"b"},":v2":{"S":"plain again"}}""");
    }

    [Test]
    public async Task AppendValue_ListOfValues_TakesTheWriter()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();

        // Act
        maps.AppendValue<AttributeValue, AttributeValueConverter>(text, AttributeValue.FromList(["a", 1, true]));
        maps.AppendValue<string, StringConverter>(text, "after");

        // Assert
        await Assert.That(Map(maps, "ExpressionAttributeValues")).IsEqualTo("""{":v0":{"L":[{"S":"a"},{"N":"1"},{"BOOL":true}]},":v1":{"S":"after"}}""");
    }

    [Test]
    public async Task IndexOfName_NameWithAndWithoutAPlaceholder_FindsOnlyTheOne()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();
        maps.AppendPath(text, "a");
        maps.AppendPath(text, "b");

        // Act
        var found = (maps.IndexOfName("b"u8), maps.IndexOfName("c"u8));

        // Assert
        await Assert.That(found).IsEqualTo((1, -1));
    }

    [Test]
    public async Task NameAt_EscapedName_UnescapesWhatTheMapHoldsQuoted()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();
        maps.AppendPath(text, "a\"b");

        // Act
        var name = (maps.NameAt(0), Encoding.UTF8.GetString(maps.QuotedNameAt(0)));

        // Assert
        await Assert.That(name).IsEqualTo(("a\"b", "\"a\\\"b\""));
    }

    [Test]
    public async Task ValueAt_SecondValue_IsItsJson()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();
        maps.AppendValue<string, StringConverter>(text, "a");
        maps.AppendValue<int, NumberConverter<int>>(text, 1);

        // Act
        var value = Encoding.UTF8.GetString(maps.ValueAt(1));

        // Assert
        await Assert.That(value).IsEqualTo("{\"N\":\"1\"}");
    }

    [Test]
    public async Task CopyFrom_Maps_DeduplicatesAgainstTheCopy()
    {
        // Arrange
        var original = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();
        original.AppendPath(text, "a");
        original.AppendValue<int, NumberConverter<int>>(text, 1);
        var copy = new PlaceholderMaps(ArrayPool<byte>.Shared);
        copy.CopyFrom(in original);
        text.Truncate(0);

        // Act
        copy.AppendPath(text, "a");
        copy.AppendValue<int, NumberConverter<int>>(text, 1);

        // Assert
        await Assert.That((Text(text), Members(copy))).IsEqualTo(("#n0:v0", Members(original)));
    }

    [Test]
    public async Task AddPartitionKey_AfterAValue_IsTheLastValueOutsideTheNumbering()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();
        maps.AppendValue<string, StringConverter>(text, "a");

        // Act
        maps.AddPartitionKey("p");

        // Assert
        await Assert.That(Map(maps, "ExpressionAttributeValues")).IsEqualTo("{\":v0\":{\"S\":\"a\"},\":pk\":{\"S\":\"p\"}}");
    }

    [Test]
    public async Task Clear_Maps_WritesNothingAfterwards()
    {
        // Arrange
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer();
        maps.AppendPath(text, "a");
        maps.AppendValue<int, NumberConverter<int>>(text, 1);

        // Act
        maps.Clear(maxRetainedLength: 0);

        // Assert
        await Assert.That((Members(maps), maps.IndexOfName("a"u8))).IsEqualTo((string.Empty, -1));
    }

    private static string Text(PooledByteBuffer buffer) => Encoding.UTF8.GetString(buffer.WrittenSpan);

    private static string Members(in PlaceholderMaps maps)
    {
        using var body = new PooledByteBuffer();
        maps.WriteMembers(body);
        return Text(body);
    }

    private static string Map(in PlaceholderMaps maps, string member)
    {
        var text = Members(maps);
        var start = text.IndexOf($"\"{member}\":", StringComparison.Ordinal) + member.Length + 3;
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            depth += text[i] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };
            if (depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        throw new InvalidOperationException("The map does not end.");
    }
}
