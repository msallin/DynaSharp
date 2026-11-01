using System.Buffers;
using System.Text;
using DynaSharp.Mapping;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Generated code end to end: through its own writer and reader, through <see cref="Item"/>, recorded responses, pools and the table API.</summary>
public sealed class EntityRoundTripTests
{
    private const string ReadingJson =
        """{"_type":{"S":"READING"},"Sensor":{"N":"9"},"Value":{"N":"1.5"},"Calibrated":{"N":"2.25"},"Valid":{"BOOL":true},"Run":{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}"""
        + ""","At":{"S":"2026-10-31T12:00:00.0000000Z"},"Day":{"S":"2026-10-31"},"Duration":{"S":"00:01:30"},"Status":{"S":"Closed"},"Rights":{"N":"128"},"Offset":{"N":"-1"},"Previous":{"S":"Open"}}""";

    [Test]
    public async Task RoundTrip_ConverterWithExplicitMembers_WritesThroughIt()
    {
        // Arrange
        var receipt = new Receipt { Id = new Cents(1), Net = new Cents(2), Gross = new Cents(3) };

        // Act
        var json = EntityJson.Write(receipt);
        var back = EntityJson.Read<Receipt>(json);

        // Assert
        await Assert.That((json, back)).IsEqualTo(("""{"_type":{"S":"RECEIPT"},"Id":{"N":"1"},"Net":{"N":"2"},"Gross":{"N":"3"}}""", receipt));
    }

    [Test]
    public async Task WriteKey_PlainKeyWithAnExplicitConverter_WritesThroughIt()
    {
        // Arrange
        var key = Receipt.Keys.Of(new Cents(7));

        // Act
        var json = TestJson.Write(writer =>
        {
            writer.WriteStartItem();
            key.WriteKey(writer);
            writer.WriteEndItem();
        });

        // Assert
        await Assert.That(json).IsEqualTo("""{"Id":{"N":"7"}}""");
    }

    [Test]
    public async Task RoundTrip_FullEntity_GivesAnEqualEntity()
    {
        // Arrange
        var purchase = new Purchase
        {
            Customer = "alice",
            Number = -3,
            Total = 12.5m,
            Status = Status.Closed,
            Tracking = Guid.NewGuid(),
            ShipTo = new Place("Main St", "Bern", "3000"),
            Rights = Access.Delete,
            Fee = new Cents(1),
        };

        // Act
        var back = EntityJson.Read<Purchase>(EntityJson.Write(purchase));

        // Assert
        await Assert.That((back.Customer, back.Number, back.Total, back.Status, back.Tracking, back.ShipTo, back.Rights, back.Fee))
            .IsEqualTo((purchase.Customer, purchase.Number, purchase.Total, purchase.Status, purchase.Tracking, purchase.ShipTo, purchase.Rights, purchase.Fee));
    }

    [Test]
    public async Task RoundTrip_ReadOnlyValueTypes_GivesAnEqualStruct()
    {
        // Arrange
        var reading = new Reading { Sensor = long.MinValue, Value = -0.5, Calibrated = decimal.MaxValue, Run = Guid.NewGuid(), Offset = int.MaxValue, Previous = Status.Paid };

        // Act
        var back = EntityJson.Read<Reading>(EntityJson.Write(reading));

        // Assert
        await Assert.That(back).IsEqualTo(reading);
    }

