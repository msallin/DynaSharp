using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>What generated code writes. Every expected item is written by hand, never derived from the writer.</summary>
public sealed class EntityWriteTests
{
    private static readonly Guid Tracking = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Test]
    public async Task WriteAttributes_FullEntity_WritesKeysDiscriminatorAndEveryAttribute()
    {
        // Arrange
        var purchase = new Purchase
        {
            Customer = "alice",
            Number = 42,
            Total = 12.50m,
            Status = Status.Paid,
            Tracking = Tracking,
            ShipTo = new Place("Main St", "Bern", null),
            Lines = [new Line { Sku = "A-1", Quantity = 2 }],
            Tags = ["red"],
            Stages = [Status.Open],
            Counters = new Dictionary<string, int> { ["views"] = 3 },
            Rights = Access.Read | Access.Write,
            Fee = new Cents(150),
            Tip = new Cents(20),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1793448000),
            Revision = 5,
        };

        // Act
        var json = EntityJson.Write(purchase);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"_type":{"S":"PURCHASE"},"pk":{"S":"alice"},"sk":{"N":"42"},"Total":{"N":"12.50"},"Status":{"S":"Paid"}"""
            + ""","Tracking":{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"},"ShipTo":{"M":{"Street":{"S":"Main St"},"City":{"S":"Bern"}}}"""
            + ""","Lines":{"L":[{"M":{"Sku":{"S":"A-1"},"Quantity":{"N":"2"}}}]},"Tags":{"SS":["red"]},"Stages":{"SS":["Open"]}"""
            + ""","Counters":{"M":{"views":{"N":"3"}}},"Rights":{"N":"3"},"Fee":{"S":"150c"},"Tip":{"S":"20c"},"ttl":{"N":"1793448000"},"items":{"N":"1"},"Revision":{"N":"5"}}""");
    }

    [Test]
    public async Task WriteAttributes_MinimalEntity_LeavesOutNullsAndEmptySets()
    {
        // Arrange
        var purchase = new Purchase { Customer = "bob", Number = 1, Total = 0m, Status = Status.Open };

        // Act
        var json = EntityJson.Write(purchase);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"_type":{"S":"PURCHASE"},"pk":{"S":"bob"},"sk":{"N":"1"},"Total":{"N":"0"},"Status":{"S":"Open"},"Lines":{"L":[]}"""
            + ""","Counters":{"M":{}},"Rights":{"N":"0"},"Fee":{"S":"0c"},"items":{"N":"0"},"Revision":{"N":"0"}}""");
    }

    [Test]
    public async Task WriteKey_PlainKeys_WritesOnlyTheTableKey()
    {
        // Arrange
        var purchase = new Purchase { Customer = "bob", Number = 7, Total = 1m, Status = Status.Open };

        // Act
        var json = EntityJson.WriteKey(purchase);

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"bob"},"sk":{"N":"7"}}""");
    }

    [Test]
    public async Task WriteKey_NoSortKey_WritesThePartitionKey()
    {
        // Arrange
        var account = new Account { Id = "a" };

        // Act
        var json = EntityJson.WriteKey(account);

        // Assert
        await Assert.That(json).IsEqualTo("""{"Id":{"S":"a"}}""");
    }

    [Test]
    public async Task WriteAttributes_OptOut_WritesNoDiscriminator()
    {
        // Arrange
        var foreign = new Foreign { Id = "x" };

        // Act
        var json = EntityJson.Write(foreign);

        // Assert
        await Assert.That(json).IsEqualTo("""{"id":{"S":"x"}}""");
    }

    [Test]
    public async Task WriteAttributes_OwnDiscriminatorAttribute_UsesIt()
    {
        // Arrange
        var account = new Account { Id = "a", Aliases = [], Scores = [] };

        // Act
        var json = EntityJson.Write(account);

        // Assert
        await Assert.That(json).IsEqualTo("""{"kind":{"S":"ACCOUNT"},"Id":{"S":"a"},"Plan":{"S":"free"},"Logins":{"N":"7"},"Note":{"S":"kept"},"Aliases":{"L":[]},"Scores":{"L":[]}}""");
    }

    [Test]
    public async Task WriteAttributes_Struct_WritesEveryValue()
    {
        // Arrange
        var reading = new Reading
        {
            Sensor = 9,
            Value = 1.5,
            Calibrated = 2.25m,
            Valid = true,
            Run = Tracking,
            At = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
            Day = new DateOnly(2026, 10, 31),
            Duration = TimeSpan.FromSeconds(90),
            Status = Status.Closed,
            Rights = Access.Delete,
            Offset = -1,
            Previous = Status.Open,
        };

        // Act
        var json = EntityJson.Write(reading);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"_type":{"S":"READING"},"Sensor":{"N":"9"},"Value":{"N":"1.5"},"Calibrated":{"N":"2.25"},"Valid":{"BOOL":true},"Run":{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}"""
            + ""","At":{"S":"2026-10-31T12:00:00.0000000Z"},"Day":{"S":"2026-10-31"},"Duration":{"S":"00:01:30"},"Status":{"S":"Closed"},"Rights":{"N":"128"},"Offset":{"N":"-1"},"Previous":{"S":"Open"}}""");
    }

    [Test]
    public async Task WriteAttributes_AliasedEnumMember_WritesTheFirstName()
    {
        // Arrange (Settled and Paid are one value, which has one stored form)
        var purchase = new Purchase { Customer = "bob", Number = 1, Total = 1m, Status = Status.Settled };

        // Act
        var json = EntityJson.Write(purchase);

        // Assert
        await Assert.That(json).Contains("\"Status\":{\"S\":\"Paid\"}");
    }

    [Test]
    public async Task WriteAttributes_UndefinedEnumValue_ThrowsAndNamesTheEnum()
    {
        // Arrange
        var purchase = new Purchase { Customer = "bob", Number = 1, Total = 1m, Status = (Status)99 };

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Write(purchase));

        // Assert
        await Assert.That(failure.Message).IsEqualTo("The value is not a member of DynaSharp.UnitTests.Mapping.Status, so it has no name to store.");
    }

    [Test]
    public async Task WriteAttributes_NumberEnumOfACombination_WritesTheNumber()
    {
        // Arrange
        var shelf = new Shelf { Id = "s", Rack = 1, Flags = [Access.Read | Access.Delete] };

        // Act
        var json = EntityJson.Write(shelf);

        // Assert
        await Assert.That(json).IsEqualTo("""{"_type":{"S":"SHELF"},"Id":{"S":"s"},"Rack":{"N":"1"},"Flags":{"NS":["129"]}}""");
    }

    [Test]
    public async Task WriteAttributes_EverySetShape_UsesItsSetType()
    {
        // Arrange
        var shelf = new Shelf
        {
            Id = "s",
            Numbers = new HashSet<int> { 4 },
            Blobs = [[1]],
            Ids = [Tracking],
            Names = ["n"],
            Optional = ["o"],
        };

        // Act
        var json = EntityJson.Write(shelf);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"_type":{"S":"SHELF"},"Id":{"S":"s"},"Rack":{"N":"0"},"Numbers":{"NS":["4"]},"Blobs":{"BS":["AQ=="]},"Ids":{"SS":["0f8fad5b-d9cb-469f-a165-70867728950e"]},"Names":{"SS":["n"]},"Optional":{"SS":["o"]}}""");
    }

    [Test]
    public async Task WriteAttributes_Recursion_NestsMaps()
    {
        // Arrange
        var tree = new Tree { Name = "root", Children = [new Tree { Name = "leaf" }] };

        // Act
        var json = EntityJson.Write(tree);

        // Assert
        await Assert.That(json).IsEqualTo("""{"Name":{"S":"root"},"Children":{"L":[{"M":{"Name":{"S":"leaf"},"Children":{"L":[]}}}]}}""");
    }

    [Test]
    public async Task WriteAttributes_BaseProperties_AreWrittenFirst()
    {
        // Arrange
        var derived = new DerivedThing { Id = "d", Extra = 1 };

        // Act
        var json = EntityJson.Write(derived);

        // Assert
        await Assert.That(json).IsEqualTo("""{"_type":{"S":"DERIVED"},"Id":{"S":"d"},"Shared":{"S":"base"},"Extra":{"N":"1"}}""");
    }

    [Test]
    public async Task WriteAttributes_OverrideWithoutTheAttributesOfTheBase_IsWrittenAsTheBaseSays()
    {
        // Arrange (the attributes are not inherited, and an override used to store the secret under the name of the property)
        var memo = new SealedMemo { Text = "x", Secret = "s" };

        // Act
        var json = EntityJson.Write(memo);

        // Assert
        await Assert.That(json).IsEqualTo("""{"t":{"S":"x"}}""");
    }

    [Test]
    public async Task WriteAttributes_NothingMapped_WritesAnEmptyItem()
    {
        // Arrange
        var empty = new Empty();

        // Act
        var json = EntityJson.Write(empty);

        // Assert
        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    public async Task Info_Entity_DescribesTheTable()
    {
        // Arrange, Act
        var info = Info<Purchase>();

        // Assert
        await Assert.That((info.Discriminator, info.DiscriminatorAttributeName, info.PartitionKeyName, info.SortKeyName, info.VersionAttributeName))
            .IsEqualTo(("PURCHASE", "_type", "pk", (string?)"sk", (string?)"Revision"));
    }

    [Test]
    public async Task Info_IndexOverAnAttribute_IsListedWithItsKeys()
    {
        // Arrange, Act
        var index = Info<Purchase>().Indexes.Single();

        // Assert
        await Assert.That(index).IsEqualTo(new DynamoDbIndexInfo("ByStatus", "Status", null, Sparse: false, PartitionKeyType: ScalarAttributeType.String) { PartitionKeyBuilder = "ByStatusPk" });
    }

    [Test]
    public async Task Info_OptOut_HasNoDiscriminatorOrSortKey()
    {
        // Arrange, Act
        var info = Info<Foreign>();

        // Assert
        await Assert.That((info.Discriminator, info.SortKeyName, info.VersionAttributeName, info.Indexes.Count)).IsEqualTo(((string?)null, (string?)null, (string?)null, 0));
    }

    private static DynamoDbEntityInfo Info<T>()
        where T : IDynamoDbEntity<T> => T.Info;
}
