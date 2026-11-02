using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// An item read for one attribute: <c>{"count":{"N":"7"}}</c> gives 7, and <c>{"stats":{"M":{"count":{"N":"7"}}}}</c> does for a nested path. No entity is made, so nothing but the attribute can fail. An attribute
/// that is missing, or that the converter counts as missing, gives what the converter says it means, and a throw when it says nothing, because a counter that silently reads 0 is the bug this exists to prevent.
/// </summary>
/// <remarks>
/// An item of another type fails as a read of the entity does, by the same call. A filter has kept such items out of a <c>Scan</c> and of nearly every <c>Query</c>, so the read looks only where
/// <see cref="ReadOptions.ChecksDiscriminator"/> says, and that takes a second walk of the item: JSON has no order of attributes, the reader goes one way, and a reader is a struct, so the copy is the walk.
/// </remarks>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal readonly struct SelectedItem<TValue, TConverter> : IItemReader<TValue>
    where TConverter : IDynamoDbConverter<TValue>
{
    public static TValue Read(ref DynamoDbReader reader, CommandState state)
    {
        var path = state.ReturnPath!;
        var entity = state.Table.ReadTypeName;
        if (state.Read.ChecksDiscriminator)
        {
            CheckDiscriminator(reader, state.Table, entity);
        }

        if (AttributePath<TValue, TConverter>.TryRead(ref reader, path, path, entity, out var value))
        {
            return value!;
        }

        return TConverter.TryReadMissing(out var missing) ? missing : throw DynamoDbMappingException.Missing(entity, path);
    }

    // An item without the attribute passes, as it does in generated code: a read checks a discriminator that is there.
    private static void CheckDiscriminator(DynamoDbReader item, TableModel table, string entity)
    {
        var attribute = table.DiscriminatorAttributeName!;
        while (item.TryReadAttributeName())
        {
            if (item.Json.ValueTextEquals(attribute))
            {
                Check(ref item, table, entity, attribute);
                return;
            }

            item.SkipAttributeValue();
        }
    }

    private static void Check(ref DynamoDbReader item, TableModel table, string entity, string attribute)
    {
        try
        {
            ConverterChecks.ExpectDiscriminator(ref item, table.DiscriminatorUtf8, entity, attribute);
        }
        catch (Exception exception) when (DynamoDbMappingException.IsConversionFailure(exception))
        {
            throw new DynamoDbMappingException(entity, attribute, exception);
        }
    }
}
