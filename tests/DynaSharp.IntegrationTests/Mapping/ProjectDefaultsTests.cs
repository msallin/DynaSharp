using DynaSharp.DefaultsFixture;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A project that sets <see cref="DynaSharp.Mapping.DynamoDbDefaultsAttribute"/> and <see cref="DynaSharp.Mapping.DynamoDbDefaultConverterAttribute"/>, against what a service stores.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ProjectDefaultsTests(Backend backend)
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.FromUnixTimeMilliseconds(1700000000123);

    [Test]
    public async Task CreateTable_PlainKey_HasTheConvertedAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var guests = sandbox.Client.Table<Guest>(Tables.UniqueName());

        // Act
        await sandbox.CreateTableAsync(guests, guests.CreateTable().WaitUntilActive());
        var description = await guests.DescribeTableAsync();

        // Assert
        await Assert.That(description.AttributeDefinitions.Single().AttributeName).IsEqualTo("guest_id");
    }

    [Test]
    public async Task Put_Guest_StoresConvertedNamesLeavesOutWhatIsEmptyAndWritesThroughTheDefault()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var guests = sandbox.Client.Table<Guest>(Tables.UniqueName());
        await sandbox.CreateTableAsync(guests, guests.CreateTable().WaitUntilActive());

        // Act
        await guests.Put(new Guest { GuestId = "g1", GuestName = "Anna", PreferredName = string.Empty, LastSeen = Noon, VisitDates = [Noon] }).SendAsync();
        var stored = await sandbox.Client.Table(guests.Name, "guest_id").Get("g1").Consistent().ItemAsync();

        // Assert
        await Assert.That((string.Join(",", stored!.Keys.Order(StringComparer.Ordinal)), stored["last_seen"].AsString(), stored["visit_dates"].AsList()[0].AsString()))
            .IsEqualTo(("VIP,_type,guest_id,guest_name,kept_note,last_seen,visit_dates", "1700000000123", "1700000000123"));
    }

    [Test]
    public async Task Get_Guest_ReadsWhatAPutWrote()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var guests = sandbox.Client.Table<Guest>(Tables.UniqueName());
        await sandbox.CreateTableAsync(guests, guests.CreateTable().WaitUntilActive());
        await guests.Put(new Guest { GuestId = "g1", GuestName = "Anna", FirstSeen = Noon, VisitDates = [Noon], HomeAddress = new HomeAddress { ZipCode = "8000", StreetLines = ["Bahnhofstrasse 1"] } }).SendAsync();

        // Act
        var guest = await guests.Get(Guest.Keys.Of("g1")).Consistent().ItemAsync();

        // Assert
        await Assert.That((guest!.GuestName, guest.FirstSeen, guest.VisitDates[0], guest.HomeAddress!.ZipCode, guest.HomeAddress.StreetLines[0], guest.PreferredName))
            .IsEqualTo(("Anna", (DateTimeOffset?)Noon, Noon, "8000", "Bahnhofstrasse 1", "none"));
    }

    [Test]
    public async Task Update_ByHandle_WritesTheConvertedAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var guests = sandbox.Client.Table<Guest>(Tables.UniqueName());
        await sandbox.CreateTableAsync(guests, guests.CreateTable().WaitUntilActive());
        await guests.Put(new Guest { GuestId = "g1", GuestName = "Anna" }).SendAsync();

        // Act
        await guests.Update(Guest.Keys.Of("g1")).Set(Guest.Attrs.GuestName, "Bea").Set(Guest.Attrs.HomeAddress, new HomeAddress { ZipCode = "3000" }).SendAsync();
        var stored = await sandbox.Client.Table(guests.Name, "guest_id").Get("g1").Consistent().ItemAsync();

        // Assert
        await Assert.That((stored!["guest_name"].AsString(), stored["home_address"].AsMap()["zip_code"].AsString())).IsEqualTo(("Bea", "3000"));
    }

    [Test]
    public async Task Query_IndexOfAnEntityWithoutADiscriminator_KeepsItsFrozenNamesAndFindsTheItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var bookings = sandbox.Client.Table<Booking>(Tables.UniqueName());
        await sandbox.CreateTableAsync(bookings, bookings.CreateTable().WaitUntilActive());
        await bookings.Put(new Booking { BookingId = "b1", CheckIn = Noon, RoomNumber = "101", BookedAt = Noon }).SendAsync();

        // Act
        var found = await bookings.Index(Booking.Indexes.ByRoom).Query(Booking.Keys.ByRoomPk.Of("101")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((found.Single().BookingId, found.Single().CheckIn, found.Single().BookedAt)).IsEqualTo(("b1", Noon, Noon));
    }

    [Test]
    public async Task Put_EmptySetOfADefaultThatOmitsEmpty_ReadsBackEmpty()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var bookings = sandbox.Client.Table<Booking>(Tables.UniqueName());
        await sandbox.CreateTableAsync(bookings, bookings.CreateTable().WaitUntilActive());

        // Act
        await bookings.Put(new Booking { BookingId = "b1", CheckIn = Noon }).SendAsync();
        await bookings.Update(Booking.Keys.Of("b1", Noon)).Add(Booking.Attrs.SpecialRequests, ["late"]).SendAsync();
        var booking = await bookings.Get(Booking.Keys.Of("b1", Noon)).Consistent().ItemAsync();

        // Assert
        await Assert.That(booking!.SpecialRequests.Single()).IsEqualTo("late");
    }
}
