using System.Text.Json;
using System.Text.Json.Serialization;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Documents of an external contract</see>: a service keeps an event whose schema another team owns, such as a message of a Kafka topic, as the
/// JSON it is. A <see cref="JsonElement"/> property stores it as a native map, so a filter, a projection and an update reach into it by path, which a JSON string in one attribute does not
/// allow, and the contract is serialized with a source-generated context, so it is safe under Native AOT. Dates stay the text the contract wrote. Numbers keep their value, and DynamoDB
/// keeps them in a canonical form, so <c>1.50</c> reads back as <c>1.5</c> (DynamoDB Local keeps the text).
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ExternalContractTests(Backend backend)
{
    private static readonly ParcelScanned Light = new("p1", "2026-11-06T10:00:00+01:00", new ParcelWeight(1.50m, "kg"), ["fragile"], null);

    private static readonly ParcelScanned Heavy = new("p2", "2026-11-06T10:05:00+01:00", new ParcelWeight(12.5m, "kg"), [], "Ring twice");

    [Test]
    public async Task PutThenGet_Event_DeserializesToTheContract()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var scans = sandbox.Shop.Of<ScanRecord>();

        // Act
        await scans.Put(ScanRecord.Of(Light)).SendAsync();
        var stored = await scans.Get(ScanRecord.Keys.Of("p1")).Consistent().ItemAsync();

        // Assert
        await Assert.That(JsonSerializer.Deserialize(stored!.Event, ContractJson.Default.ParcelScanned)).IsEqualTo(Light);
    }

    [Test]
    public async Task Scan_FilterByAPathInsideTheEvent_FindsTheHeavyParcel()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var scans = sandbox.Shop.Of<ScanRecord>();
        await scans.Put(ScanRecord.Of(Light)).SendAsync();
        await scans.Put(ScanRecord.Of(Heavy)).SendAsync();

        // Act
        var heavy = await scans.Scan().Filter(Attr("Event.weight.kilograms").Gt(10)).Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(heavy.Select(static scan => scan.ParcelId).ToArray()).IsEquivalentTo(["p2"]);
    }

    [Test]
    public async Task Update_SetAPathInsideTheEvent_ChangesThatFieldAlone()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var scans = sandbox.Shop.Of<ScanRecord>();
        await scans.Put(ScanRecord.Of(Light)).SendAsync();

        // Act
        await scans.Update(ScanRecord.Keys.Of("p1")).Set("Event.comment", "Left at the door").SendAsync();
        var stored = await scans.Get(ScanRecord.Keys.Of("p1")).Consistent().ItemAsync();

        // Assert
        await Assert.That(JsonSerializer.Deserialize(stored!.Event, ContractJson.Default.ParcelScanned)).IsEqualTo(Light with { Comment = "Left at the door" });
    }

    [Test]
    public async Task Project_APathInsideTheEvent_ReadsThatPartOnly()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<ScanRecord>().Put(ScanRecord.Of(Light)).SendAsync();

        // Act
        var item = await sandbox.Shop.Get(ScanRecord.Keys.Pk.Format("p1"), "SCAN").Project("Event.weight").Consistent().ItemAsync();

        // Assert
        var json = item!["Event"].ToJson();
        await Assert.That((json.EnumerateObject().Select(static field => field.Name).Single(), json.GetProperty("weight").GetProperty("kilograms").GetDecimal())).IsEqualTo(("weight", 1.5m));
    }
}

internal sealed record ParcelScanned(string ParcelId, string ScannedAt, ParcelWeight Weight, IReadOnlyList<string> Tags, string? Comment)
{
    public bool Equals(ParcelScanned? other) =>
        other is not null && (ParcelId, ScannedAt, Weight, Comment) == (other.ParcelId, other.ScannedAt, other.Weight, other.Comment) && Tags.SequenceEqual(other.Tags);

    public override int GetHashCode() => HashCode.Combine(ParcelId, ScannedAt, Weight, Comment);
}

internal sealed record ParcelWeight(decimal Kilograms, string Unit);

[JsonSerializable(typeof(ParcelScanned))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ContractJson : JsonSerializerContext;

[DynamoDbEntity]
[Discriminator("SCAN")]
[PartitionKey("PARCEL#{ParcelId}")]
[SortKey("SCAN")]
internal sealed partial record ScanRecord
{
    public required string ParcelId { get; init; }

    public required JsonElement Event { get; init; }

    public static ScanRecord Of(ParcelScanned scanned) => new() { ParcelId = scanned.ParcelId, Event = JsonSerializer.SerializeToElement(scanned, ContractJson.Default.ParcelScanned) };
}
