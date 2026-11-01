using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Canned pages of a read, whose items are <c>{pk: "a", sk: ...}</c>, and what the tests of paged reads need to look at a read from outside.</summary>
internal static class PageJson
{
    /// <summary>The key that a page of <see cref="Items"/> with a next key ends in.</summary>
    /// <param name="sortKey">The sort key.</param>
    /// <returns>The key as JSON.</returns>
    public static string Key(string sortKey) => "{\"pk\":{\"S\":\"a\"},\"sk\":{\"S\":\"" + sortKey + "\"}}";

    /// <summary>A last page.</summary>
    /// <param name="sortKeys">The sort keys of the items.</param>
    /// <returns>The response.</returns>
    public static StubResponse Last(params string[] sortKeys) => StubResponse.Json(Items(null, sortKeys));

    /// <summary>A page that has a next one, whose key is the last item.</summary>
    /// <param name="sortKeys">The sort keys of the items.</param>
    /// <returns>The response.</returns>
    public static StubResponse More(params string[] sortKeys) => StubResponse.Json(Items(Key(sortKeys[^1]), sortKeys));

    /// <summary>A page that a filter left empty.</summary>
    /// <param name="sortKey">The sort key it stopped at.</param>
    /// <returns>The response.</returns>
    public static StubResponse FilteredEmpty(string sortKey) => StubResponse.Json("{\"Items\":[],\"Count\":0,\"ScannedCount\":7,\"LastEvaluatedKey\":" + Key(sortKey) + "}");

    /// <summary>Writes the body of a page.</summary>
    /// <param name="next">The key of the next page as JSON, if there is one.</param>
    /// <param name="sortKeys">The sort keys of the items.</param>
    /// <returns>The body.</returns>
    public static string Items(string? next, params string[] sortKeys)
    {
        var body = new StringBuilder("{\"Items\":[").AppendJoin(',', sortKeys.Select(Key)).Append("],\"Count\":");
        body.Append(sortKeys.Length.ToString(CultureInfo.InvariantCulture)).Append(",\"ScannedCount\":").Append(sortKeys.Length.ToString(CultureInfo.InvariantCulture));
        if (next is not null)
        {
            body.Append(",\"LastEvaluatedKey\":").Append(next);
        }

        return body.Append('}').ToString();
    }

    /// <summary>Reads everything an enumeration gives.</summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="elements">The enumeration.</param>
    /// <returns>The elements.</returns>
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> elements)
    {
        var all = new List<T>();
        await foreach (var element in elements)
        {
            all.Add(element);
        }

        return all;
    }

    /// <summary>Reads everything an enumeration with a token gives.</summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="elements">The enumeration.</param>
    /// <returns>The elements.</returns>
    public static async Task<List<T>> ToListAsync<T>(this ConfiguredCancelableAsyncEnumerable<T> elements)
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
    public static string SortKeys(this IEnumerable<Item> items) => string.Join(",", items.Select(static item => item.GetString("sk")));

    /// <summary>Raises a number to a value, for a test that keeps the most of what ran at once.</summary>
    /// <param name="most">The number.</param>
    /// <param name="value">The value.</param>
    public static void InterlockedMax(ref int most, int value)
    {
        var seen = Volatile.Read(ref most);
        while (value > seen)
        {
            var before = Interlocked.CompareExchange(ref most, value, seen);
            if (before == seen)
            {
                return;
            }

            seen = before;
        }
    }

    /// <summary>A request that hangs until the read is stopped, and reports that it was. The token cancels it, as it would a call on the wire.</summary>
    /// <param name="stopped">What reports it.</param>
    /// <param name="token">The token of the request.</param>
    /// <returns>A task that ends cancelled.</returns>
    public static async Task WaitUntilCancelledAsync(Action stopped, CancellationToken token)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, token);
        }
        finally
        {
            stopped();
        }
    }

    /// <summary>A read of a table with what shows that its state went back: the state, and the version it has after one release and no other.</summary>
    /// <typeparam name="TRead">What the read gives.</typeparam>
    /// <param name="Read">The read.</param>
    /// <param name="State">The state of the read.</param>
    /// <param name="Released">The version of the state after exactly one release.</param>
    internal readonly record struct Tracked<TRead>(TRead Read, CommandState State, int Released)
    {
        /// <summary>Gets a value indicating whether the state went back once. A second release throws, so the number cannot move on by a mistake of the read.</summary>
        public bool ReleasedOnce => State.Version == Released;
    }
}
