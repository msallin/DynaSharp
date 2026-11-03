using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// Developer Guide, "Projections": KEYS_ONLY holds the keys of the table and the index, INCLUDE those and the attributes named, ALL everything.
public sealed class FakeIndexTests
{
    private static readonly KeySchema TableKey = new("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.String);
    private static readonly Item Order = new() { ["pk"] = "c", ["sk"] = "o1", ["status"] = "open", ["total"] = 5, ["note"] = "n" };

    [Test]
    [Arguments(ProjectionType.All, null, "note,pk,sk,status,total")]
    [Arguments(ProjectionType.KeysOnly, null, "pk,sk,status")]
    [Arguments(ProjectionType.Include, new[] { "total", "missing" }, "pk,sk,status,total")]
    public async Task Project_Item_HoldsWhatTheProjectionSays(ProjectionType type, string[]? nonKeyAttributes, string expected)
    {
        // Arrange
        var index = new FakeIndex("ByStatus", new KeySchema("status", ScalarAttributeType.String), new IndexProjection { ProjectionType = type, NonKeyAttributes = nonKeyAttributes }, true, TableKey);

        // Act
        var projected = index.Project(Order);

        // Assert
        await Assert.That(string.Join(",", projected.Keys.Order(StringComparer.Ordinal))).IsEqualTo(expected);
    }

    [Test]
    public async Task Backfill_ItemsWithAndWithoutTheKey_IndexesThoseWithIt()
    {
        // Arrange
        var index = new FakeIndex("ByStatus", new KeySchema("status", ScalarAttributeType.String), new IndexProjection { ProjectionType = ProjectionType.All }, true, TableKey);
        Item[] items = [Order, new() { ["pk"] = "c", ["sk"] = "o2" }, new() { ["pk"] = "c", ["sk"] = "o3", ["status"] = 1 }];

        // Act
        index.Backfill(items);

        // Assert
        await Assert.That((index.ItemCount, index.SizeBytes)).IsEqualTo((1L, ItemSize.Of(Order)));
    }

    [Test]
    public async Task Locate_IndexWithASortKey_SortsByItThenTheTableKey()
    {
        // Arrange
        var index = new FakeIndex("ByTotal", new KeySchema("pk", ScalarAttributeType.String, "total", ScalarAttributeType.Number), new IndexProjection { ProjectionType = ProjectionType.All }, false, TableKey);

        // Act
        var (partition, slot) = index.Locate(Order);

        // Assert
        await Assert.That((partition.AsString(), slot)).IsEqualTo(("c", new SlotKey(AttributeValue.FromNumber("5"), "c", "o1")));
    }
}
