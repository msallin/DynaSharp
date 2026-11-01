namespace DynaSharp.Mapping;

/// <summary>
/// Marks the property that holds the version of an item, for optimistic locking. It applies to a single integer property of an entity, which cannot be
/// nullable, has no custom converter, and is part of no key.
/// </summary>
/// <remarks>
/// <para>
/// The property holds the version the caller last read, and 0 means a new item. Every write of the typed API enforces it, and the condition shows in
/// <c>ToRequestJson()</c>. A <c>Put</c> takes the version from the entity: it adds <c>attribute_not_exists</c> for 0 and <c>version = :old</c> for any other, and stores the next
/// version, which <see cref="WriteResult{T}.Version"/> returns, without changing the entity; with <c>.IgnoreVersion()</c> it stores the entity as it is, for an import or a migration. A
/// <c>Delete</c> of an entity takes the version from it too. A key does not carry a version, so <c>Update</c> and a <c>Delete</c> by key need <c>.IfVersion(n)</c>, or <c>.IgnoreVersion()</c> to
/// write without the check, and sending without either throws <see cref="InvalidOperationException"/>. A <see cref="BatchWriteCommand{T}"/> cannot carry a condition, so it takes
/// <c>.IgnoreVersion()</c> only.
/// </para>
/// <para>
/// A write that loses the race fails with <see cref="ConditionalCheckFailedException"/>, or in a transaction with a <see cref="TransactionCanceledException"/>. Nothing is
/// read or retried by itself. The table without a schema and the requests of <c>DynaSharp.Requests</c> know no entity, so they do not enforce a version.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class VersionAttribute : Attribute
{
}
