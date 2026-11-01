using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>What a page does with one item of <c>Items</c>. It is a static member of a struct, so that the page loop stays free of delegates and each kind of read gets its own code.</summary>
/// <typeparam name="T">What an item gives.</typeparam>
internal interface IItemReader<T>
{
    /// <summary>Reads one item.</summary>
    /// <param name="reader">The reader, positioned after the opening brace of the item. It ends after the closing one.</param>
    /// <param name="state">The state of the read.</param>
    /// <returns>What the item gives.</returns>
    public static abstract T Read(ref DynamoDbReader reader, CommandState state);
}
