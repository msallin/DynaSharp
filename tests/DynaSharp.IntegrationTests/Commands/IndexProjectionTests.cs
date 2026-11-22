using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>The projection that <c>Include</c> declares: what <see cref="CreateTableCommand"/> creates, a typed read of it, and the drift of a table created without it.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class IndexProjectionTests(Backend backend)
{
    [Test]
    public async Task CreateTable_Include_ProjectsItAndTheDiscriminatorAndATypedReadOfTheIndexFindsTheItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var gadgets = sandbox.Client.Table<ListedGadget>(Tables.UniqueName());

        // Act (a local index reads consistently, so the item is there at once; a global one shows it only once it propagates)
        var created = await sandbox.CreateTableAsync(gadgets, gadgets.CreateTable().WaitUntilActive());
        await gadgets.Put(new ListedGadget { Id = "1", Name = "Scale", Firmware = "2.1" }).SendAsync();
        var listed = await gadgets.Index(ListedGadget.Indexes.ByFirmware).Query(ListedGadget.Keys.Pk.Of("1")).Consistent().ItemsAsync().ToListAsync();

        // Assert
        var global = created.Table.GlobalSecondaryIndexes.Single().Projection!;
        var local = created.Table.LocalSecondaryIndexes.Single().Projection!;
        await Assert.That((global.ProjectionType, string.Join(",", (global.NonKeyAttributes ?? []).Order(StringComparer.Ordinal)), local.ProjectionType, string.Join(",", (local.NonKeyAttributes ?? []).Order(StringComparer.Ordinal)), listed.Single().Name))
            .IsEqualTo((ProjectionType.Include, "Name,_type", ProjectionType.Include, "Name,_type", "Scale"));
    }

    [Test]
    public async Task SendAsync_FailOnDriftOverAnIndexOfEveryAttribute_ThrowsNamingTheProjections()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var whole = sandbox.Client.Table<WholeGadget>(Tables.UniqueName());
        await sandbox.CreateTableAsync(whole, whole.CreateTable().WaitUntilActive());
        var listed = sandbox.Client.Table<ListedGadget>(whole.Name);

        // Act
        var failure = await Items.FailureOfAsync<TableDriftException>(async () => await Tables.SendAsync(listed.CreateTable().FailOnDrift()));

        // Assert
        var drift = failure.Drift.Single();
        await Assert.That((drift.Kind, drift.IndexName, drift.Description.Contains("with a projection of ALL, and the entities declare INCLUDE (Name, _type)", StringComparison.Ordinal)))
            .IsEqualTo((TableDriftKind.IndexDiffers, (string?)"DeviceList", true));
    }
}
