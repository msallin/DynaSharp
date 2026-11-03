namespace DynaSharp.Testing.Fake;

/// <summary>Cuts the items a read enumerates into a page: <c>Limit</c>, the page of 1 MB by <see cref="ItemSize"/>, <c>LastEvaluatedKey</c>, then the filter, the projection and <c>Select</c>.</summary>
internal static class PageBuilder
{
    /// <summary>Builds a page.</summary>
    /// <param name="items">The items in the order of the read, after <c>ExclusiveStartKey</c>.</param>
    /// <param name="options">How to cut it.</param>
    /// <returns>The page.</returns>
    public static Page Build(IEnumerable<Item> items, PageOptions options) => throw FakeError.Unsupported("a page of a read");
}
