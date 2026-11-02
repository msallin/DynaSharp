using DynaSharp.Fluent;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

// What a read does about items of other types, per kind of read. A generated entity cannot have a discriminator on a key of its table, so the models are made by hand.
public sealed class TableModelTests
{
    [Test]
    [Arguments(null, null, "None", "None", "None")]
    [Arguments(null, "_type", "None", "None", "None")]
    [Arguments(null, "sk", "None", "None", "None")]
    [Arguments("NOTE", "_type", "CheckEachItem", "Filter", "Filter")]
    [Arguments("NOTE", "pk", "CheckEachItem", "CheckEachItem", "Filter")]
    [Arguments("NOTE", "sk", "CheckEachItem", "CheckEachItem", "Filter")]
    public async Task DiscriminatorChecks_TableOfEachShape_AnswerPerKindOfRead(string? discriminator, string? attribute, string onGet, string onQuery, string onScan)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var model = new TableModel(client.Core, "Shop", "pk", "sk", discriminator, attribute);

        // Assert
        await Assert.That((model.DiscriminatorOnGet.ToString(), model.DiscriminatorOnQuery.ToString(), model.DiscriminatorOnScan.ToString())).IsEqualTo((onGet, onQuery, onScan));
    }

    [Test]
    public async Task DiscriminatorOnQuery_IndexKeyedByTheDiscriminator_LooksAtEachItemWhereTheTableFilters()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = new TableModel(client.Core, "Shop", "pk", "sk", "NOTE", "_type");

        // Act
        var index = table.ForIndex("ByType", "_type", "pk");

        // Assert
        await Assert.That((table.DiscriminatorOnQuery, index.DiscriminatorOnQuery)).IsEqualTo((DiscriminatorCheck.Filter, DiscriminatorCheck.CheckEachItem));
    }
}
