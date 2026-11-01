using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Index overloading</see>: a table has few global indexes, so each type gives the same key attributes a meaning of its own, and one index answers several questions. Each type declares
/// its own templates over the same attributes.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class IndexOverloadingTests(Backend backend)
{
    private const string Red = "red";

    [Test]
    public async Task Query_OneIndex_AnswersWhatEachTypeMeansByIt()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutTeamAsync(sandbox);
        await sandbox.Shop.Of<Warehouse>().Put(new Warehouse { Id = "w1", City = "Bern", Name = "North" }).SendAsync();
        var staff = sandbox.Shop.Of<Staffer>().Index(Staffer.Indexes.Gsi1);
        var warehouses = sandbox.Shop.Of<Warehouse>().Index(Warehouse.Indexes.Gsi1);

        // Act
        var hires = await staff.Query(Staffer.Keys.Gsi1Pk.Of(Red), Staffer.Keys.Gsi1Sk.BeginsWith()).ItemsAsync().ToListAsync();
        var inBern = await warehouses.Query(Warehouse.Keys.Gsi1Pk.Of("Bern")).ItemsAsync().ToListAsync();

        // Assert (oldest hire first, the order of this type's key)
        await Assert.That((string.Join(",", hires.Select(static staffer => staffer.Id)), inBern.Single().Name)).IsEqualTo(("ann,ben", "North"));
    }

    [Test]
    public async Task Query_SharedPartition_ReadsBothTypesInTheOrderOfTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutTeamAsync(sandbox);
        var index = sandbox.Shop.Index<Staffer>(Staffer.Indexes.Gsi1);

        // Act
        var items = await index.Query(Staffer.Keys.Gsi1Pk.Of(Red)).Of<Staffer, Expense>().ItemsAsync().ToListAsync();

        // Assert (AMOUNT# before HIRED#, and by amount, negative first, because of n8.2)
        await Assert.That(string.Join(",", items.Select(static item => item.Match(static staffer => staffer.Id, static expense => expense.Id)))).IsEqualTo("refund,lunch,ann,ben");
    }

    private static async Task PutTeamAsync(ShopSandbox sandbox)
    {
        await sandbox.Shop.Of<Staffer>().BatchWrite()
            .Put(new Staffer { Id = "ben", Team = Red, HiredOn = new DateOnly(2024, 3, 1) })
            .Put(new Staffer { Id = "ann", Team = Red, HiredOn = new DateOnly(2021, 7, 1) })
            .SendAsync();
        await sandbox.Shop.Of<Expense>().BatchWrite()
            .Put(new Expense { Id = "lunch", Team = Red, Amount = 40m })
            .Put(new Expense { Id = "refund", Team = Red, Amount = -15.5m })
            .SendAsync();
    }
}

[DynamoDbEntity]
[Discriminator("STAFFER")]
[PartitionKey("STAFF#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("GSI1", PartitionKey = "TEAM#{Team}", SortKey = "HIRED#{HiredOn}")]
internal sealed partial record Staffer
{
    public required string Id { get; init; }

    public required string Team { get; init; }

    public required DateOnly HiredOn { get; init; }
}

[DynamoDbEntity]
[Discriminator("EXPENSE")]
[PartitionKey("EXPENSE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "TEAM#{Team}", SortKey = "AMOUNT#{Amount:n8.2}")]
internal sealed partial record Expense
{
    public required string Id { get; init; }

    public required string Team { get; init; }

    public required decimal Amount { get; init; }
}

[DynamoDbEntity]
[Discriminator("WAREHOUSE")]
[PartitionKey("WAREHOUSE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "CITY#{City}", SortKey = "{Name}")]
internal sealed partial record Warehouse
{
    public required string Id { get; init; }

    public required string City { get; init; }

    public required string Name { get; init; }
}
