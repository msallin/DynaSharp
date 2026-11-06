using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// A key written by hand that holds the stored values as they are, without the template of the entity: what a key of an entity written by hand can write, and the one way left to
/// point a table of an entity at an item its own keys cannot name, such as an item of another type.
/// </summary>
/// <typeparam name="T">The entity.</typeparam>
/// <param name="partitionKey">The partition key as it is stored.</param>
/// <param name="sortKey">The sort key as it is stored, or the default value to leave it out, which DynamoDB rejects on a table with one.</param>
internal readonly struct StoredKey<T>(AttributeValue partitionKey, AttributeValue sortKey = default) : IKey<T>
    where T : IDynamoDbEntity<T>
{
    public void WriteKey(DynamoDbWriter writer)
    {
        writer.WriteAttributeName(T.Info.PartitionKeyName);
        AttributeValueConverter.Write(writer, partitionKey);
        if (T.Info.SortKeyName is { } sortKeyName && sortKey.Type != AttributeType.None)
        {
            writer.WriteAttributeName(sortKeyName);
            AttributeValueConverter.Write(writer, sortKey);
        }
    }
}
