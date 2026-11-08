using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Service registry with liveness</see>: instances register and renew a time to live, and a reader sees those whose time has not run out. TTL only cleans up: DynamoDB deletes an expired item
/// days later, so liveness is a filter on the attribute, never the absence of the item.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ServiceRegistryTests(Backend backend)
{
    private const string Billing = "billing";

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Test]
    public async Task LiveAsync_InstanceThatStoppedRenewing_IsGoneThoughStillStored()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var instances = sandbox.Shop.Of<ServiceInstance>();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        await RegisterAsync(instances, clock, "i-1");
        await RegisterAsync(instances, clock, "i-2");
        clock.Advance(TimeSpan.FromSeconds(20));
        await HeartbeatAsync(instances, clock, "i-1");
        clock.Advance(TimeSpan.FromSeconds(20));

        // Act
        var live = await LiveAsync(instances, clock);
        var stored = await instances.Query(ServiceInstance.Keys.Pk.Of(Billing)).CountAsync();

        // Assert
        await Assert.That((live.Single().InstanceId, stored)).IsEqualTo(("i-1", 2));
    }

    [Test]
    public async Task HeartbeatAsync_InstanceDeletedByTtl_FailsInsteadOfCreatingHalfAnItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var instances = sandbox.Shop.Of<ServiceInstance>();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => HeartbeatAsync(instances, clock, "i-gone"));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }

    private static async Task RegisterAsync(Table<ServiceInstance> instances, TimeProvider clock, string id) =>
        await instances.Put(new ServiceInstance { Service = Billing, InstanceId = id, Address = "10.0.0.1:443", ExpiresAt = clock.GetUtcNow() + Ttl }).SendAsync();

    private static async Task HeartbeatAsync(Table<ServiceInstance> instances, TimeProvider clock, string id) =>
        await instances.Update(ServiceInstance.Keys.Of(Billing, id)).Set(ServiceInstance.Attrs.ExpiresAt, clock.GetUtcNow() + Ttl).IfExists().SendAsync();

    private static async Task<List<ServiceInstance>> LiveAsync(Table<ServiceInstance> instances, TimeProvider clock) =>
        await instances.Query(ServiceInstance.Keys.Pk.Of(Billing)).Filter(ServiceInstance.Attrs.ExpiresAt.Gt(clock.GetUtcNow())).ItemsAsync().ToListAsync();
}

[DynamoDbEntity]
[Discriminator("INSTANCE")]
[PartitionKey("SERVICE#{Service}")]
[SortKey("INSTANCE#{InstanceId}")]
internal sealed partial record ServiceInstance
{
    public required string Service { get; init; }

    public required string InstanceId { get; init; }

    public required string Address { get; init; }

    /// <summary>Gets when the instance counts as gone. The attribute is the TTL of the table, so it is a number of epoch seconds.</summary>
    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset ExpiresAt { get; init; }
}
