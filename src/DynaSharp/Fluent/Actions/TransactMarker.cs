namespace DynaSharp;

/// <summary>
/// Names one action of a transaction, to find out what became of it when the transaction is cancelled:
/// <see cref="TransactionCanceledException.ReasonFor"/> and <see cref="TransactionCanceledException.ItemFor"/>. The verbs of
/// <see cref="TransactWriteCommand"/> hand it out with <c>out</c>.
/// </summary>
/// <remarks>
/// A catch filter can only see a marker that is declared outside the <c>try</c>, and the compiler then wants it assigned before the <c>try</c>, because the
/// action may never have been added: <c>TransactMarker taken = default;</c>. A default marker names no action.
/// </remarks>
public readonly struct TransactMarker
{
    internal TransactMarker(int index)
    {
        Position = index + 1;
    }

    /// <summary>Gets the index of the action.</summary>
    /// <exception cref="ArgumentException">This is a default marker, which no transaction handed out.</exception>
    internal int Index => Position > 0 ? Position - 1 : throw new ArgumentException("This marker is a default value. A marker comes from an action of a transaction, through its out parameter.", "marker");

    // One more than the index, so that the default value is told from the first action.
    private int Position { get; }
}
