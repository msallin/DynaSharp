using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// The decision says 'a' is the string "a". A char is an INumberBase<char>, so it binds to every overload for numbers, and each of them must
// end in a string value: {"N":"a"} must not come out of any path. Each test looks at the values a command sends.
public sealed class CharacterValueTests
{
    private const string Value = """{":v0":{"S":"a"}}""";

    [Test]
    [Arguments("Set")]
    [Arguments("SetIfNotExists")]
    [Arguments("Add")]
    [Arguments("SetThroughAHandle")]
    public async Task Update_Character_IsAString(string action)
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = action switch
        {
            "Set" => fixture.Shop.Update("k").Set("x", 'a').ToRequestJson(),
            "SetIfNotExists" => fixture.Shop.Update("k").SetIfNotExists("x", 'a').ToRequestJson(),
            "Add" => fixture.Shop.Update("k").Add("x", 'a').ToRequestJson(),
            _ => fixture.Shop.Update("k").Set(Attr<char>("x"), 'a').ToRequestJson(),
        };

        // Assert
        await Assert.That(ValuesOf(json)).IsEqualTo(Value);
    }

    [Test]
    public async Task With_Character_IsAString()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Put().With("pk", "k").With("letter", 'a').ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"k"},"letter":{"S":"a"}}}""");
    }

    [Test]
    public async Task ItemInitializer_Character_IsAString()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Put(new Item { ["pk"] = 'k' }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"k"}}}""");
    }

    [Test]
    [Arguments("Eq")]
    [Arguments("Ne")]
    [Arguments("Lt")]
    [Arguments("Le")]
    [Arguments("Gt")]
    [Arguments("Ge")]
    public async Task If_ComparisonWithACharacter_IsAString(string comparison)
    {
        // Arrange
        using var fixture = new Fixture();
        var attribute = Attr("x");

        // Act
        var json = comparison switch
        {
            "Eq" => fixture.Shop.Update("k").If(attribute.Eq('a')).ToRequestJson(),
            "Ne" => fixture.Shop.Update("k").If(attribute.Ne('a')).ToRequestJson(),
            "Lt" => fixture.Shop.Update("k").If(attribute.Lt('a')).ToRequestJson(),
            "Le" => fixture.Shop.Update("k").If(attribute.Le('a')).ToRequestJson(),
            "Gt" => fixture.Shop.Update("k").If(attribute.Gt('a')).ToRequestJson(),
            _ => fixture.Shop.Update("k").If(attribute.Ge('a')).ToRequestJson(),
        };

        // Assert
        await Assert.That(ValuesOf(json)).IsEqualTo(Value);
    }

    [Test]
    public async Task If_Between_WritesTwoStrings()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("k").If(Attr("x").Between('a', 'b')).ToRequestJson();

        // Assert
        await Assert.That(ValuesOf(json)).IsEqualTo("""{":v0":{"S":"a"},":v1":{"S":"b"}}""");
    }

    [Test]
    public async Task If_HoleOfAnInterpolation_IsAString()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("k").If($"{Attr("x")} = {'a'}").ToRequestJson();

        // Assert
        await Assert.That(ValuesOf(json)).IsEqualTo(Value);
    }

    [Test]
    public async Task If_CharacterAndTheSameString_ShareAPlaceholder()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("k").Set("x", 'a').Set("y", "a").ToRequestJson();

        // Assert
        await Assert.That(ValuesOf(json)).IsEqualTo(Value);
    }

    [Test]
    public async Task Key_Character_IsAString()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Get('k', 's').ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"k"},"sk":{"S":"s"}}}""");
    }

    [Test]
    [Arguments("Eq")]
    [Arguments("Lt")]
    [Arguments("Le")]
    [Arguments("Gt")]
    [Arguments("Ge")]
    [Arguments("Between")]
    public async Task SortKey_Character_IsAString(string comparison)
    {
        // Arrange
        var key = comparison switch
        {
            "Eq" => SortKey.Eq('a'),
            "Lt" => SortKey.Lt('a'),
            "Le" => SortKey.Le('a'),
            "Gt" => SortKey.Gt('a'),
            "Ge" => SortKey.Ge('a'),
            _ => SortKey.Between('a', 'a'),
        };
        var writer = new DynaSharp.Fluent.ExpressionWriter(System.Buffers.ArrayPool<byte>.Shared);
        using var body = new PooledByteBuffer();

        // Act
        key.Render(writer, writer[DynaSharp.Fluent.Clause.KeyCondition], "sk");
        writer.WriteMembers(body);

        // Assert
        await Assert.That(ValuesOf("{" + System.Text.Encoding.UTF8.GetString(body.WrittenSpan)[1..] + "}")).IsEqualTo(Value);
    }

    [Test]
    public async Task FromNumber_Character_IsAString()
    {
        // Arrange
        const char letter = 'a';

        // Act
        var value = AttributeValue.FromNumber(letter);

        // Assert
        await Assert.That(value).IsEqualTo((AttributeValue)"a");
    }

    [Test]
    public async Task WriteNumber_Character_IsAStringInAListToo()
    {
        // Arrange
        const string expected = """{"L":[{"S":"a"}]}""";

        // Act
        var json = TestJson.Write(static writer =>
        {
            writer.WriteStartList();
            writer.WriteNumber('a');
            writer.WriteEndList();
        });

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task WriteNumberSetElement_Character_Throws()
    {
        // Arrange
        const char letter = 'a';

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(TestJson.Write(static writer =>
        {
            writer.WriteStartNumberSet();
            writer.WriteNumberSetElement(letter);
            writer.WriteEndSet();
        })));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("value");
    }

    [Test]
    public async Task NumberConverter_Character_RoundTripsAsAString()
    {
        // Arrange
        const char letter = 'a';

        // Act
        var json = TestJson.Write(static writer => NumberConverter<char>.Write(writer, letter));
        var read = TestJson.Read(json, static (ref reader) => reader.ReadAttribute<char, NumberConverter<char>>());

        // Assert
        await Assert.That((json, read)).IsEqualTo(("""{"S":"a"}""", letter));
    }

    [Test]
    [Arguments("""{"S":"ab"}""")]
    [Arguments("""{"S":""}""")]
    [Arguments("""{"N":"1"}""")]
    public async Task NumberConverter_ValueThatIsNoCharacter_Throws(string json)
    {
        // Arrange
        var text = json;

        // Act
        var failure = await TestClient.CaptureAsync<JsonException>(() => Task.FromResult(TestJson.Read(text, static (ref reader) => reader.ReadAttribute<char, NumberConverter<char>>())));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task SendAsync_ReturnOfACharacter_ReadsWhatItWrote()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"x":{"S":"a"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var letter = await client.Table("Shop", "pk").Update("k").Set("x", 'a').Return(Attr<char>("x")).SendAsync();

        // Assert
        await Assert.That(letter).IsEqualTo('a');
    }

    private static string ValuesOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("ExpressionAttributeValues").GetRawText();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly StubHandler _handler = new();
        private readonly DynamoDbClient _client;

        public Fixture()
        {
            _client = TestClient.Create(_handler);
            Shop = _client.Table("Shop", "pk", "sk");
        }

        public Table<Item> Shop { get; }

        public void Dispose()
        {
            _client.Dispose();
            _handler.Dispose();
        }
    }
}
