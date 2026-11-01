using System.Globalization;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Serialization;
using TUnit.Assertions.Enums;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Generated entities through the table API against real storage: what a backend stores is what the entity declares, and it reads back.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class GeneratedEntityTests(Backend backend)
{
    private static readonly int[] Deltas = [999_999, -999_999, 100, -100, 11, -11, 2, -2, 1, -1, 0, -101];

    private static readonly Shipment Full = new()
    {
        Customer = "alice",
        Number = "S-1",
        State = ShipmentState.Sent,
        Weight = 12.5m,
        Tracking = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Note = "fragile",
        Destination = new Depot("Bern", 4),
        Parcels = [new Parcel { Label = "outer", Contents = [new Parcel { Label = "inner", Contents = [new Parcel { Label = "core" }] }] }],
        Tags = ["red", "blue"],
        History = [ShipmentState.Packed, ShipmentState.Sent],
        Counters = new Dictionary<string, int> { ["scans"] = 3 },
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1793448000),
    };

    private static Attr<string, StringConverter> RouteEndCity => Shipment.Attrs.Route.At(Leg.Attrs.To).At(Depot.Attrs.City);

    [Test]
    public async Task Put_FullEntity_ReadsBackThroughTheEntity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);

        // Act
        await shipments.Put(Full).SendAsync();
        var read = await shipments.Get(Full).Consistent().SendAsync();

        // Assert
        var back = read.Item!;
        await Assert.That((back.Customer, back.Number, back.State, back.Weight, back.Tracking, back.Note, back.Destination, back.ExpiresAt, back.Revision))
            .IsEqualTo((Full.Customer, Full.Number, Full.State, Full.Weight, Full.Tracking, Full.Note, Full.Destination, Full.ExpiresAt, 1L));
        await Assert.That((back.Tags.SetEquals(Full.Tags), back.History.SetEquals(Full.History), back.Counters["scans"])).IsEqualTo((true, true, 3));
        await Assert.That(back.Parcels[0].Contents[0].Contents[0].Label).IsEqualTo("core");
    }

    [Test]
    public async Task Put_FullEntity_StoresEachAttributeInItsDynamoDbType()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);

        // Act
        await shipments.Put(Full).SendAsync();
        var stored = await sandbox.ReadAsync("alice", "S-1");

        // Assert: the attribute DynamoDB's TTL reads must be a number, and enum names must be strings.
        await Assert.That((stored!["_type"].Type, stored["ttl"].Type, stored["State"].Type, stored["History"].Type, stored["Destination"].Type, stored["Parcels"].Type))
            .IsEqualTo((AttributeType.String, AttributeType.Number, AttributeType.String, AttributeType.StringSet, AttributeType.Map, AttributeType.List));
    }

    [Test]
    public async Task Put_MinimalEntity_LeavesOutNullsAndEmptySets()
    {
        // Arrange (DynamoDB rejects an empty set, so a store that accepts this item proves it was left out)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        var minimal = new Shipment { Customer = "bob", Number = "S-2", State = ShipmentState.Packed, Weight = 0m };

        // Act
        await shipments.Put(minimal).SendAsync();
        var stored = await sandbox.ReadAsync("bob", "S-2");

        // Assert
        await Assert.That((stored!.ContainsKey("Tags"), stored.ContainsKey("History"), stored.ContainsKey("Note"), stored.ContainsKey("Tracking"), stored.ContainsKey("ttl")))
            .IsEqualTo((false, false, false, false, false));
    }

    [Test]
    public async Task Get_MinimalItem_GivesEmptySets()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        var minimal = new Shipment { Customer = "bob", Number = "S-2", State = ShipmentState.Packed, Weight = 0m };
        await shipments.Put(minimal).SendAsync();

        // Act
        var read = await shipments.Get(minimal).Consistent().SendAsync();

        // Assert
        await Assert.That((read.Item!.Tags.Count, read.Item.History.Count, read.Item.Note)).IsEqualTo((0, 0, (string?)null));
    }

    [Test]
    public async Task Get_NoItem_FindsNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);

        // Act
        var read = await shipments.Get(Full).Consistent().SendAsync();

        // Assert
        await Assert.That(read.Found).IsFalse();
    }

    [Test]
    public async Task Get_ItemOfAnotherType_ThrowsNamingIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "alice", ["sk"] = "S-1", ["_type"] = "INVOICE", ["State"] = "Sent", ["Weight"] = 1 }).SendAsync();
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);

        // Act
        var failure = await Capture(() => shipments.Get(Full).Consistent().SendAsync().AsTask());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("The item is of type \"INVOICE\", so it cannot be read as Shipment.");
    }

    [Test]
    public async Task Get_ItemWithoutARequiredAttribute_ThrowsNamingIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "alice", ["sk"] = "S-1", ["State"] = "Sent" }).SendAsync();
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);

        // Act
        var failure = await Capture(() => shipments.Get(Full).Consistent().SendAsync().AsTask());

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName)).IsEqualTo(("Shipment", (string?)"Weight"));
    }

    [Test]
    public async Task Put_StructEntity_ReadsBack()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var meters = sandbox.Client.Table<Meter>(await sandbox.CreateTableAsync());
        var meter = new Meter { Id = "m", Count = long.MaxValue, Active = true };

        // Act
        await meters.Put(meter).SendAsync();
        var read = await meters.Get(meter).Consistent().SendAsync();

        // Assert
        await Assert.That(read.Item).IsEqualTo(meter);
    }

    [Test]
    public async Task Put_ExpiresAt_StoresWholeEpochSeconds()
    {
        // Arrange (TTL reads a number of seconds, and DynamoDB ignores an item with anything else in the attribute)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);

        // Act
        await shipments.Put(Full with { ExpiresAt = Full.ExpiresAt!.Value.AddMilliseconds(750) }).SendAsync();
        var stored = await sandbox.ReadAsync("alice", "S-1");

        // Assert
        await Assert.That(stored!["ttl"].AsNumber()).IsEqualTo("1793448000");
    }

    [Test]
    public async Task Query_FilterOnTheTtlHandle_ComparesAsNumbers()
    {
        // Arrange (deletion lags by days, so a read that must not see expired items filters on the attribute)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        var now = Full.ExpiresAt!.Value;
        await shipments.Put(Full with { Number = "expired", ExpiresAt = now.AddSeconds(-1) }).SendAsync();
        await shipments.Put(Full with { Number = "valid", ExpiresAt = now.AddSeconds(1) }).SendAsync();

        // Act
        var valid = await shipments.Query("alice").Filter(Shipment.Attrs.ExpiresAt.Gt(now)).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(valid.Select(shipment => shipment.Number)).IsEquivalentTo(["valid"]);
    }

    [Test]
    public async Task Update_TakingTheLastElementOfASet_LeavesNoAttribute()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        await shipments.Put(Full with { Tags = ["red"] }).SendAsync();

        // Act
        await shipments.Update(Shipment.Keys.Of("alice", "S-1")).IgnoreVersion().Delete(Shipment.Attrs.Tags, ["red"]).SendAsync();
        var stored = await sandbox.ReadAsync("alice", "S-1");

        // Assert
        await Assert.That(stored!.ContainsKey("Tags")).IsFalse();
    }

    [Test]
    public async Task Update_SetToAnEmptySet_IsRejectedByDynamoDb()
    {
        // Arrange (a put leaves an empty set out, but an update writes what it is given, and DynamoDB has no empty sets)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        await shipments.Put(Full).SendAsync();

        // Act
        async Task Act() => await shipments.Update(Shipment.Keys.Of("alice", "S-1")).IgnoreVersion().Set(Shipment.Attrs.Tags, []).SendAsync();

        // Assert
        await Assert.That(Act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task Put_NestingAtTheDepthDynamoDbAllows_ReadsBack()
    {
        // Arrange (a parcel takes two levels, a list and a map, so 15 of them are 31 of the 32 that DynamoDB takes)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        var parcel = new Parcel { Label = "1" };
        for (var depth = 2; depth <= 15; depth++)
        {
            parcel = new Parcel { Label = depth.ToString(CultureInfo.InvariantCulture), Contents = [parcel] };
        }

        // Act
        await shipments.Put(Full with { Parcels = [parcel] }).SendAsync();
        var read = (await shipments.Get(Full).Consistent().SendAsync()).Item!;

        // Assert
        var deepest = read.Parcels[0];
        while (deepest.Contents.Count > 0)
        {
            deepest = deepest.Contents[0];
        }

        await Assert.That(deepest.Label).IsEqualTo("1");
    }

    [Test]
    public async Task Update_HandleTwoDocumentsDeep_ChangesOneAttributeOfTheInnerDocument()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shipments = sandbox.Client.Table<Shipment>(sandbox.TableName);
        await shipments.Put(Full with { Route = new Leg(new Depot("Bern"), new Depot("Thun", 2)) }).SendAsync();

        // Act
        var result = await shipments.Update(Shipment.Keys.Of("alice", "S-1")).IgnoreVersion().Set(RouteEndCity, "Spiez").If(RouteEndCity.Eq("Thun")).ReturnNew().SendAsync();

        // Assert
        await Assert.That(result.Attributes!.Route).IsEqualTo(new Leg(new Depot("Bern"), new Depot("Spiez", 2)));
    }

    [Test]
    public async Task Query_SignedIntegersInASortKey_ReturnTheNumericOrder()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var movements = sandbox.Client.Table<Movement>(sandbox.TableName);
        await PutMovementsAsync(movements);

        // Act
        var read = await movements.Query(Movement.Keys.Pk.Format("KB-01")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(read.Select(movement => movement.Delta).ToArray()).IsEquivalentTo(Deltas.Order(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BetweenPrefixAndUpperBoundOfNegativeIntegers_IsInclusive()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var movements = sandbox.Client.Table<Movement>(sandbox.TableName);
        await PutMovementsAsync(movements);

        // Act
        var read = await movements.Query(Movement.Keys.Pk.Format("KB-01"), SortKey.Between(Movement.Keys.Sk.Prefix(-100), Movement.Keys.Sk.UpperBound(-1))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(read.Select(movement => movement.Delta).ToArray()).IsEquivalentTo(Deltas.Where(delta => delta is >= -100 and <= -1).Order(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_PrefixOfANegativeInteger_MatchesThatValueOnly()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var movements = sandbox.Client.Table<Movement>(sandbox.TableName);
        await PutMovementsAsync(movements);

        // Act
        var read = await movements.Query(Movement.Keys.Pk.Format("KB-01"), SortKey.BeginsWith(Movement.Keys.Sk.Prefix(-1))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(read.Select(movement => movement.Delta)).IsEquivalentTo([-1]);
    }

    [Test]
    public async Task Query_IndexOverANumberAttribute_OrdersByTheNumberWithNegatives()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var spec = new TableSpec(SortKeyType: KeyAttributeType.String, GlobalIndexes: [new GlobalIndexSpec("ByPoints", "Game", SortKey: "Points", SortKeyType: KeyAttributeType.Number)]);
        var scores = sandbox.Client.Table<Score>(await sandbox.CreateTableAsync(spec));
        int[] points = [10, -2, 9, 0, -10];
        foreach (var point in points)
        {
            await scores.Put(new Score { Player = "alice", Round = $"r{point}", Game = "chess", Points = point }).SendAsync();
        }

        // Act
        var read = await scores.Index("ByPoints").Query("chess", SortKey.Between(AttributeValue.FromNumber(-5), AttributeValue.FromNumber(9))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(read.Select(score => score.Points).ToArray()).IsEquivalentTo([-2, 0, 9], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Put_IndexOverItsOwnAttributes_StoresNoExtraAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var spec = new TableSpec(SortKeyType: KeyAttributeType.String, GlobalIndexes: [new GlobalIndexSpec("ByPoints", "Game", SortKey: "Points", SortKeyType: KeyAttributeType.Number)]);
        var table = await sandbox.CreateTableAsync(spec);
        var scores = sandbox.Client.Table<Score>(table);

        // Act
        await scores.Put(new Score { Player = "alice", Round = "r1", Game = "chess", Points = 3 }).SendAsync();
        var stored = (await sandbox.Client.Table(table, "pk", "sk").Get("alice", "r1").Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That(stored.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["Game", "Points", "_type", "pk", "sk"], CollectionOrdering.Matching);
    }

    private static async Task PutMovementsAsync(Table<Movement> movements)
    {
        foreach (var delta in Deltas)
        {
            await movements.Put(new Movement { Sku = "KB-01", Delta = delta, Id = Guid.NewGuid() }).SendAsync();
        }
    }

    private static async Task<DynamoDbMappingException> Capture(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (DynamoDbMappingException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The action did not throw.");
    }
}
