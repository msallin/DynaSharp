using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Idempotency keys</see>: a client retries with the key it chose, so the second call does nothing and answers like the first. A record of the key goes in the same transaction as the effect and holds the
/// answer. TTL cleans the records up and must outlast the retry horizon of the client: after it, the call counts as new. TTL deletes days late, so the condition goes by the time, not by the absence of the record.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class IdempotencyKeyTests(Backend backend)
{
    private const string Key = "request-1";

    private static readonly TimeSpan Horizon = TimeSpan.FromHours(24);

    private static ManualClock NewClock => new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task ChargeAsync_Retry_AnswersLikeTheFirstCallAndChargesOnce()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var clock = NewClock;
        var first = await ChargeAsync(sandbox, clock, Key, "first");

        // Act
        var retry = await ChargeAsync(sandbox, clock, Key, "second");

        // Assert
        await Assert.That((retry, await sandbox.Shop.Of<Charge>().Query(Charge.Keys.Pk.Of()).Consistent().CountAsync())).IsEqualTo((first, 1));
    }

    [Test]
    public async Task ChargeAsync_ConcurrentRetries_ChargeOnce()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var clock = NewClock;

        // Act
        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => ChargeAsync(sandbox, clock, Key, $"charge{i}")));

        // Assert
        await Assert.That((answers.Distinct().Count(), await sandbox.Shop.Of<Charge>().Query(Charge.Keys.Pk.Of()).Consistent().CountAsync())).IsEqualTo((1, 1));
    }

    [Test]
    public async Task ChargeAsync_RetryAfterTheHorizon_ChargesAgainThoughTheRecordIsStillThere()
    {
        // Arrange (this is why the TTL must be longer than any retry)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var clock = NewClock;
        await ChargeAsync(sandbox, clock, Key, "first");

        // Act
        clock.Advance(Horizon + TimeSpan.FromSeconds(1));
        var late = await ChargeAsync(sandbox, clock, Key, "second");

        // Assert
        await Assert.That((late, await sandbox.Shop.Of<Charge>().Query(Charge.Keys.Pk.Of()).Consistent().CountAsync())).IsEqualTo(("second", 2));
    }

    [Test]
    public async Task ChargeAsync_ExpiryOfTheRecord_IsANumberAsTtlNeedsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var clock = NewClock;

        // Act
        await ChargeAsync(sandbox, clock, Key, "first");
        var stored = await sandbox.Shop.Get(IdempotencyRecord.Keys.Pk.Format(Key), IdempotencyRecord.Keys.Sk.Format()).Consistent().ItemAsync();

        // Assert
        await Assert.That(stored!["ttl"].AsNumber<long>()).IsEqualTo((clock.GetUtcNow() + Horizon).ToUnixTimeSeconds());
    }

    private static Task<string> ChargeAsync(CommandSandbox sandbox, TimeProvider clock, string key, string chargeId)
    {
        var records = sandbox.Shop.Of<IdempotencyRecord>();
        var now = clock.GetUtcNow();
        return records.RetryOnConflictAsync(async ct =>
        {
            TransactMarker answered = default;
            try
            {
                await sandbox.Client.TransactWrite()
                    .Put(records, new IdempotencyRecord { Key = key, Answer = chargeId, ExpiresAt = now + Horizon }, out answered)
                    .If(IdempotencyRecord.Attrs.ExpiresAt.NotExistsOrLt(now))
                    .ReturnOldOnFailure()
                    .Put(sandbox.Shop.Of<Charge>(), new Charge { Id = chargeId })
                    .SendAsync(ct);
                return chargeId;
            }
            catch (TransactionCanceledException exception) when (exception.ReasonFor(answered) == TransactionFailure.ConditionalCheckFailed)
            {
                // The one condition of the transaction, so it ends here, and what leaves the delegate is a conflict: the first call is still running, and only asking again gets its answer.
                return exception.ItemFor(answered)!.To<IdempotencyRecord>().Answer;
            }
        });
    }
}

[DynamoDbEntity]
[Discriminator("IDEMPOTENCY")]
[PartitionKey("IDEMPOTENCY#{Key}")]
[SortKey("RECORD")]
internal sealed partial record IdempotencyRecord
{
    public required string Key { get; init; }

    public required string Answer { get; init; }

    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset ExpiresAt { get; init; }
}

[DynamoDbEntity]
[Discriminator("CHARGE")]
[PartitionKey("CHARGES")]
[SortKey("{Id}")]
internal sealed partial record Charge
{
    public required string Id { get; init; }
}
