using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// Behavior a test needs that a backend does not have, so the test is skipped on that backend. Mostly that is behavior of real DynamoDB that an emulator does not implement.
/// The values at the end go the other way, see <see cref="Gaps"/>.
/// </summary>
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

    /// <summary>GetShardIterator at the starting sequence number of a shard reads from its first record, also when the records of other streams came in between.</summary>
    ReadAtTheStartingSequenceNumber,

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

    /// <summary>PartiQL: ExecuteStatement, BatchExecuteStatement and ExecuteTransaction.</summary>
    PartiQl,

    /// <summary>A table takes tags when it is created.</summary>
    Tags,

    /// <summary>A table created with server-side encryption enabled reports it in SSEDescription.</summary>
    ServerSideEncryptionDescription,

    /// <summary>OnDemandThroughput on a table with provisioned capacity is a ValidationException.</summary>
    OnDemandThroughputRejectedWithProvisionedCapacity,

    /// <summary>A global secondary index takes OnDemandThroughput in CreateTable, and DescribeTable reports it.</summary>
    GlobalIndexOnDemandThroughput,

    /// <summary>UpdateTable changes the OnDemandThroughput of a global secondary index.</summary>
    UpdateGlobalIndexOnDemandThroughput,

    /// <summary>UpdateTable changes the server-side encryption of a table.</summary>
    UpdateServerSideEncryption,

    /// <summary>A BatchGetItem whose response would pass 16 MB returns the rest as UnprocessedKeys.</summary>
    BatchResponseLimit,

    /// <summary>A stream has one shard, so a test knows where its records are. What only the emulators do: a table of real DynamoDB has several partitions.</summary>
    OneShardPerStream,

    /// <summary>The time to live of a table can change again right after a change. What only the emulators do: real DynamoDB allows one change an hour.</summary>
    TimeToLiveChangesAgainAtOnce,

    /// <summary>A table is gone, and its name free, when DeleteTable returns. What only the emulators do: real DynamoDB takes it through DELETING.</summary>
    TableGoneAtOnce,

    /// <summary>A stream ARN of an account nobody has answers ResourceNotFoundException. What only the emulators do: real DynamoDB checks the account first.</summary>
    StreamOfAnUnknownAccountIsNotFound,
}

/// <summary>
/// The known gaps per backend. A gap is a skip with a reason, not a weaker assertion: the test states what DynamoDB does, and a
/// backend that does not do it is marked here. Dynoxide's ConsistentRead is a no-op as far as reads go, but nothing a test can
/// observe differs: it rejects consistent reads on a global secondary index and reports the doubled read capacity, so no test
/// needs a gap for it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Backend.RealAws"/> has every gap of the emulators covered, since each is a statement of what DynamoDB does: such a test runs there, which is what checks the statement. No
/// test needs <see cref="Gap.TransactionConflict"/>, <see cref="Gap.TransactionInProgress"/> or <see cref="Gap.MultiShardStreams"/>, because nothing can make them happen on demand, on an
/// emulator or on real AWS.
/// </para>
/// <para>
/// Real AWS can also produce what no emulator does, and a test must not call that a failure: a throttle, <c>UnprocessedItems</c> and <c>UnprocessedKeys</c>, <c>TransactionConflict</c>,
/// several shards, a table that is busy, and reads that lag behind writes. Where the tests can bear it, the setup absorbs it for all of them: the clients retry, <see cref="Tables"/> waits
/// for tables, <see cref="StreamReading"/> reads every shard, and <see cref="SettlingHandler"/> lets writes settle. A test that needs what only an emulator does has a gap of its own, at the end of <see cref="Gap"/>.
/// A real rejection that the first runs still bring up is a new row here or a wider test, and never a weaker assertion for everybody.
/// </para>
/// </remarks>
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
        [(Backend.Dynoxide, Gap.GlobalIndexOnDemandThroughput)] = "Dynoxide ignores the OnDemandThroughput of a global secondary index and reports none.",
        [(Backend.Dynoxide, Gap.UpdateGlobalIndexOnDemandThroughput)] = "Dynoxide requires ProvisionedThroughput in the Update of a global secondary index, also on demand.",
        [(Backend.Dynoxide, Gap.UpdateServerSideEncryption)] = "Dynoxide rejects an UpdateTable with only SSESpecification as one that changes nothing.",
        [(Backend.DynamoDbLocal, Gap.ReadAtTheStartingSequenceNumber)] = "DynamoDB Local numbers the records of all streams from one counter and answers TrimmedDataAccessException for a position below the first record of a shard.",
        [(Backend.DynamoDbLocal, Gap.UpdateTableClass)] = "DynamoDB Local rejects an UpdateTable that only changes the table class with 'Nothing to update'.",
        [(Backend.DynamoDbLocal, Gap.TransactionCapacityPerItem)] = "DynamoDB Local reports 2 units for a transaction of two items of one table.",
        [(Backend.DynamoDbLocal, Gap.ServerSideEncryptionDescription)] = "DynamoDB Local accepts SSESpecification and leaves SSEDescription out of the table description.",
        [(Backend.DynamoDbLocal, Gap.OnDemandThroughputRejectedWithProvisionedCapacity)] = "DynamoDB Local accepts OnDemandThroughput on a table with provisioned capacity, and drops it.",
        [(Backend.DynamoDbLocal, Gap.UpdateGlobalIndexOnDemandThroughput)] = "DynamoDB Local accepts the OnDemandThroughput of an index in UpdateTable and keeps the old one.",
        [(Backend.DynamoDbLocal, Gap.UpdateServerSideEncryption)] = "DynamoDB Local rejects an UpdateTable with only SSESpecification with 'Nothing to update'.",

        // The fake's rows name only what docs/testing.md lists as outside the fake, see Backend.Fake.
        [(Backend.Fake, Gap.PartiQl)] = "The fake does not answer PartiQL: ExecuteStatement is UnknownOperationException.",
        [(Backend.Fake, Gap.Tags)] = "The fake does not keep tags: Tags in CreateTable is UnsupportedByFakeException.",
        [(Backend.Fake, Gap.BatchResponseLimit)] = "The fake leaves no key unprocessed: it does not cut a BatchGetItem response at 16 MB.",
        [(Backend.RealAws, Gap.OneShardPerStream)] = "A stream of real DynamoDB can have several shards, and the records of the test may be in any of them.",
        [(Backend.RealAws, Gap.TimeToLiveChangesAgainAtOnce)] = "Real DynamoDB refuses a second change of the time to live of a table within an hour.",
        [(Backend.RealAws, Gap.TableGoneAtOnce)] = "Real DynamoDB keeps a table in DELETING for a while, and its name stays taken until it is gone.",
        [(Backend.RealAws, Gap.StreamOfAnUnknownAccountIsNotFound)] = "IAM refuses the ARN of another account before DynamoDB looks for the stream.",
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

    /// <summary>Tells whether a backend lacks a behavior.</summary>
    /// <param name="backend">The backend.</param>
    /// <param name="gap">The behavior.</param>
    /// <returns><see langword="true"/> if a test that needs it is skipped there.</returns>
    internal static bool IsMissing(Backend backend, Gap gap) => Known.ContainsKey((backend, gap));
}
