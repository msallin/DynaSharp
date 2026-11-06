namespace DynaSharp.Mapping;

/// <summary>
/// Marks the property whose attribute is the time to live of the table: DynamoDB deletes an item some time after the instant it holds. <c>CreateTable()</c> of the entity turns time to live on for it, as
/// <c>.TimeToLive(handle)</c> on the chain does, and so does <c>FakeDynamoDb.CreateTable&lt;T&gt;</c> of <c>DynaSharp.Testing</c>.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB reads a number of seconds since 1970-01-01T00:00:00Z and ignores an item whose attribute holds anything else, a string among them, so the property has to be stored as one: a <see cref="DateTimeOffset"/> with
/// <c>[DynamoDbConverter(typeof(EpochSecondsConverter))]</c>, an integer that holds the seconds, or a converter of the application that is an <see cref="Serialization.IAddableConverter"/>, which says that it writes a
/// number. Anything else is the error <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA036</see>, as is a second such property on one entity and one that is not stored, because a table has time to live on one attribute. Nullable is fine: an item
/// without the attribute never expires.
/// </para>
/// <para>
/// A table has one attribute of time to live, so <c>CreateTable().Including&lt;TOther&gt;()</c> throws <see cref="ArgumentException"/> for two entities that name different ones, as it does for other disagreements, and
/// an entity without one takes that of the others. <c>.TimeToLive(...)</c> on the chain replaces what the entities declare. Turning time to live on needs an active table, so the create waits for it, five minutes at most
/// unless <c>WaitUntilActive(timeout)</c> says otherwise, and takes <c>dynamodb:DescribeTimeToLive</c> and <c>dynamodb:UpdateTimeToLive</c>, also on a table that <c>IfNotExists()</c> finds: see
/// <see cref="CreateTableCommand.TimeToLive(string)"/>.
/// </para>
/// <para>
/// DynamoDB deletes within days of the expiry, not at it, and a read returns an expired item until then, so a read that must not see one filters on the attribute. It is on a property of an entity, not of a document.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class TimeToLiveAttribute : Attribute
{
}
