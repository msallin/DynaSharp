namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Shortcuts for what the tests do with a read.</summary>
internal static class Read
{
    /// <summary>Reads everything an enumeration gives.</summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="elements">The enumeration.</param>
    /// <returns>The elements.</returns>
    public static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> elements)
    {
        var all = new List<T>();
        await foreach (var element in elements)
        {
            all.Add(element);
        }

        return all;
    }

    /// <summary>Gets the sort keys of items.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The keys, joined.</returns>
    public static string SortKeys(IEnumerable<Item> items) => string.Join(",", items.Select(static item => item.GetString("sk")));
}
