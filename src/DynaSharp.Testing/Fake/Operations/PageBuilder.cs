using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Cuts the items a read enumerates into a page, in the order of the Developer Guide: read up to <c>Limit</c> items or up to 1 MB by <see cref="ItemSize"/>, then filter, then
/// project, then count. A filter that drops every item read still ends a cut page with <c>LastEvaluatedKey</c>, so a page can be empty and not be the last.
/// </summary>
/// <remarks>
/// <para>
/// <c>LastEvaluatedKey</c> is the key of the last item read when the page stopped at <c>Limit</c> or at 1 MB, also when nothing follows, as on DynamoDB: the next page is then empty
/// and the end. It holds the key of the table, and for a read of an index the key of the index as well.
/// </para>
/// <para>
/// <c>Select</c> without a value is <c>ALL_ATTRIBUTES</c> for a table, <c>ALL_PROJECTED_ATTRIBUTES</c> for an index and <c>SPECIFIC_ATTRIBUTES</c> with a projection. A local index
/// that does not project everything fetches the item from the table for <c>ALL_ATTRIBUTES</c>, and for a projection that names an attribute only the table holds; each fetch costs
/// the units of the whole item. A global index cannot fetch, so <c>ALL_ATTRIBUTES</c> on one that does not project everything is a <c>ValidationException</c>.
/// </para>
/// </remarks>
internal static class PageBuilder
{
    /// <summary>The most a page reads.</summary>
    public const long MaxPageBytes = 1024 * 1024;

    private const string Invalid = "One or more parameter values were invalid: ";

    /// <summary>Builds a page.</summary>
    /// <param name="items">The items in the order of the read, after <c>ExclusiveStartKey</c>, as the table or the index holds them. They are read lazily, up to where the page ends.</param>
    /// <param name="options">How to cut it.</param>
    /// <returns>The page.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a <c>Select</c> that does not fit the projection or the index, before anything is read.</exception>
    public static Page Build(IEnumerable<Item> items, PageOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        var select = EffectiveSelect(options);
        var fetches = options.Index is { IsGlobal: false } index && index.Projection.ProjectionType != ProjectionType.All;
        var result = new List<Item>();
        var count = 0;
        var scanned = 0;
        long bytes = 0;
        double fetchUnits = 0;
        Item? lastEvaluatedKey = null;
        foreach (var entry in items)
        {
            scanned++;
            bytes += ItemSize.Of(entry);
            var source = fetches ? Source(entry, select, options, ref fetchUnits) : entry;
            if (options.Filter is null || options.Filter(source))
            {
                count++;
                if (select != SelectKind.Count)
                {
                    result.Add(options.Projection is null ? source : options.Projection(source));
                }
            }

            if (scanned == options.Limit || bytes >= MaxPageBytes)
            {
                lastEvaluatedKey = KeyOf(entry, options);
                break;
            }
        }

        return new Page(result, count, scanned, lastEvaluatedKey, ReadCapacity.Units(bytes, options.ConsistentRead), fetchUnits);
    }

    /// <summary>Reads the <c>ExclusiveStartKey</c> of a read and checks it against the key of the table, and of the index for a read of an index.</summary>
    /// <param name="request">The request.</param>
    /// <param name="table">The table read.</param>
    /// <param name="index">The index read, or <see langword="null"/>.</param>
    /// <returns>The key, in its stored form, or <see langword="null"/> when the request has none.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a key with an attribute too many or too few, of another type, or empty, an empty map included.</exception>
    public static Item? ReadStartKey(JsonElement request, FakeTable table, FakeIndex? index)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (RequestJson.Optional(request, "ExclusiveStartKey") is not { } value)
        {
            return null;
        }

        var key = RequestJson.ReadItem(value, "ExclusiveStartKey");
        var names = table.KeySchema.Names.Concat(index?.KeySchema.Names ?? []).Distinct(StringComparer.Ordinal).Count();
        try
        {
            if (key.Count != names)
            {
                throw FakeError.Validation("The provided key element does not match the schema");
            }

            _ = table.KeySchema.ReadKey(Subset(key, table.KeySchema));
            _ = index?.KeySchema.ReadKey(Subset(key, index.KeySchema));
        }
        catch (FakeError error)
        {
            throw FakeError.Validation("The provided starting key is invalid: " + error.Message);
        }

        return key;
    }

    private static Item Subset(Item key, KeySchema keySchema) =>
        new(keySchema.Names.Where(key.ContainsKey).Select(name => KeyValuePair.Create(name, key[name])));

    // What a read of a local index that does not project everything works on: the item of the table where the request needs more than the index holds.
    private static Item Source(Item entry, SelectKind select, PageOptions options, ref double fetchUnits)
    {
        if (select is not (SelectKind.AllAttributes or SelectKind.SpecificAttributes))
        {
            return entry;
        }

        var table = options.Table;
        var full = table.Get(table.KeySchema.KeyOf(entry))!;
        if (select == SelectKind.SpecificAttributes && options.Projection!(entry).Equals(options.Projection(full)))
        {
            return entry;
        }

        fetchUnits += ReadCapacity.Units(ItemSize.Of(full), options.ConsistentRead);
        return full;
    }

    private static SelectKind EffectiveSelect(PageOptions options)
    {
        var hasProjection = options.Projection is not null;
        var select = options.Select;
        if (select == SelectKind.Unspecified)
        {
            return hasProjection ? SelectKind.SpecificAttributes : options.Index is null ? SelectKind.AllAttributes : SelectKind.AllProjectedAttributes;
        }

        if (select == SelectKind.SpecificAttributes)
        {
            return hasProjection ? select : throw FakeError.Validation(Invalid + "Select type SPECIFIC_ATTRIBUTES needs a ProjectionExpression");
        }

        if (hasProjection)
        {
            throw FakeError.Validation(Invalid + $"Cannot specify a ProjectionExpression with Select type {WireNames.Selects.GetNameIfSet(select)}");
        }

        if (select == SelectKind.AllProjectedAttributes && options.Index is null)
        {
            throw FakeError.Validation(Invalid + "ALL_PROJECTED_ATTRIBUTES can be used only when reading an index with IndexName");
        }

        if (select == SelectKind.AllAttributes && options.Index is { IsGlobal: true } index && index.Projection.ProjectionType != ProjectionType.All)
        {
            throw FakeError.Validation(Invalid + $"Select type ALL_ATTRIBUTES is not supported for global secondary index {index.Name} because its projection type is not ALL");
        }

        return select;
    }

    private static Item KeyOf(Item entry, PageOptions options)
    {
        var key = new Item(4);
        foreach (var name in options.Table.KeySchema.Names.Concat(options.Index?.KeySchema.Names ?? []))
        {
            key[name] = entry[name];
        }

        return key;
    }
}