    [Test]
    public async Task RoundTrip_EmptySets_AreEmptyAgain()
    {
        // Arrange (an empty set is not written, and a missing one reads as empty)
        var purchase = new Purchase { Customer = "c", Number = 1, Total = 1m, Status = Status.Open };

        // Act
        var back = EntityJson.Read<Purchase>(EntityJson.Write(purchase));

        // Assert
        await Assert.That((back.Tags.Count, back.Stages.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task ItemFrom_Entity_HoldsWhatTheItemStores()
    {
        // Arrange
        var member = new Member("i", "n", Age: 40);

        // Act
        var item = Item.From(member);

        // Assert
        await Assert.That((item.GetString("_type"), item.GetString("Id"), item.GetNumber<int>("Age"), item.ContainsKey("Nick"))).IsEqualTo(((string?)"MEMBER", (string?)"i", (int?)40, false));
    }

    [Test]
    public async Task ItemTo_Entity_ReadsItBack()
    {
        // Arrange
        var member = new Member("i", "n", Nick: "k") { Extra = "e" };

        // Act
        var back = Item.From(member).To<Member>();

        // Assert
        await Assert.That(back).IsEqualTo(member);
    }

    [Test]
    public async Task ItemTo_OtherType_Throws()
    {
        // Arrange
        var item = Item.From(new Member("i", "n"));

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => item.To<Purchase>());

        // Assert
        await Assert.That(failure.EntityType).IsEqualTo("Purchase");
    }

    [Test]
    public async Task ReadRecordedResponse_NestedMap_ReadsADocument()
    {
        // Arrange (the attributes of a real response, in the order AWS sent them)
        // Act
        var nested = EntityJson.ReadRecorded<NestedA>("proj_get_a1");

        // Assert
        await Assert.That(nested.A).IsEqualTo(new SiblingLeaves("bb", "cc"));
    }

    [Test]
    public async Task ReadRecordedResponse_ListOfMapsWithKeysInDifferentOrders_ReadsEachElement()
    {
        // Arrange
        // Act
        var nested = EntityJson.ReadRecorded<ListOfPairs>("proj_get_a3");

        // Assert
        await Assert.That(nested.L.SequenceEqual([new Pair("x0", "y0"), new Pair("x1", "y1")])).IsTrue();
    }

    [Test]
    public async Task ReadRecordedResponse_UnknownNestedAttribute_IsSkipped()
    {
        // Arrange
        // Act
        var nested = EntityJson.ReadRecorded<MapAndList>("proj_get_a4");

        // Assert
        await Assert.That((nested.A.B, nested.L.Count)).IsEqualTo(("bb", 2));
    }

    [Test]
    public async Task ReadRecordedResponse_StringSetWithAnEmptyMember_KeepsIt()
    {
        // Arrange
        // Act
        var set = EntityJson.ReadRecorded<KeyedSet>("esm_put_ss_mixed");

        // Assert
        await Assert.That((set.Pk, string.Join('|', set.Attr.Order(StringComparer.Ordinal)))).IsEqualTo(("esm-put-ss-mixed", "|a"));
    }

    [Test]
    public async Task ReadRecordedResponse_SetDeletedToNothing_IsEmpty()
    {
        // Arrange (the item kept only its key after the last set member was deleted)
        // Act
        var set = EntityJson.ReadRecorded<KeyedSet>("esm_upd_del_last");

        // Assert
        await Assert.That((set.Pk, set.Attr.Count)).IsEqualTo(("esm-upd-del-last", 0));
    }

    [Test]
    public async Task Read_ListOfDocuments_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Line>();
        var json = """{"L":[""" + string.Join(',', Enumerable.Repeat("""{"M":{"Sku":{"S":"s"},"Quantity":{"N":"1"}}}""", 40)) + "]}";

        // Act
        var lines = ReadLines(json, pool);

        // Assert
        await Assert.That((lines.Count, pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo((40, 0, false));
    }

    [Test]
    public async Task Read_ListOfDocumentsThatFailsHalfWay_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Line>();
        const string Json = """{"L":[{"M":{"Sku":{"S":"s"},"Quantity":{"N":"1"}}},{"M":{"Sku":{"S":"s"}}}]}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => ReadLines(Json, pool));

        // Assert
        await Assert.That((failure.AttributeName, pool.Outstanding, pool.Returned)).IsEqualTo(((string?)"Quantity", 0, 1));
    }

    [Test]
    public async Task Read_KnownNamesOfValueTypes_AllocatesNothing()
    {
        // Arrange (a first pass compiles everything the second pass measures)
        var json = Encoding.UTF8.GetBytes(ReadingJson);
        Touch(json, 5);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        Touch(json, 200);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task Read_UnknownAttributes_AllocateNothing()
    {
        // Arrange
        var unknown = ""","Unknown":{"M":{"a":{"L":[{"S":"long text"},{"N":"1"}]}}},"Other":{"SS":["x","y"]},"pk":{"S":"USER#1"},"gsi1pk":{"S":"x"}}""";
        var json = Encoding.UTF8.GetBytes(ReadingJson[..^1] + unknown);
        Touch(json, 5);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        Touch(json, 200);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task Read_ClassOfValueTypes_AllocatesExactlyTheObject()
    {
        // Arrange (no string for a name, nothing boxed: one object per read)
        var json = Encoding.UTF8.GetBytes("""{"_type":{"S":"GAUGE"},"Id":{"N":"1"},"Low":{"N":"2.5"},"High":{"N":"9"},"Mode":{"S":"Open"}}""");
        Measure(json, 5);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        Measure(json, 100);
        var perRead = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        // Assert
        await Assert.That(perRead).IsEqualTo(56);
    }

    [Test]
    public async Task Write_FullEntityToAWriter_AllocatesNothing()
    {
        // Arrange
        var reading = new Reading { Sensor = 1, Value = 2.5, Calibrated = 3m, Run = Guid.NewGuid(), At = DateTimeOffset.UnixEpoch, Status = Status.Open, Offset = 1 };
        using var buffer = new PooledByteBuffer(4096);
        using var json = WireJson.CreateWriter(buffer);
        var writer = new DynamoDbWriter(json);
        Rewrite(writer, json, buffer, reading, 5);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        Rewrite(writer, json, buffer, reading, 200);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task TableGet_GeneratedEntity_ReadsTheResponse()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"kind":{"S":"ACCOUNT"},"Id":{"S":"a"},"Plan":{"S":"pro"}}}"""));
        var accounts = client.Table<Account>("Shop");

