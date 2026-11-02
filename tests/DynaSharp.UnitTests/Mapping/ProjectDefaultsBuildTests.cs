using System.Reflection;
using DynaSharp.DefaultsFixture;
using DynaSharp.Mapping;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>What <see cref="DynamoDbDefaultsAttribute"/> and <see cref="DynamoDbDefaultConverterAttribute"/> do in a real build: the types of <c>DynaSharp.DefaultsFixture</c>, a project that sets them all.</summary>
public sealed class ProjectDefaultsBuildTests
{
    private const long Millis = 1700000000123;

    private static readonly DateTimeOffset Seen = DateTimeOffset.FromUnixTimeMilliseconds(Millis);

    private static readonly Guest Anna = new() { GuestId = "g1", GuestName = "Anna", LastSeen = Seen };

    private static readonly Booking Stay = new() { BookingId = "b1", CheckIn = Seen, BookedAt = Seen };

    [Test]
    [Arguments("""
        "guest_id":{"S":"g1"}
        """)]
    [Arguments("""
        "guest_name":{"S":"Anna"}
        """)]
    [Arguments("""
        "VIP":{"BOOL":false}
        """)]
    [Arguments("""
        "_type":{"S":"GUEST"}
        """)]
    public async Task ToRequestJson_PutOfAGuest_StoresTheConvertedNames(string expected)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Guest>("Shop").Put(Anna).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(expected);
    }

    [Test]
    public async Task ToRequestJson_GetOfAPlainKey_NamesItsConvertedAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Guest>("Shop").Get(Guest.Keys.Of("g1")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Key":{"guest_id":{"S":"g1"}}
            """);
    }

    [Test]
    public async Task Info_EntityWithAPlainKey_NamesItsConvertedAttribute()
    {
        // Arrange, Act
        var info = InfoOf<Guest>();

        // Assert
        await Assert.That((info.PartitionKeyName, info.Discriminator)).IsEqualTo(("guest_id", "GUEST"));
    }

    [Test]
    public async Task Info_EntityWithTemplates_KeepsTheFrozenNamesAndHasNoDiscriminator()
    {
        // Arrange, Act
        var info = InfoOf<Booking>();

        // Assert
        await Assert.That((info.PartitionKeyName, info.SortKeyName, info.Indexes[0].PartitionKeyName, info.Discriminator)).IsEqualTo(("pk", "sk", "byroompk", (string?)null));
    }

    [Test]
    public async Task ToRequestJson_PutOfABooking_HasNoDiscriminatorAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Booking>("Shop").Put(Stay).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("_type");
    }

    [Test]
    public async Task Attrs_Handle_HasTheConvertedPath()
    {
        // Arrange, Act
        var path = Guest.Attrs.HomeAddress.At(HomeAddress.Attrs.ZipCode).Path;

        // Assert
        await Assert.That(path).IsEqualTo("home_address.zip_code");
    }

    [Test]
    public async Task ToRequestJson_SetOfAnInputOfAnIndexByHandle_KeepsTheIndexKey()
    {
        // Arrange (an update finds the template of an input by the path of its handle, which is the converted name)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Booking>("Shop").Update(Booking.Keys.Of("b1", Seen)).Set(Booking.Attrs.RoomNumber, "101").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {"S":"ROOM#101"}
            """);
    }

    [Test]
    public async Task ToRequestJson_ProjectionByTheConvertedName_NeedsNoSuppression()
    {
        // Arrange (a declared name here would be DYNA213, which fails this build)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Guest>("Shop").Scan().Project("guest_name", "home_address.zip_code").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"_type","#n1":"guest_name","#n2":"home_address","#n3":"zip_code","#n4":"guest_id"}
            """);
    }

    [Test]
    public async Task ToRequestJson_DefaultConverter_WritesThePropertyItsNullableFormAndAList()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Guest>("Shop").Put(Anna with { FirstSeen = Seen, VisitDates = [Seen] }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains($$"""
            "last_seen":{"S":"{{Millis}}"},"first_seen":{"S":"{{Millis}}"},"visit_dates":{"L":[{"S":"{{Millis}}"}]}
            """);
    }

    [Test]
    public async Task ItemTo_DefaultConverter_ReadsWhatItWrote()
    {
        // Arrange
        var item = Item.From(Anna with { FirstSeen = Seen, VisitDates = [Seen] });

        // Act
        var guest = item.To<Guest>();

        // Assert
        await Assert.That((guest.LastSeen, guest.FirstSeen, guest.VisitDates[0])).IsEqualTo((Seen, (DateTimeOffset?)Seen, Seen));
    }

    [Test]
    public async Task ToRequestJson_TemplateKey_KeepsItsFormatBesideTheDefault()
    {
        // Arrange (a placeholder has formats of its own, and the attribute of the property goes through the default)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Booking>("Shop").Put(Stay).ToRequestJson();

        // Assert
        await Assert.That((json.Contains(Booking.Keys.Sk.Format(Seen), StringComparison.Ordinal), json.Contains("\"check_in\":{\"S\":\"1700000000123\"}", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task ToRequestJson_OmitEmpty_LeavesOutWhatTheDefaultCovers()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Guest>("Shop").Put(Anna with { PreferredName = string.Empty, HomeAddress = new HomeAddress { ZipCode = string.Empty } }).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("preferred_name", StringComparison.Ordinal), json.Contains("visit_dates", StringComparison.Ordinal), json.Contains("\"home_address\":{\"M\":{}}", StringComparison.Ordinal))).IsEqualTo((false, false, true));
    }

    [Test]
    public async Task ToRequestJson_OmitEmpty_KeepsWhatARequiredPropertyAndAWayOutSay()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Guest>("Shop").Put(Anna with { GuestName = string.Empty, KeptNote = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("\"guest_name\":{\"S\":\"\"}", StringComparison.Ordinal), json.Contains("\"kept_note\":{\"S\":\"\"}", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Put_OmitEmptyOnAnInputOfAKey_DoesNotLeaveItOut()
    {
        // Arrange (the key still says what is wrong with it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var bookings = client.Table<Booking>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = bookings.Put(Stay with { RoomNumber = string.Empty }).ToRequestJson());

        // Assert
        await Assert.That(failure).IsTypeOf<KeyFormatException>();
    }

    [Test]
    public async Task DynamoDbDefaultConverterAttribute_OnTheAssembly_SaysWhatItWasGiven()
    {
        // Arrange, Act
        var attribute = typeof(Guest).Assembly.GetCustomAttributes<DynamoDbDefaultConverterAttribute>().Single(static candidate => candidate.ValueType == typeof(DateTimeOffset));

        // Assert
        await Assert.That((attribute.ValueType, attribute.ConverterType)).IsEqualTo((typeof(DateTimeOffset), typeof(StoredTimestampConverter)));
    }

    [Test]
    public async Task ItemAsync_NameTheTypeLacksThroughADefault_ReadsAsWithAConverterOnTheProperty()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"room_number":{"S":"101"},"state":{"S":"TELEPORTED"},"prior_state":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var room = await client.Table<Room>("Shop").Get(Room.Keys.Of("101")).ItemAsync();

        // Assert
        await Assert.That((room!.State, room.PriorState)).IsEqualTo((RoomState.Unknown, (RoomState?)null));
    }

    [Test]
    public async Task ItemAsync_NameTheTypeHasThroughADefault_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"room_number":{"S":"101"},"state":{"S":"Taken"},"prior_state":{"S":"Free"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var room = await client.Table<Room>("Shop").Get(Room.Keys.Of("101")).ItemAsync();

        // Assert
        await Assert.That((room!.State, room.PriorState)).IsEqualTo((RoomState.Taken, (RoomState?)RoomState.Free));
    }

    [Test]
    public async Task ItemAsync_NameTheTypeLacksInAnElementThroughADefault_ThrowsBecauseAnElementIsNotAsked()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"room_number":{"S":"101"},"history":{"L":[{"S":"TELEPORTED"}]}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Room>("Shop").Get(Room.Keys.Of("101")).ItemAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("history");
    }

    [Test]
    public async Task SendAsync_SelectOfANameTheTypeLacksThroughADefault_GivesNullForANullableHandle()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"prior_state":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var state = await client.Table<Room>("Shop").Get(Room.Keys.Of("101")).Select(Room.Attrs.PriorState).SendAsync();

        // Assert
        await Assert.That((state.Found, state.Value)).IsEqualTo((true, (RoomState?)null));
    }

    [Test]
    public async Task SendAsync_SelectOfANameTheTypeLacksThroughADefault_ThrowsForAHandleThatCannotBeNull()
    {
        // Arrange (DYNA209 reports this where it sees a handle, and a variable is no such place)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"state":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);
        var state = Room.Attrs.State;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Room>("Shop").Get(Room.Keys.Of("101")).Select(state).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("state");
    }

    [Test]
    public async Task DynamoDbDefaultsAttribute_OnTheAssembly_SaysWhatItWasGiven()
    {
        // Arrange, Act
        var attribute = typeof(Guest).Assembly.GetCustomAttribute<DynamoDbDefaultsAttribute>()!;

        // Assert
        await Assert.That((attribute.Naming, attribute.NoDiscriminator, attribute.OmitEmpty)).IsEqualTo((AttributeNaming.SnakeCase, true, true));
    }

    private static DynamoDbEntityInfo InfoOf<T>()
        where T : IDynamoDbEntity<T> => T.Info;
}
