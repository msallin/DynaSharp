namespace DynaSharp.IntegrationTests.Backends;

/// <summary>Behavior of real DynamoDB that a backend does not implement. A test that needs it is skipped on that backend.</summary>
/// <remarks>When adding a gap: add the value, and the backends that lack it with the reason in <see cref="Gaps"/>.</remarks>
public enum Gap
{
    /// <summary>A transaction that touches an item another transaction is writing fails with TransactionConflict.</summary>
    TransactionConflict,

    /// <summary>A retried transaction request with the same token while the first is running fails with TransactionInProgress.</summary>
    TransactionInProgress,

    /// <summary>A stream with several shards and shard lineage (parent and child shards).</summary>
    MultiShardStreams,

    /// <summary>Enabling time to live on a table that has it already is a ValidationException.</summary>
    TimeToLiveEnabledTwiceRejected,

    /// <summary>The shard of a stream that was disabled stays readable and is closed: it has an ending sequence number and the last GetRecords returns no next iterator.</summary>
    ClosedShardsAfterDisablingTheStream,

    /// <summary>The shard of an enabled stream has no ending sequence number: only closed shards have one.</summary>
    OpenShardWithoutEndingSequenceNumber,

    /// <summary>DescribeStream returns the key schema of the table.</summary>
    StreamDescriptionKeySchema,

    /// <summary>The shard of a stream that has no record yet already has a starting sequence number.</summary>
    StartingSequenceNumberOfAnEmptyShard,

    /// <summary>TransactGetItems with a ProjectionExpression returns the projected attributes only, key attributes included only when projected.</summary>
    TransactGetProjectionWithoutKeys,

    /// <summary>A global secondary index of a table with provisioned capacity has capacity of its own: CreateTable and UpdateTable set it and DescribeTable reports it.</summary>
    GlobalIndexCapacity,

    /// <summary>UpdateTable changes the storage class of a table.</summary>
    UpdateTableClass,

    /// <summary>DescribeLimits returns the capacity limits of the account and the tables.</summary>
    DescribeLimits,

    /// <summary>A transaction reports the capacity of every item it writes: two write units per item of up to 1 KB, also for several items of one table.</summary>
    TransactionCapacityPerItem,
}

/// <summary>
/// The known gaps per backend. A gap is a skip with a reason, not a weaker assertion: the test states what DynamoDB does, and a
/// backend that does not do it is marked here. Dynoxide's ConsistentRead is a no-op as far as reads go, but nothing a test can
/// observe differs: it rejects consistent reads on a global secondary index and reports the doubled read capacity, so no test
/// needs a gap for it.
/// </summary>
public static class Gaps
{
    private static readonly Dictionary<(Backend Backend, Gap Gap), string> Known = new()
    {
        [(Backend.Dynoxide, Gap.TransactionConflict)] = "Dynoxide has no TransactionConflict: it runs transactions one at a time.",
        [(Backend.Dynoxide, Gap.TransactionInProgress)] = "Dynoxide has no TransactionInProgress: it runs transactions one at a time.",
        [(Backend.Dynoxide, Gap.MultiShardStreams)] = "Dynoxide streams have a single shard.",
        [(Backend.Dynoxide, Gap.TimeToLiveEnabledTwiceRejected)] = "Dynoxide accepts UpdateTimeToLive that changes nothing.",
        [(Backend.Dynoxide, Gap.ClosedShardsAfterDisablingTheStream)] = "Dynoxide drops the stream when it is disabled, so its shard is gone instead of closed.",
        [(Backend.Dynoxide, Gap.OpenShardWithoutEndingSequenceNumber)] = "Dynoxide reports the last sequence number as the ending sequence number of its open shard.",
        [(Backend.Dynoxide, Gap.StreamDescriptionKeySchema)] = "Dynoxide leaves the key schema out of DescribeStream.",
        [(Backend.Dynoxide, Gap.StartingSequenceNumberOfAnEmptyShard)] = "Dynoxide reports an empty sequence number range until the first record arrives.",
        [(Backend.Dynoxide, Gap.TransactGetProjectionWithoutKeys)] = "Dynoxide adds the key attributes to the items TransactGetItems projects.",
        [(Backend.Dynoxide, Gap.GlobalIndexCapacity)] = "Dynoxide ignores the capacity of a global secondary index in UpdateTable and reports none for a new one.",
        [(Backend.Dynoxide, Gap.DescribeLimits)] = "Dynoxide answers DescribeLimits with UnknownOperationException.",
        [(Backend.DynamoDbLocal, Gap.UpdateTableClass)] = "DynamoDB Local rejects an UpdateTable that only changes the table class with 'Nothing to update'.",
        [(Backend.DynamoDbLocal, Gap.TransactionCapacityPerItem)] = "DynamoDB Local reports 2 units for a transaction of two items of one table.",
    };

    /// <summary>Skips the running test when <paramref name="backend"/> lacks <paramref name="gap"/>.</summary>
    /// <param name="backend">The backend of the test.</param>
    /// <param name="gap">The behavior the test needs.</param>
    public static void SkipIfMissing(Backend backend, Gap gap)
    {
        if (Known.TryGetValue((backend, gap), out var reason))
        {
            Skip.Test($"{backend}: {reason}");
        }
    }
}
