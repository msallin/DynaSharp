using System.Globalization;

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

    /// <summary>
    /// Reads who holds a lock from its item, and checks that the item is one that <see cref="LockTable"/> wrote: an <c>owner</c> and an <c>rvn</c> that are
    /// strings, and a <c>leaseMs</c> that is a whole number from 1 to the milliseconds of a day.
    /// </summary>
    /// <param name="item">The item, or <see langword="null"/> if there is none.</param>
    /// <param name="name">The name of the lock, for the message.</param>
    /// <param name="table">The name of the table, for the message.</param>
    /// <returns>The holder, or <see langword="null"/> if the lock is free: no item, or an item without <c>owner</c>.</returns>
    /// <exception cref="InvalidOperationException">The item has an <c>owner</c>, but is not a lock item written by <see cref="LockTable"/>.</exception>
    public static Holder? HolderOf(Item? item, string name, string table)
    {
        if (item is null || !item.ContainsKey(Owner))
        {
            return null;
        }

        var owner = Text(item, Owner, name, table);
        var recordVersion = Text(item, RecordVersion, name, table);
        var leaseDuration = TimeSpan.FromMilliseconds(Whole(item, LeaseDuration, 1, (long)TimeSpan.FromDays(1).TotalMilliseconds, name, table));
        return new(owner, recordVersion, leaseDuration);
    }

    /// <summary>Reads the fencing token of an item, 0 for none, and checks that it is a whole number that is not negative.</summary>
    /// <param name="item">The item, or <see langword="null"/> if there is none.</param>
    /// <param name="name">The name of the lock, for the message.</param>
    /// <param name="table">The name of the table, for the message.</param>
    /// <returns>The fencing token.</returns>
    /// <exception cref="InvalidOperationException">The item has a <c>fence</c> that is not a whole number from 0.</exception>
    public static long FencingTokenOf(Item? item, string name, string table) =>
        item is null || !item.ContainsKey(FencingToken) ? 0 : Whole(item, FencingToken, 0, long.MaxValue - 1, name, table);

    private static string Text(Item item, string attribute, string name, string table) =>
        item.TryGetValue(attribute, out var value) && value.Type == AttributeType.String
            ? value.AsString()
            : throw NotALock(name, table, $"its attribute {attribute} is missing or not a string");

    private static long Whole(Item item, string attribute, long min, long max, string name, string table)
    {
        if (item.TryGetValue(attribute, out var value) && value.Type == AttributeType.Number
            && long.TryParse(value.AsNumber(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) && whole >= min && whole <= max)
        {
            return whole;
        }

        throw NotALock(name, table, $"its attribute {attribute} is missing or not a whole number from {min} to {max}");
    }

    private static InvalidOperationException NotALock(string name, string table, string detail) =>
        new($"The item of lock \"{name}\" in table {table} is not a lock item written by LockTable: {detail}. Another item uses the key of this lock; give the lock another name.");

    /// <summary>Who holds a lock, as its item says.</summary>
    /// <param name="Owner">The owner id of the lease that holds it.</param>
    /// <param name="RecordVersion">The record version number of the last acquire or renewal.</param>
    /// <param name="LeaseDuration">The duration of the holder's lease.</param>
    internal sealed record Holder(string Owner, string RecordVersion, TimeSpan LeaseDuration);
}
