namespace DynaSharp.Mapping;

/// <summary>
/// Marks the property that holds the version of an item, for optimistic locking. It applies to a single integer property of an entity, which cannot be
/// nullable, has no custom converter, and is part of no key.
/// </summary>
/// <remarks>
/// <para>
/// The property holds the version the caller last read, and 0 means a new item. Every write of the typed API enforces it, and the condition shows in
/// <c>ToRequestJson()</c>. A <c>Put</c> takes the version from the entity: it adds <c>attribute_not_exists</c> for 0, see below, and <c>version = :old</c> for any other, and stores the next
/// version, which <see cref="WriteResult{T}.Version"/> returns, without changing the entity; with <c>.IgnoreVersion()</c> it stores the entity as it is, for an import or a migration. A
/// <c>Delete</c> of an entity takes the version from it too. A key does not carry a version, so <c>Update</c> and a <c>Delete</c> by key need <c>.IfVersion(n)</c>, or <c>.IgnoreVersion()</c> to
/// write without the check, and sending without either throws <see cref="InvalidOperationException"/>. A <see cref="BatchWriteCommand{T}"/> cannot carry a condition, so it takes
/// <c>.IgnoreVersion()</c> only.
/// </para>
/// <para>
/// Version 0 checks the key and never looks at the version attribute: every 0, on a <c>Put</c>, in <c>.IfVersion(0)</c>, on a <c>Delete</c> of an entity and on a step of a transaction, renders
/// <c>attribute_not_exists</c> on the partition key. So a first write fails on any item under its key, also on one without a version attribute, such as one written by other code: that item reads as
/// version 0, and a <c>Put</c> at 0 then fails with <see cref="ConditionalCheckFailedException"/>, because 0 says that no item has the key.
/// <c>.IfNotExists()</c> at version 0 is the same condition and renders once; above version 0 it throws, because no item can lack its key and hold a version. <c>.IfExists()</c> at version 0 throws for the same reason the
/// other way around, unless <c>.IgnoreVersion()</c> lifts it as it lifts that rule.
/// </para>
/// <para>
/// A table that keeps such items, because other code wrote them or they are older than the version, is what <c>[Version(Missing = VersionMissing.Allow)]</c> is for, and that is the first answer for it. It takes over an
/// item that holds 0 as well, which is what the first <c>SaveAsync</c> of the AWS SDK stores, because that reads as 0 too. Every 0 above then renders <c>(attribute_not_exists(version) OR version = 0)</c>, so 0 means an item
/// without a version, one that holds 0, or none: the first write replaces such an item and stores version 1, and the writes after it are ordinary ones.
/// A read gives 0 for an item without a version, also for a <c>required</c> version, and so does <c>Select</c> of the handle, where it would throw otherwise. No item at all is something else: <c>Select</c> has
/// <see cref="SelectResult{TValue}.Found"/> false, and <see cref="SelectResult{TValue}.Value"/> throws. The price is that a write at 0 no longer proves that the key was free. Two
/// clauses say what they mean instead: <c>.IfNotExists()</c> at version 0 keeps <c>attribute_not_exists</c> on the partition key alone, because that is what was asked for, and <c>.IfExists()</c> at version 0 no longer
/// throws, because such an item can exist, and renders <c>attribute_exists</c> on the partition key beside that same condition on the version attribute. A version above 0 is the same either way.
/// </para>
/// <para>
/// To take over a single item without changing the attribute, put it once with a version above 0 and without the check: <c>orders.Put(order with { Revision = 1 }).IgnoreVersion()</c>, which stores version 1, and the writes
/// after it are ordinary ones. Under the default, at 0 the item would read as 0 and fail the next write the same way.
/// </para>
/// <para>
/// A write that loses the race fails with <see cref="ConditionalCheckFailedException"/>, or in a transaction with a <see cref="TransactionCanceledException"/>. Nothing is
/// read or retried by itself. The table without a schema and the requests of <c>DynaSharp.Requests</c> know no entity, so they do not enforce a version.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class VersionAttribute : Attribute
{
    /// <summary>Gets or sets what an item without the version attribute means, <see cref="VersionMissing.Reject"/> by default, and <see cref="VersionMissing.Allow"/> for a table that keeps such items. See the remarks of this class.</summary>
    public VersionMissing Missing { get; set; }
}
