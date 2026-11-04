namespace DynaSharp.Coordination;

/// <summary>
/// The item of a lock: its key and its attributes. The names are fixed, because every process that contends for a lock must read the same item
/// the same way, and they are part of the stored format, as <c>_type</c> is for entities.
/// </summary>
internal static class LockItem
{
    /// <summary>The prefix of the partition key: <c>LOCK#</c> and the name of the lock.</summary>
    public const string KeyPrefix = "LOCK#";

    /// <summary>The sort key, on a table that has one.</summary>
    public const string SortKey = "LOCK";

    /// <summary>The value of the discriminator, so that a scan of a single table can tell a lock from an entity.</summary>
    public const string Discriminator = "LOCK";

    /// <summary>The discriminator attribute, the one entities use.</summary>
    public const string Type = "_type";

    /// <summary>The holder: an id made for each acquire, so a process that acquires twice holds two different leases. Absent while the lock is free.</summary>
    public const string Owner = "owner";

    /// <summary>The record version number, a new id on every acquire and renew. A contender takes over only if it stayed the same for a whole lease.</summary>
    public const string RecordVersion = "rvn";

    /// <summary>The fencing token, a number that every acquire raises by one with <c>ADD</c>, and that a release keeps.</summary>
    public const string FencingToken = "fence";

    /// <summary>The duration of the lease in milliseconds, which a contender waits out on its own clock.</summary>
    public const string LeaseDuration = "leaseMs";

    /// <summary>Builds the partition key of a lock.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <returns>The partition key.</returns>
    public static AttributeValue PartitionKey(string name) => KeyPrefix + name;

    /// <summary>Gets the sort key of a lock in a table.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The sort key, or none if the table has no sort key.</returns>
    public static AttributeValue SortKeyOf(Table<Item> table) => table.SortKeyName is null ? default(AttributeValue) : SortKey;

    /// <summary>Gets the lease duration as it is stored: whole milliseconds, rounded up, so that a contender never waits less than the holder holds.</summary>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <returns>The milliseconds.</returns>
    public static long Milliseconds(TimeSpan leaseDuration) => (leaseDuration.Ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond;

    /// <summary>Makes a new id for an owner or a record version.</summary>
    /// <returns>The id.</returns>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
