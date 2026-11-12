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
/// <c>LastEvaluatedKey</c> is the key of the last item read when the page stopped at <c>Limit</c> or at 1 MB, also when nothing follows: the next page is then empty and the end.
/// It holds the key of the table, and for a read of an index the key of the index as well. A <c>Query</c> whose key condition has a part for the sort key, whatever its
/// operator, direction or filter, leaves it out when <c>Limit</c> ends the page at the last item the condition matches, and so does a <c>Query</c> of a table without a sort key,
/// as real DynamoDB does (<c>FidelityTests</c> question 13, runs 43286714921 and 43288107804). With a condition on the partition key alone of a table with a sort key, DynamoDB
/// answers the same to each query of one table, but tables of one shape answer differently, so no request decides it: the fake sends it there, the answer of most tables.
/// </para>
/// <para>
/// <c>Select</c> without a value is <c>ALL_ATTRIBUTES</c> for a table, <c>ALL_PROJECTED_ATTRIBUTES</c> for an index and <c>SPECIFIC_ATTRIBUTES</c> with a projection. A local index
/// that does not project everything fetches the item from the table for <c>ALL_ATTRIBUTES</c>, and for a projection that names an attribute only the table holds; each fetch costs
/// the units of the whole item. A global index cannot fetch, so <c>ALL_ATTRIBUTES</c> on one that does not project everything is a <c>ValidationException</c>, and so is a projection
/// that names an attribute it does not project.
/// </para>
/// <para>
/// The filter of a <c>Query</c> of a secondary index, global or local, may read only what the index projects, else the query is a <c>ValidationException</c>, as on DynamoDB Local. The
/// filter of a <c>Scan</c> of a global index is evaluated on the entry of the index, so an attribute it does not project is missing there, as the Developer Guide says a global index
/// holds only what it projects (<c>FidelityTests</c> asks real DynamoDB). The filter of a <c>Scan</c> of a local index is evaluated on the item of the table where it reads an attribute
/// the index does not project, as on DynamoDB Local, and costs nothing more.
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
    /// <exception cref="FakeError">
    /// A <c>ValidationException</c> for a <c>Select</c> that does not fit the projection or the index, for a filter of a query or a projection of a global index that reads what the
    /// index does not project, before anything is read.
    /// </exception>
    public static Page Build(IEnumerable<Item> items, PageOptions options)
    {
        var unprojected = options.Index is { } read ? options.FilterAttributes.Where(attribute => !read.Projects(attribute)).Order(StringComparer.Ordinal).ToList() : [];
        if (unprojected.Count > 0 && options.IsQuery)
        {
            throw FakeError.Validation(Invalid + $"Secondary index {options.Index!.Name} does not project one or more filter attributes: [{string.Join(", ", unprojected)}]");
        }

        var select = EffectiveSelect(options);
        if (select == SelectKind.SpecificAttributes
            && options.Index is { IsGlobal: true } global
            && options.ProjectionAttributes.Where(attribute => !global.Projects(attribute)).ToList() is { Count: > 0 } missing)
        {
            throw FakeError.Validation(Invalid + $"Global secondary index {global.Name} does not project [{string.Join(", ", missing)}]");
        }

        var filterReadsTable = unprojected.Count > 0 && !options.Index!.IsGlobal;
        var fetches = options.Index is { IsGlobal: false } index && index.Projection.ProjectionType != ProjectionType.All;
        var result = new List<Item>();
        var count = 0;
        var scanned = 0;
        long bytes = 0;
        double fetchUnits = 0;
        Item? lastEvaluatedKey = null;
        using var pending = items.GetEnumerator();
        while (pending.MoveNext())
        {
            var entry = pending.Current;
            scanned++;
            bytes += ItemSize.Of(entry);
            var source = fetches ? Source(entry, select, options, ref fetchUnits) : entry;
            if (options.Filter is null || options.Filter(filterReadsTable ? options.Table.Get(options.Table.KeySchema.KeyOf(entry))! : source))
            {
                count++;
                if (select != SelectKind.Count)
                {
                    result.Add(options.Projection is null ? source : options.Projection(source));
                }
            }

            if (scanned == options.Limit || bytes >= MaxPageBytes)
            {
                var atTheEndOfTheRange = scanned == options.Limit && options.KnowsItsEnd && !pending.MoveNext();
                lastEvaluatedKey = atTheEndOfTheRange ? null : KeyOf(entry, options);
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
            return hasProjection ? select : throw FakeError.Validation("Must specify the AttributesToGet or ProjectionExpression when choosing to get SPECIFIC_ATTRIBUTES");
        }

        if (hasProjection)
        {
            throw FakeError.Validation($"Cannot specify the ProjectionExpression when choosing to get {(select == SelectKind.Count ? "only the Count" : WireNames.Selects.GetNameIfSet(select))}");
        }

        if (select == SelectKind.AllProjectedAttributes && options.Index is null)
        {
            // DynamoDB Local says "Querying" for a Scan too (Vectors/reads.json).
            throw FakeError.Validation("ALL_PROJECTED_ATTRIBUTES can be used only when Querying using an IndexName");
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
