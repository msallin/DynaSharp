using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>A template of one bare placeholder has no structure to protect, so it holds what DynamoDB holds: a control character and U+10FFFF too.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class BareKeyTests(Backend backend)
{
    [Test]
    [Arguments("b\u0001")]
    [Arguments("b\U0010FFFF")]
    public async Task Query_ValueOnlyATemplateRefuses_FindsTheItemAndRestoresIt(string listOrder)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var devices = sandbox.Client.Table<ListedDevice>(Tables.UniqueName());
        await sandbox.CreateTableAsync(devices, devices.CreateTable().WaitUntilActive());
        await devices.Put(new ListedDevice { Id = "1", ListOrder = listOrder }).SendAsync();

        // Act
        var found = await devices.Index(ListedDevice.Indexes.DeviceList)
            .Query(ListedDevice.Keys.DeviceListPk.Of(), ListedDevice.Keys.DeviceListSk.Gt("b"))
            .ItemsAsync()
            .ToListAsync();

        // Assert
        await Assert.That(found.Single().ListOrder).IsEqualTo(listOrder);
    }
}
