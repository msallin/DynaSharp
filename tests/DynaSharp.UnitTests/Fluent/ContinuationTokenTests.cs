using System.Buffers.Text;
using System.Text;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using static DynaSharp.UnitTests.Fluent.PageJson;

namespace DynaSharp.UnitTests.Fluent;

public sealed class ContinuationTokenTests
{
    [Test]
    public async Task Default_HoldsNoPosition()
    {
        // Arrange
        var token = default(ContinuationToken);

        // Act
        var (hasValue, text, key) = (token.HasValue, token.ToString(), token.ToKey());

        // Assert
        await Assert.That((hasValue, text, key)).IsEqualTo((false, string.Empty, null));
    }

    [Test]
    public async Task Parse_EmptyText_IsTheDefaultValue()
    {
        // Arrange
        var text = default(ContinuationToken).ToString();

        // Act
        var token = ContinuationToken.Parse(text);

        // Assert
        await Assert.That(token).IsEqualTo(default(ContinuationToken));
    }

    [Test]
    public async Task Parse_Null_IsTheDefaultValue()
    {
        // Arrange (the first page of a web client has no token)
        string? text = null;

        // Act
        var token = ContinuationToken.Parse(text);

        // Assert
        await Assert.That(token).IsEqualTo(default(ContinuationToken));
    }

    [Test]
    public async Task TryParse_Null_IsTheDefaultValue()
    {
        // Arrange
        string? text = null;

        // Act
        var parsed = ContinuationToken.TryParse(text, out var token);

        // Assert
        await Assert.That((parsed, token)).IsEqualTo((true, default(ContinuationToken)));
    }

    [Test]
    public async Task TryParse_Token_IsTrue()
    {
        // Arrange
        var text = ContinuationToken.FromKey(new Item { ["pk"] = "a" }).ToString();

        // Act
        var parsed = ContinuationToken.TryParse(text, out var token);

        // Assert
        await Assert.That((parsed, token.ToString())).IsEqualTo((true, text));
    }

    [Test]
    [Arguments("not base64!")]
    [Arguments("a")]
    [Arguments("====")]
    [Arguments("é")]
    [Arguments(" ")]
    public async Task Parse_NotBase64_ThrowsAFormatException(string text)
    {
        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This is not a continuation token");
    }

    // What decodes well and still is no key. The last ones are what a client would send to get its own members into the request.
    [Test]
    [Arguments("hello")]
    [Arguments("{")]
    [Arguments("[]")]
    [Arguments("null")]
    [Arguments("1")]
    [Arguments("{}")]
    [Arguments("""{"pk":1}""")]
    [Arguments("""{"pk":{"S":1}}""")]
    [Arguments("""{"pk":{"X":"a"}}""")]
    [Arguments("""{"pk":{"S":"a"}""")]
    [Arguments("""{"pk":{"B":"not base64!"}}""")]
    [Arguments("""{"pk":{"SS":[]}}""")]
    [Arguments("""{"pk":{"S":"a"},"pk":{"S":"b"}}""")]
    [Arguments("""{"pk":{"S":"a"}} """ + """{"pk":{"S":"b"}}""")]
    [Arguments("""{"pk":{"S":"a"}},"TableName":"Other","x":{"S":"b"}""")]
    [Arguments("""{"pk":{"S":"a"}}},"TableName":"Other"}""")]
    [Arguments("""{"pk":{"S":"a"}} /* comment */""")]
    public async Task Parse_DecodesToWhatIsNoKey_ThrowsAFormatException(string json)
    {
        // Arrange
        var text = Versioned(Encoding.UTF8.GetBytes(json));

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This is not a continuation token");
    }

    // JSON that is well-formed for a tokenizer and falls apart when its text is read.
    [Test]
    [Arguments(new byte[] { 0x7B, 0x22, 0x70, 0x22, 0x3A, 0x7B, 0x22, 0x53, 0x22, 0x3A, 0x22, 0xFF, 0x22, 0x7D, 0x7D })]
    [Arguments(new byte[] { 0x7B, 0x22, 0xFF, 0x22, 0x3A, 0x7B, 0x22, 0x53, 0x22, 0x3A, 0x22, 0x61, 0x22, 0x7D, 0x7D })]
    public async Task Parse_TextThatIsNotUtf8_ThrowsAFormatException(byte[] json)
    {
        // Arrange
        var text = Versioned(json);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This is not a continuation token");
    }

