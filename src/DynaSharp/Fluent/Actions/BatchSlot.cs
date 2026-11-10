using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>Names one get of a <see cref="BatchGetCommand{T}"/> that has a slot, with the type it reads, to read its item from the <see cref="BatchGetResult{T}"/>.</summary>
/// <typeparam name="T">How the item is read.</typeparam>
public readonly struct BatchSlot<T>
    where T : IDynamoDbDocument<T>
{
    internal BatchSlot(int index)
    {
        Position = index + 1;
    }

    /// <summary>Gets the index of the get among the gets of the batch that have a slot.</summary>
    /// <exception cref="ArgumentException">This is a default slot, which no batch handed out.</exception>
    internal int Index => Position > 0 ? Position - 1 : throw new ArgumentException("This slot is a default value. A slot comes from a get of a batch, through its out parameter.", "slot");

    // One more than the index, so that the default value is told from the first get.
    private int Position { get; }
}
