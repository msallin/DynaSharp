using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>Names one get of a <see cref="TransactGetCommand"/>, with the type it reads, to read it from the <see cref="TransactGetResult"/>.</summary>
/// <typeparam name="T">How the item is read.</typeparam>
public readonly struct TransactSlot<T>
    where T : IDynamoDbDocument<T>
{
    internal TransactSlot(int index)
    {
        Position = index + 1;
    }

    /// <summary>Gets the index of the get.</summary>
    /// <exception cref="ArgumentException">This is a default slot, which no transaction handed out.</exception>
    internal int Index => Position > 0 ? Position - 1 : throw new ArgumentException("This slot is a default value. A slot comes from a get of a transaction, through its out parameter.", "slot");

    // One more than the index, so that the default value is told from the first get.
    private int Position { get; }
}
