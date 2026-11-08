using System.Buffers.Text;
using System.Text;
using DynaSharp.Testing;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.PageJson;

namespace DynaSharp.UnitTests.Fluent;

// Signed tokens as a web API sees them: a page hands out Next.ToString(), the client sends back what it likes, and the next request parses it and reads on.
public sealed class ContinuationTokenSignerTests
{
    private const int SignatureLength = 32;

    private static readonly byte[] KeyA = SecretKey(0xA1);
    private static readonly byte[] KeyB = SecretKey(0xB2);

    private static readonly AttributeValue[] TwoPartitions = ["p0", "p1"];

    [Test]
    public async Task Constructor_NoKey_Throws()
    {
        // Act
        var failure = TestClient.Catch(() => _ = new ContinuationTokenSigner());

        // Assert
        await Assert.That(failure is ArgumentException { ParamName: "keys", Message: "A signer needs a key to sign with. (Parameter 'keys')" }).IsTrue();
    }

    [Test]
    public async Task Constructor_NullKey_Throws()
    {
        // Act
        var failure = TestClient.Catch(() => _ = new ContinuationTokenSigner(KeyA, null!));

        // Assert
        await Assert.That(failure is ArgumentNullException { ParamName: "keys" }).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(ContinuationTokenSigner.MinKeyLength - 1)]
    public async Task Constructor_ShortKey_ThrowsNamingTheKey(int length)
    {
        // Act
        var failure = TestClient.Catch(() => _ = new ContinuationTokenSigner(KeyA, new byte[length]));

        // Assert
        await Assert.That((failure is ArgumentException { ParamName: "keys" }, failure.Message.StartsWith($"Key 1 has {length} bytes", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Constructor_KeyClearedAfterwards_StillSignsWithItsCopy()
    {
        // Arrange
        var key = SecretKey(0xA1);
        var signer = new ContinuationTokenSigner(key);
        Array.Clear(key);
        var text = await FirstNextAsync(signer, static shop => shop.Query("a").PageAsync());

        // Act
        var page = await ReadAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That(page).IsEqualTo(Key("1"));
    }

    [Test]
    public async Task Next_OfASigningClient_IsTheKeyAndASignature()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var bytes = Base64Url.DecodeFromChars(text);

        // Assert
        await Assert.That((bytes[0], Encoding.UTF8.GetString(bytes[1..^SignatureLength]), bytes.Length)).IsEqualTo(((byte)2, Key("1"), 1 + Key("1").Length + SignatureLength));
    }

    [Test]
    public async Task PageAsync_SignedTokenThroughItsText_ContinuesFromItsKey()
    {
        // Arrange
        var signer = new ContinuationTokenSigner(KeyA);
        var text = await FirstNextAsync(signer, static shop => shop.Query("a").PageAsync());

        // Act
        var startKey = await ReadAsync(signer, text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That(startKey).IsEqualTo(Key("1"));
    }

    [Test]
    public async Task PagesAsync_OfASigningClient_SignsEveryNext()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last("2"));
        using var client = Signing(handler, new ContinuationTokenSigner(KeyA));

        // Act
        var pages = await client.Table("Orders", "pk", "sk").Query("a").PagesAsync().ToListAsync();

        // Assert
        await Assert.That((Base64Url.DecodeFromChars(pages[0].Next.ToString())[0], pages[1].Next.HasValue)).IsEqualTo(((byte)2, false));
    }

    // Every byte of the token, flipped, so that one a client edited anywhere, in the version, the key or the signature, sends no request.
    [Test]
    public async Task PageAsync_TokenWithAnyByteAltered_IsRejectedBeforeARequest()
    {
        // Arrange
        var signer = new ContinuationTokenSigner(KeyA);
        var bytes = Base64Url.DecodeFromChars(await FirstNextAsync(signer, static shop => shop.Query("a").PageAsync()));
        using var handler = new StubHandler(_ => Last());
        using var client = Signing(handler, signer);
        var accepted = new List<int>();

        // Act
        for (var i = 0; i < bytes.Length; i++)
        {
            var altered = (byte[])bytes.Clone();
            altered[i] ^= 0x01;
            try
            {
                await client.Table("Orders", "pk", "sk").Query("a").PageAsync(ContinuationToken.Parse(Base64Url.EncodeToString(altered)));
                accepted.Add(i);
            }
            catch (FormatException)
            {
            }
        }

        // Assert
        await Assert.That((accepted.Count, handler.Requests.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task PageAsync_SignatureOfAnotherKey_ThrowsWithoutQuotingTheToken()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var failure = await CaptureAsync(new ContinuationTokenSigner(KeyB), text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That((failure.Message.StartsWith("This continuation token does not carry a signature", StringComparison.Ordinal), failure.Message.Contains(text, StringComparison.Ordinal), failure.Message.Contains("\"a\"", StringComparison.Ordinal))).IsEqualTo((true, false, false));
    }

    // Rotation, step one: the instances that already hold the new key behind the old one take the tokens of those that sign with the old key.
    [Test]
    public async Task PageAsync_TokenOfAKeyBehindTheFirst_IsTaken()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var startKey = await ReadAsync(new ContinuationTokenSigner(KeyB, KeyA), text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That(startKey).IsEqualTo(Key("1"));
    }

    // Rotation, step two: with the new key in front, a token is signed with it, so an instance that holds only the new key takes it.
    [Test]
    public async Task PageAsync_TokenOfASignerWithSeveralKeys_IsSignedWithTheFirst()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyB, KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var (withNew, withOld) = (
            await ReadAsync(new ContinuationTokenSigner(KeyB), text, static (shop, after) => shop.Query("a").PageAsync(after)),
            (await CaptureAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a").PageAsync(after))).Message.StartsWith("This continuation token does not carry a signature", StringComparison.Ordinal));

        // Assert
        await Assert.That((withNew, withOld)).IsEqualTo((Key("1"), true));
    }

    [Test]
    public async Task PageAsync_PlainTokenOnASigningClient_Throws()
    {
        // Arrange
        var text = ContinuationToken.FromKey(new Item { ["pk"] = "a", ["sk"] = "1" }).ToString();

        // Act
        var failure = await CaptureAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token is not signed");
    }

    [Test]
    public async Task PageAsync_PlainTokenOfThisProcessOnASigningClient_GoesThrough()
    {
        // Arrange: a read of the request API continued in a command, which is code of the service
        using var handler = new StubHandler(_ => Last());
        using var client = Signing(handler, new ContinuationTokenSigner(KeyA));
        var after = ContinuationToken.FromKey(new Item { ["pk"] = "a", ["sk"] = "1" });

        // Act
        await client.Table("Orders", "pk", "sk").Query("a").PageAsync(after);

        // Assert
        await Assert.That(handler.Requests[0].BodyText).EndsWith(",\"ExclusiveStartKey\":" + Key("1") + "}");
    }

    [Test]
    public async Task PageAsync_SignedTokenOnAClientThatDoesNotSign_Throws()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var failure = await CaptureAsync(signer: null, text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token is signed");
    }

    [Test]
    public async Task PageAsync_NoTokenOnASigningClient_ReadsTheFirstPage()
    {
        // Arrange
        using var handler = new StubHandler(_ => Last("1"));
        using var client = Signing(handler, new ContinuationTokenSigner(KeyA));

        // Act
        var page = await client.Table("Orders", "pk", "sk").Query("a").PageAsync(ContinuationToken.Parse(string.Empty));

        // Assert
        await Assert.That((page.Count, handler.Requests[0].BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal))).IsEqualTo((1, false));
    }

    // A token of one query, sent to another: each of these changes what the signature binds, so none of them reads on.
    [Test]
    [Arguments("other partition")]
    [Arguments("other table")]
    [Arguments("an index")]
    [Arguments("other sort key condition")]
    [Arguments("other operator")]
    [Arguments("a sort key condition")]
    [Arguments("a scan")]
    public async Task PageAsync_TokenOfAnotherQuery_Throws(string other)
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a", SortKeyCondition.BeginsWith("x")).PageAsync());
        Func<DynamoDbClient, ContinuationToken, ValueTask<Page<Item>>> read = other switch
        {
            "other partition" => static (client, after) => client.Table("Orders", "pk", "sk").Query("b", SortKeyCondition.BeginsWith("x")).PageAsync(after),
            "other table" => static (client, after) => client.Table("Archive", "pk", "sk").Query("a", SortKeyCondition.BeginsWith("x")).PageAsync(after),
            "an index" => static (client, after) => client.Table("Orders", "pk", "sk").Index("ByPk", "pk", "sk").Query("a", SortKeyCondition.BeginsWith("x")).PageAsync(after),
            "other sort key condition" => static (client, after) => client.Table("Orders", "pk", "sk").Query("a", SortKeyCondition.BeginsWith("y")).PageAsync(after),
            "other operator" => static (client, after) => client.Table("Orders", "pk", "sk").Query("a", SortKeyCondition.Gt("x")).PageAsync(after),
            "a sort key condition" => static (client, after) => client.Table("Orders", "pk", "sk").Query("a").PageAsync(after),
            _ => static (client, after) => client.Table("Orders", "pk", "sk").Scan().PageAsync(after),
        };

        // Act
        var failure = await CaptureOnClientAsync(new ContinuationTokenSigner(KeyA), text, read);

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token does not carry a signature");
    }

    [Test]
    public async Task PageAsync_TokenOfAQueryWithTheOtherComparison_Throws()
    {
        // Arrange (the two conditions differ in the operator alone)
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a", SortKeyCondition.Gt("x")).PageAsync());

        // Act
        var failure = await CaptureAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a", SortKeyCondition.Lt("x")).PageAsync(after));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token does not carry a signature");
    }

    [Test]
    public async Task PageAsync_SignedTokenOfAQueryBetweenTwoValues_ReadsOn()
    {
        // Arrange (the condition has more values than names)
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a", SortKeyCondition.Between("a", "c")).PageAsync());

        // Act
        var startKey = await ReadAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a", SortKeyCondition.Between("a", "c")).PageAsync(after));

        // Assert
        await Assert.That(startKey).IsEqualTo(Key("1"));
    }

    // What the signature leaves out: the filter, the projection, the page size and the direction, which a web API may let its client change between pages.
    [Test]
    public async Task PageAsync_SameQueryWithAnotherFilterAndLimit_ReadsOn()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").Filter(Attr("status").Eq("open")).Limit(2).PageAsync());

        // Act
        var startKey = await ReadAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a").Filter(Attr("status").Eq("shipped")).Limit(5).Project("status").Descending().PageAsync(after));

        // Assert
        await Assert.That(startKey).IsEqualTo(Key("1"));
    }

    [Test]
    public async Task PageAsync_SignedScanToken_ReadsOn()
    {
        // Arrange
        var signer = new ContinuationTokenSigner(KeyA);
        var text = await FirstNextAsync(signer, static shop => shop.Scan().PageAsync());

        // Act
        var startKey = await ReadAsync(signer, text, static (shop, after) => shop.Scan().PageAsync(after));

        // Assert
        await Assert.That(startKey).IsEqualTo(Key("1"));
    }

    [Test]
    public async Task PageAsync_SignedFanOutToken_ReadsOn()
    {
        // Arrange
        var signer = new ContinuationTokenSigner(KeyA);
        using var handler = new StubHandler(FanOutResponse);
        using var client = Signing(handler, signer);
        var first = await client.Table("Orders", "pk", "sk").Query(TwoPartitions).PageAsync(1);

        // Act
        var next = await client.Table("Orders", "pk", "sk").Query(TwoPartitions).PageAsync(1, ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That((Base64Url.DecodeFromChars(first.Next.ToString())[0], next.Count)).IsEqualTo(((byte)2, 1));
    }

    // Same number of partitions, so only the signature tells the lists apart.
    [Test]
    public async Task PageAsync_FanOutTokenOfOtherPartitions_Throws()
    {
        // Arrange
        var signer = new ContinuationTokenSigner(KeyA);
        using var handler = new StubHandler(FanOutResponse);
        using var client = Signing(handler, signer);
        var first = await client.Table("Orders", "pk", "sk").Query(TwoPartitions).PageAsync(1);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(async () => await client.Table("Orders", "pk", "sk").Query(["p0", "p2"]).PageAsync(1, ContinuationToken.Parse(first.Next.ToString())));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token does not carry a signature");
    }

    [Test]
    public async Task PageAsync_PlainFanOutTokenOnASigningClient_Throws()
    {
        // Arrange
        using var plainHandler = new StubHandler(FanOutResponse);
        using var plain = TestClient.Create(plainHandler);
        var text = (await plain.Table("Orders", "pk", "sk").Query(TwoPartitions).PageAsync(1)).Next.ToString();
        using var handler = new StubHandler(FanOutResponse);
        using var client = Signing(handler, new ContinuationTokenSigner(KeyA));

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(async () => await client.Table("Orders", "pk", "sk").Query(TwoPartitions).PageAsync(1, ContinuationToken.Parse(text)));

        // Assert
        await Assert.That((failure.Message.StartsWith("This continuation token is not signed", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Parse_SignedToken_KeepsItsKeyAndItsText()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var token = ContinuationToken.Parse(text);

        // Assert
        await Assert.That((token.ToString(), token.ToKey())).IsEqualTo((text, new Item { ["pk"] = "a", ["sk"] = "1" }));
    }

    [Test]
    public async Task TryParse_SignedToken_IsTrue()
    {
        // Arrange
        var text = await FirstNextAsync(new ContinuationTokenSigner(KeyA), static shop => shop.Query("a").PageAsync());

        // Act
        var parsed = ContinuationToken.TryParse(text, out var token);

        // Assert
        await Assert.That((parsed, token.ToString())).IsEqualTo((true, text));
    }

    [Test]
    public async Task Equals_TokenAndItsParsedText_IsEqual()
    {
        // Arrange
        var token = ContinuationToken.FromKey(new Item { ["pk"] = "a" });

        // Act
        var parsed = ContinuationToken.Parse(token.ToString());

        // Assert
        await Assert.That((parsed == token, parsed.GetHashCode() == token.GetHashCode(), default(ContinuationToken).GetHashCode())).IsEqualTo((true, true, 0));
    }

    // A version 2 that is too short for a key and a signature, or whose key is no key, is no token, whatever its signature would say.
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(SignatureLength)]
    public async Task Parse_SignedVersionWithoutRoomForAKey_IsNoToken(int length)
    {
        // Arrange
        var text = Base64Url.EncodeToString([2, .. new byte[length]]);

        // Act
        var failure = await TestClient.CaptureAsync<FormatException>(() => Task.FromResult(ContinuationToken.Parse(text)));

        // Assert
        await Assert.That(failure.Message).StartsWith("This is not a continuation token");
    }

    [Test]
    [Arguments("{")]
    [Arguments("{}")]
    [Arguments("""{"pk":{"S":"a"}},"TableName":"Other","x":{"S":"b"}""")]
    public async Task Parse_SignedVersionWhoseKeyIsNoKey_IsNoToken(string json)
    {
        // Arrange
        var text = Base64Url.EncodeToString([2, .. Encoding.UTF8.GetBytes(json), .. new byte[SignatureLength]]);

        // Act
        var parsed = ContinuationToken.TryParse(text, out _);

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    // A signature that a client wrote over a key it chose: right shape, wrong signature.
    [Test]
    public async Task PageAsync_MadeUpSignedToken_Throws()
    {
        // Arrange
        var text = Base64Url.EncodeToString([2, .. Encoding.UTF8.GetBytes(Key("9")), .. new byte[SignatureLength]]);

        // Act
        var failure = await CaptureAsync(new ContinuationTokenSigner(KeyA), text, static (shop, after) => shop.Query("a").PageAsync(after));

        // Assert
        await Assert.That(failure.Message).StartsWith("This continuation token does not carry a signature");
    }

    private static byte[] SecretKey(byte fill)
    {
        var key = new byte[ContinuationTokenSigner.MinKeyLength];
        key.AsSpan().Fill(fill);
        return key;
    }

    private static DynamoDbClient Signing(StubHandler handler, ContinuationTokenSigner? signer) =>
        new(TestClient.Options(handler) with { TokenSigner = signer });

    // Each partition gives one item and a next key on its first page, and nothing after.
    private static StubResponse FanOutResponse(RecordedRequest request)
    {
        var partition = request.BodyText.Contains("\"p0\"", StringComparison.Ordinal) ? "0" : "1";
        return request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)
            ? StubResponse.Json("""{"Items":[]}""")
            : StubResponse.Json(Items(Key(partition), partition));
    }

    private static async Task<string> FirstNextAsync(ContinuationTokenSigner signer, Func<Table<Item>, ValueTask<Page<Item>>> read)
    {
        using var handler = new StubHandler(_ => More("1"));
        using var client = Signing(handler, signer);
        return (await read(client.Table("Orders", "pk", "sk"))).Next.ToString();
    }

    // Reads on from the text and returns the start key that went out.
    private static async Task<string> ReadAsync(ContinuationTokenSigner signer, string text, Func<Table<Item>, ContinuationToken, ValueTask<Page<Item>>> read)
    {
        using var handler = new StubHandler(_ => Last());
        using var client = Signing(handler, signer);
        await read(client.Table("Orders", "pk", "sk"), ContinuationToken.Parse(text));
        var body = handler.Requests[0].BodyText;
        const string Member = ",\"ExclusiveStartKey\":";
        return body[(body.IndexOf(Member, StringComparison.Ordinal) + Member.Length)..^1];
    }

    private static Task<FormatException> CaptureAsync(ContinuationTokenSigner? signer, string text, Func<Table<Item>, ContinuationToken, ValueTask<Page<Item>>> read) =>
        CaptureOnClientAsync(signer, text, (client, after) => read(client.Table("Orders", "pk", "sk"), after));

    private static async Task<FormatException> CaptureOnClientAsync(ContinuationTokenSigner? signer, string text, Func<DynamoDbClient, ContinuationToken, ValueTask<Page<Item>>> read)
    {
        using var handler = new StubHandler(_ => Last());
        using var client = Signing(handler, signer);
        var failure = await TestClient.CaptureAsync<FormatException>(async () => await read(client, ContinuationToken.Parse(text)));
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
        return failure;
    }
}
