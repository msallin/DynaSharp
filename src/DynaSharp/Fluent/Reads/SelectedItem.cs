using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>An item read for one attribute, see <see cref="SelectedItem"/>.</summary>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal readonly struct SelectedItem<TValue, TConverter> : IItemReader<TValue>
    where TConverter : IDynamoDbConverter<TValue>
{
    public static TValue Read(ref DynamoDbReader reader, CommandState state)
    {
        SelectedItem.CheckType(reader, state);
        return SelectedItem.Read<TValue, TConverter>(ref reader, state, 0);
    }
}

/// <summary>An item read for two attributes, see <see cref="SelectedItem"/>.</summary>
/// <typeparam name="T1">The type of the first attribute.</typeparam>
/// <typeparam name="TConverter1">The converter of the first attribute.</typeparam>
/// <typeparam name="T2">The type of the second attribute.</typeparam>
/// <typeparam name="TConverter2">The converter of the second attribute.</typeparam>
internal readonly struct SelectedItem<T1, TConverter1, T2, TConverter2> : IItemReader<(T1, T2)>
    where TConverter1 : IDynamoDbConverter<T1>
    where TConverter2 : IDynamoDbConverter<T2>
{
    public static (T1, T2) Read(ref DynamoDbReader reader, CommandState state)
    {
        SelectedItem.CheckType(reader, state);
        var first = reader;
        return (SelectedItem.Read<T1, TConverter1>(ref first, state, 0), SelectedItem.Read<T2, TConverter2>(ref reader, state, 1));
    }
}

/// <summary>An item read for three attributes, see <see cref="SelectedItem"/>.</summary>
/// <typeparam name="T1">The type of the first attribute.</typeparam>
/// <typeparam name="TConverter1">The converter of the first attribute.</typeparam>
/// <typeparam name="T2">The type of the second attribute.</typeparam>
/// <typeparam name="TConverter2">The converter of the second attribute.</typeparam>
/// <typeparam name="T3">The type of the third attribute.</typeparam>
/// <typeparam name="TConverter3">The converter of the third attribute.</typeparam>
internal readonly struct SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3> : IItemReader<(T1, T2, T3)>
    where TConverter1 : IDynamoDbConverter<T1>
    where TConverter2 : IDynamoDbConverter<T2>
    where TConverter3 : IDynamoDbConverter<T3>
{
    public static (T1, T2, T3) Read(ref DynamoDbReader reader, CommandState state)
    {
        SelectedItem.CheckType(reader, state);
        var first = reader;
        var second = reader;
        return (SelectedItem.Read<T1, TConverter1>(ref first, state, 0), SelectedItem.Read<T2, TConverter2>(ref second, state, 1), SelectedItem.Read<T3, TConverter3>(ref reader, state, 2));
    }
}

/// <summary>An item read for four attributes, see <see cref="SelectedItem"/>.</summary>
/// <typeparam name="T1">The type of the first attribute.</typeparam>
/// <typeparam name="TConverter1">The converter of the first attribute.</typeparam>
/// <typeparam name="T2">The type of the second attribute.</typeparam>
/// <typeparam name="TConverter2">The converter of the second attribute.</typeparam>
/// <typeparam name="T3">The type of the third attribute.</typeparam>
/// <typeparam name="TConverter3">The converter of the third attribute.</typeparam>
/// <typeparam name="T4">The type of the fourth attribute.</typeparam>
/// <typeparam name="TConverter4">The converter of the fourth attribute.</typeparam>
internal readonly struct SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3, T4, TConverter4> : IItemReader<(T1, T2, T3, T4)>
    where TConverter1 : IDynamoDbConverter<T1>
    where TConverter2 : IDynamoDbConverter<T2>
    where TConverter3 : IDynamoDbConverter<T3>
    where TConverter4 : IDynamoDbConverter<T4>
{
    public static (T1, T2, T3, T4) Read(ref DynamoDbReader reader, CommandState state)
    {
        SelectedItem.CheckType(reader, state);
        var first = reader;
        var second = reader;
        var third = reader;
        return (
            SelectedItem.Read<T1, TConverter1>(ref first, state, 0),
            SelectedItem.Read<T2, TConverter2>(ref second, state, 1),
            SelectedItem.Read<T3, TConverter3>(ref third, state, 2),
            SelectedItem.Read<T4, TConverter4>(ref reader, state, 3));
    }
}

/// <summary>
/// An item read for some of its attributes: <c>{"count":{"N":"7"}}</c> gives 7, and <c>{"stats":{"M":{"count":{"N":"7"}}}}</c> does for a nested path. No entity is made, so nothing but the attributes can fail. An attribute
/// that is missing, or that the converter counts as missing, gives what the converter says it means, and a throw when it says nothing, because a counter that silently reads 0 is the bug this exists to prevent.
/// </summary>
/// <remarks>
/// <para>
/// An item of another type fails as a read of the entity does, by the same call. A filter has kept such items out of a <c>Scan</c> and of nearly every <c>Query</c>, so the read looks only where
/// <see cref="ReadOptions.ChecksDiscriminator"/> says, and that takes a walk of the item of its own: JSON has no order of attributes, the reader goes one way, and a reader is a struct, so the copy is the walk.
/// </para>
/// <para>For the same reason a read of several attributes walks a copy of the item for each of them but the last, which takes the reader to the end of the item.</para>
/// </remarks>
internal static class SelectedItem
{
    /// <summary>Checks the type of an item before its attributes are read, where <see cref="ReadOptions.ChecksDiscriminator"/> asks for it.</summary>
    /// <param name="item">A copy of the reader, positioned after the opening brace of the item.</param>
    /// <param name="state">The state of the read.</param>
    /// <exception cref="DynamoDbMappingException">The item is of another type.</exception>
    public static void CheckType(DynamoDbReader item, CommandState state)
    {
        if (state.Read.ChecksDiscriminator)
        {
            CheckDiscriminator(item, state.Table, state.Table.ReadTypeName);
        }
    }

    /// <summary>Reads one of the selected attributes of an item, up to and including the closing brace of the item.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="reader">The reader, positioned after the opening brace of the item.</param>
    /// <param name="state">The state of the read.</param>
    /// <param name="position">Which of the selected attributes, 0 for the first.</param>
    /// <returns>The value.</returns>
    /// <exception cref="DynamoDbMappingException">The attribute is missing and the converter has no answer for that, or its value cannot be read.</exception>
    public static TValue Read<TValue, TConverter>(ref DynamoDbReader reader, CommandState state, int position)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var path = state.SelectedPath(position);
        var entity = state.Table.ReadTypeName;
        if (AttributePath<TValue, TConverter>.TryRead(ref reader, path, path, entity, out var value))
        {
            return value!;
        }

        if (TConverter.TryReadMissing(out var missing))
        {
            return missing;
        }

        // A version that allows an item without one has a handle that answers above, so this is a version that rejects it.
        throw path == state.Table.Writes?.VersionAttributeName ? DynamoDbMappingException.MissingVersion(entity, path) : DynamoDbMappingException.Missing(entity, path);
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