    [Test]
    public async Task Parse_LoneSurrogate_ThrowsAFormatException()
    {
        // Arrange
        var text = Versioned(Encoding.UTF8.GetBytes("{\"pk\":{\"S\":\"\\" + "uD800\"}}"));

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This is not a continuation token");
    }

    [Test]
    public async Task Parse_Failure_DoesNotQuoteTheText()
    {
        // Arrange
        var text = Versioned("""{"pk":{"S":"key-secret"} """u8);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).DoesNotContain(text);
    }

    [Test]
    public async Task ToString_Token_StartsWithTheFormatVersion()
    {
        // Arrange
        var token = ContinuationToken.FromKey(new Item { ["pk"] = "a" });

        // Act
        var bytes = Base64Url.DecodeFromChars(token.ToString());

        // Assert
        await Assert.That((bytes[0], Encoding.UTF8.GetString(bytes[1..]))).IsEqualTo(((byte)1, """{"pk":{"S":"a"}}"""));
    }

    [Test]
    public async Task Next_OfAPage_StartsWithTheFormatVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Orders", "pk", "sk").Query("a").PageAsync();

        // Assert
        await Assert.That(Base64Url.DecodeFromChars(page.Next.ToString())[0]).IsEqualTo((byte)1);
    }

    // The text of a token as it was before tokens had a version: the JSON alone, whose first byte is a brace.
    [Test]
    public async Task Parse_TokenOfTheShapeWithoutAVersion_SaysThatItIsOfAnotherFormat()
    {
        // Arrange
        var text = Base64Url.EncodeToString("""{"pk":{"S":"a"}}"""u8);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token has a format");
    }

    [Test]
    [Arguments((byte)0)]
    [Arguments((byte)3)]
    [Arguments((byte)255)]
    public async Task Parse_UnknownVersion_SaysThatItIsOfAnotherFormat(byte version)
    {
        // Arrange
        var text = Base64Url.EncodeToString([version, .. """{"pk":{"S":"a"}}"""u8]);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token has a format");
    }

    [Test]
    public async Task Parse_UnknownVersion_DoesNotQuoteTheText()
    {
        // Arrange
        var text = Base64Url.EncodeToString([3, .. """{"pk":{"S":"key-secret"}}"""u8]);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message.Contains(text, StringComparison.Ordinal) || failure.Message.Contains("key-secret", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task TryParse_UnknownVersion_IsFalseAndTheDefaultValue()
    {
        // Arrange
        var text = Base64Url.EncodeToString("""{"pk":{"S":"a"}}"""u8);

        // Act
        var parsed = ContinuationToken.TryParse(text, out var token);

        // Assert
        await Assert.That((parsed, token)).IsEqualTo((false, default(ContinuationToken)));
    }

    [Test]
    public async Task Parse_VersionAndNoKey_IsNoToken()
    {
        // Arrange
        var text = Base64Url.EncodeToString([1]);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This is not a continuation token");
    }

    [Test]
    public async Task Parse_SpacesInTheJson_AreNoProblem()
    {
        // Arrange
        var text = Versioned("""  { "pk" : { "S" : "a" } }  """u8);

        // Act
        var token = ContinuationToken.Parse(text);

        // Assert
        await Assert.That(token.ToKey()!.GetString("pk")).IsEqualTo("a");
    }

    [Test]
    public async Task ToString_Text_IsSafeInAUrl()
    {
        // Arrange: bytes that take the characters a URL would need escaped in plain base64
        var key = new Item { ["pk"] = AttributeValue.FromBinary([0xFB, 0xFF, 0xFE]), ["sk"] = "?>~" };

        // Act
        var text = ContinuationToken.FromKey(key).ToString();

        // Assert
        await Assert.That(Uri.EscapeDataString(text)).IsEqualTo(text);
    }

    [Test]
    public async Task ToKey_FromKey_RoundTrips()
    {
        // Arrange
        var key = new Item { ["pk"] = "CUSTOMER#café", ["sk"] = AttributeValue.FromNumber("12.50"), ["gsi"] = AttributeValue.FromBinary([0, 255]) };

        // Act
        var token = ContinuationToken.Parse(ContinuationToken.FromKey(key).ToString());

        // Assert
        await Assert.That(token.ToKey()).IsEqualTo(key);
    }

    [Test]
    public async Task FromKey_DictionaryOfTheRequestApi_Works()
    {
        // Arrange
        IReadOnlyDictionary<string, AttributeValue> key = new Dictionary<string, AttributeValue> { ["pk"] = "a" };

        // Act
        var token = ContinuationToken.FromKey(key);

        // Assert
        await Assert.That(token.ToKey()!.GetString("pk")).IsEqualTo("a");
    }

    [Test]
    public async Task FromKey_Null_Throws()
    {
        // Arrange
        Item? key = null;

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(ContinuationToken.FromKey(key!)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("key");
    }

    [Test]
    public async Task FromKey_NoAttributes_Throws()
    {
        // Arrange
        var key = new Item();

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(ContinuationToken.FromKey(key)));

        // Assert
        await Assert.That((failure.ParamName, failure.Message)).IsEqualTo(("key", "A key has attributes. An empty one is no position in a read. (Parameter 'key')"));
    }

    [Test]
    public async Task Equals_SameKey_IsEqual()
    {
        // Arrange
        var first = ContinuationToken.FromKey(new Item { ["pk"] = "a" });
        var second = ContinuationToken.FromKey(new Item { ["pk"] = "a" });

        // Act
        var (equal, sameHash, other) = (first == second, first.GetHashCode() == second.GetHashCode(), first == ContinuationToken.FromKey(new Item { ["pk"] = "b" }));

        // Assert
        await Assert.That((equal, sameHash, other)).IsEqualTo((true, true, false));
    }

    // The real shape of a key: what botocore read from LastEvaluatedKey in a response of the service, against what comes out of a token that went through a client.
    [Test]
    public async Task Next_RealLastEvaluatedKey_RoundTripsAsText()
    {
        // Arrange
        var vector = OperationVectors.Response("Query", "PropertiesInAnotherOrder");
        using var handler = new StubHandler(_ => StubResponse.Json(vector.Wire));
        using var client = TestClient.Create(handler);
        var first = await client.Table("Orders", "pk", "sk").Query("CUSTOMER#1").PageAsync();
        var response = await client.QueryAsync(TestClient.CreateQuery());

        // Act
        var parsed = ContinuationToken.Parse(first.Next.ToString());

        // Assert
        await Assert.That(new Item(response.LastEvaluatedKey!).Equals(parsed.ToKey())).IsTrue();
    }

    [Test]
    public async Task Next_RealLastEvaluatedKey_GoesBackAsItCame()
    {
        // Arrange
        const string Key = """{"pk":{"S":"CUSTOMER#1"},"sk":{"B":"AAE="},"status":{"N":"1.50"}}""";
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Items(Key, "1")));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Orders", "pk", "sk");
        var first = await shop.Query("a").PageAsync();

        // Act
        await shop.Query("a").PageAsync(ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That(handler.Requests[1].BodyText).EndsWith(",\"ExclusiveStartKey\":" + Key + "}");
    }

    [Test]
    public async Task ToKey_OfANext_ContinuesInTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);
        var first = await client.Table("Orders", "pk", "sk").Query("a").PageAsync();

        // Act
        await client.QueryAsync(TestClient.CreateQuery() with { ExclusiveStartKey = first.Next.ToKey() });

        // Assert
        await Assert.That(handler.Requests[1].BodyText).Contains("\"ExclusiveStartKey\":" + Key("1"));
    }

    [Test]
    public async Task FromKey_OfALastEvaluatedKey_ContinuesInACommand()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);
        var response = await client.QueryAsync(TestClient.CreateQuery());

        // Act
        await client.Table("Orders", "pk", "sk").Query("a").PageAsync(ContinuationToken.FromKey(response.LastEvaluatedKey!));

        // Assert
        await Assert.That(handler.Requests[1].BodyText).EndsWith(",\"ExclusiveStartKey\":" + Key("1") + "}");
    }

    private static string Versioned(ReadOnlySpan<byte> json) => Base64Url.EncodeToString([1, .. json]);
}
