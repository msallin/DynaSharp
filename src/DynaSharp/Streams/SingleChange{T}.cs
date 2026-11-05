using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>The one type of a reading of <see cref="Change{T}"/>.</summary>
/// <typeparam name="T">The entity.</typeparam>
internal readonly struct SingleChange<T> : IChangeTypes<Change<T>>
    where T : IDynamoDbEntity<T>
{
    /// <inheritdoc/>
    public static int FindType(Item? image) => ChangeTyping.FindType<T>(image);

    /// <inheritdoc/>
    public static Change<T> Create(int index, ChangeRecord record, bool before, bool after) => ChangeTyping.Create<T>(record, before, after);
}
