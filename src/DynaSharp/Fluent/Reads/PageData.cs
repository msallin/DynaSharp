namespace DynaSharp.Fluent;

/// <summary>
/// What one response of a paged read gives its caller. The key of the next page goes to <see cref="CommandState.PageKey"/> and the capacity to
/// <see cref="CommandState.ResponseCapacity"/>, so a read that follows pages pays for neither.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <param name="Items">The items of the page.</param>
/// <param name="Count">The number of items after the filter.</param>
/// <param name="ScannedCount">The number of items read before the filter.</param>
/// <param name="KeyOffset">Where <c>LastEvaluatedKey</c> starts in the response.</param>
/// <param name="KeyLength">The length of <c>LastEvaluatedKey</c>, or zero at the end of the read.</param>
internal readonly record struct PageData<T>(T[] Items, int Count, int ScannedCount, int KeyOffset, int KeyLength)
{
    /// <summary>Gets a value indicating whether there is a next page.</summary>
    public bool HasMore => KeyLength > 0;
}
