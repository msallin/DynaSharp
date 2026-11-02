using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>Keys whose values hold the delimiter of their template, through a real table: what goes in comes back, and the conditions select by the bytes that are stored.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class RawKeyTests(Backend backend)
{
    private const string Key = "tenant#1#appt-7";

    [Test]
    public async Task Get_KeyThatHoldsTheDelimiter_ReadsWhatWasPut()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var appointments = await CreateAsync(sandbox);
        var appointment = Slot(1, "a#b") with { Note = "first" };
        await appointments.Put(appointment).SendAsync();

        // Act
        var read = await appointments.Get(Appointment.Keys.Of(Key, 1, "a#b")).ItemAsync();

        // Assert
        await Assert.That(read).IsEqualTo(appointment);
    }

    [Test]
    public async Task Update_ItemItCreates_ReadsItsRequiredInputsFromTheKeys()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var appointments = await CreateAsync(sandbox);
        await appointments.Update(Appointment.Keys.Of(Key, 1, "a#b")).Set(Appointment.Attrs.Note, "made by an update").SendAsync();

        // Act
        var read = await appointments.Get(Appointment.Keys.Of(Key, 1, "a#b")).ItemAsync();

        // Assert
        await Assert.That(read).IsEqualTo(Slot(1, "a#b") with { Note = "made by an update" });
    }

    [Test]
    public async Task Query_ConditionsOnARawValue_SelectByTheStoredBytes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var appointments = await CreateAsync(sandbox);
        foreach (var slot in new[] { "a", "a#", "a#b", "a#b#c", "aa", "b" })
        {
            await appointments.Put(Slot(1, slot)).SendAsync();
        }

        var partition = Appointment.Keys.Pk.Of(Key);

        // Act
        string[] rows =
        [
            await SlotsAsync(appointments.Query(partition, SortKeyCondition.BeginsWith(Appointment.Keys.Sk.PrefixPartial(1, "a#"))).ItemsAsync()),
            await SlotsAsync(appointments.Query(partition, Appointment.Keys.Sk.Between(1, "a#", "a#b")).ItemsAsync()),
            await SlotsAsync(appointments.Query(partition, Appointment.Keys.Sk.Gt(1, "a#b")).ItemsAsync()),
            await SlotsAsync(appointments.Query(partition, Appointment.Keys.Sk.BeginsWith(1)).ItemsAsync()),
        ];

        // Assert
        await Assert.That(string.Join(" | ", rows)).IsEqualTo("a#,a#b,a#b#c | a#,a#b | a#b#c,aa,b | a,a#,a#b,a#b#c,aa,b");
    }

    private static Appointment Slot(int day, string slot) => new() { Key = Key, Day = day, Slot = slot };

    private static async Task<Table<Appointment>> CreateAsync(Sandbox sandbox)
    {
        var appointments = sandbox.Client.Table<Appointment>(Tables.UniqueName());
        await sandbox.CreateTableAsync(appointments, appointments.CreateTable().WaitUntilActive());
        return appointments;
    }

    private static async Task<string> SlotsAsync(IAsyncEnumerable<Appointment> items) => string.Join(",", await items.Select(static appointment => appointment.Slot).ToArrayAsync());
}
