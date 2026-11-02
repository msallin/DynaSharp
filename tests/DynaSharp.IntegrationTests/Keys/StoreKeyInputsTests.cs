using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>
/// What <c>StoreKeyInputs()</c> leaves in a real table. <c>if_not_exists</c> is the service's to evaluate, so what stays and what is written comes from the emulators. <c>DYNA209</c> looks at the entity and not at its
/// updates, and these updates are what it says to use, so the handles go through variables, which it does not follow.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class StoreKeyInputsTests(Backend backend)
{
    private static readonly Guid Id = Guid.Parse("0190a5b2-7c3e-7f00-8a11-1234567890ab");

    [Test]
    public async Task Select_ItemAnUpdateCreated_ReadsTheInputOfTheKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var bookings = await CreateAsync<Booking>(sandbox);
        await bookings.Update(Booking.Keys.Of("alice", Id)).Set(Booking.Attrs.Note, "made by an update").StoreKeyInputs().SendAsync();
        var userId = Booking.Attrs.UserId;
        var bookingId = Booking.Attrs.BookingId;

        // Act
        var user = await bookings.Get(Booking.Keys.Of("alice", Id)).Select(userId).SendAsync();
        var booking = await bookings.Get(Booking.Keys.Of("alice", Id)).Select(bookingId).SendAsync();

        // Assert
        await Assert.That((user.Value, booking.Value)).IsEqualTo(("alice", Id));
    }

    [Test]
    public async Task Update_ItemThatHoldsAnotherValue_LeavesThatValue()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var bookings = await CreateAsync<Booking>(sandbox);
        await bookings.Update(Booking.Keys.Of("alice", Id)).Set("UserId", "written by another tool").SendAsync();
        var userId = Booking.Attrs.UserId;

        // Act
        await bookings.Update(Booking.Keys.Of("alice", Id)).Set(Booking.Attrs.Note, "n").StoreKeyInputs().SendAsync();
        var user = await bookings.Get(Booking.Keys.Of("alice", Id)).Select(userId).SendAsync();

        // Assert
        await Assert.That(user.Value).IsEqualTo("written by another tool");
    }

    [Test]
    public async Task Select_RawValueThatHoldsTheDelimiter_ReadsItWhole()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var appointments = await CreateAsync<Appointment>(sandbox);
        await appointments.Update(Appointment.Keys.Of("a#b", 1, "c#d")).Set(Appointment.Attrs.Note, "n").StoreKeyInputs().SendAsync();
        var keyInput = Appointment.Attrs.Key;
        var slotInput = Appointment.Attrs.Slot;

        // Act
        var key = await appointments.Get(Appointment.Keys.Of("a#b", 1, "c#d")).Select(keyInput).SendAsync();
        var slot = await appointments.Get(Appointment.Keys.Of("a#b", 1, "c#d")).Select(slotInput).SendAsync();

        // Assert
        await Assert.That((key.Value, slot.Value)).IsEqualTo(("a#b", "c#d"));
    }

    private static async Task<Table<T>> CreateAsync<T>(Sandbox sandbox)
        where T : DynaSharp.Mapping.IDynamoDbEntity<T>
    {
        var table = sandbox.Client.Table<T>(Tables.UniqueName());
        await sandbox.CreateTableAsync(table, table.CreateTable().WaitUntilActive());
        return table;
    }
}
