using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// A link of a transaction: <see cref="TransactWriteCommand"/> or a step. It is what the verbs of <see cref="TransactWriteExtensions"/> work on, so that
/// the next action and the send read the same after every step. Nothing outside this library implements it.
/// </summary>
/// <typeparam name="TSelf">The type of the link.</typeparam>
public interface ITransactWriteChain<TSelf>
    where TSelf : ITransactWriteChain<TSelf>, allows ref struct
{
    /// <summary>Makes the next handle of a link of this type.</summary>
    /// <param name="state">The state of the command.</param>
    /// <returns>The handle.</returns>
    internal static abstract TSelf Continue(CommandState state);

    /// <summary>Uses up this handle.</summary>
    /// <returns>The state of the command.</returns>
    internal CommandState Enter();
}
