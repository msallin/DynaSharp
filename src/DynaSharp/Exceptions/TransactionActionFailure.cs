namespace DynaSharp;

/// <summary>An action that made a transaction fail. It holds neither keys nor values, so it is safe to log.</summary>
/// <param name="Index">The position of the action in the transaction, starting at 0.</param>
/// <param name="Reason">Why it failed.</param>
public readonly record struct TransactionActionFailure(int Index, TransactionFailure Reason);