        // Act
        var result = await accounts.Get(new Account { Id = "a" }).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Item!.Plan, handler.Requests[0].BodyText)).IsEqualTo((true, "pro", """{"TableName":"Shop","Key":{"Id":{"S":"a"}}}"""));
    }

    [Test]
    public async Task TableGet_ItemOfAnotherType_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"kind":{"S":"OTHER"},"Id":{"S":"a"}}}"""));
        var accounts = client.Table<Account>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await accounts.Get(new Account { Id = "a" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("\"OTHER\"");
    }

    [Test]
    public async Task TablePut_GeneratedEntity_SendsTheItem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));
        var members = client.Table<Member>("Shop");

        // Act
        await members.Put(new Member("i", "n") { Extra = "e" }).SendAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).IsEqualTo(
            """{"TableName":"Shop","Item":{"_type":{"S":"MEMBER"},"Id":{"S":"i"},"Name":{"S":"n"},"Age":{"N":"18"},"Limit":{"N":"1.5"},"Motto":{"S":"hello \"world\""},"Stage":{"S":"Closed"},"Extra":{"S":"e"}}}""");
    }

    private static List<Line> ReadLines(string json, ArrayPool<Line> pool) => TestJson.Read(json, (ref reader) =>
        ListConverter<Line, DocumentConverter<Line>>.Read(ref reader, reader.ReadStartAttribute(), pool));

    private static void Touch(byte[] json, int times)
    {
        for (var i = 0; i < times; i++)
        {
            ReadOne<Reading>(json);
        }
    }

    private static void Measure(byte[] json, int times)
    {
        for (var i = 0; i < times; i++)
        {
            ReadOne<Gauge>(json);
        }
    }

    private static T ReadOne<T>(byte[] json)
        where T : IDynamoDbDocument<T>
    {
        var reader = new DynamoDbReader(json);
        reader.ReadStartItem();
        return T.ReadAttributes(ref reader);
    }

    private static void Rewrite<T>(DynamoDbWriter writer, System.Text.Json.Utf8JsonWriter json, PooledByteBuffer buffer, T value, int times)
        where T : IDynamoDbDocument<T>
    {
        for (var i = 0; i < times; i++)
        {
            writer.WriteStartItem();
            T.WriteAttributes(writer, value);
            writer.WriteEndItem();
            json.Flush();
            json.Reset();
            buffer.Clear(4096);
        }
    }
}
