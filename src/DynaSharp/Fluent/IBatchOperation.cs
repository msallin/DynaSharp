using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>What sets a batch operation apart for <see cref="BatchDriver"/>, which runs them all the same way.</summary>
/// <typeparam name="TResult">What the batch returns.</typeparam>
internal interface IBatchOperation<TResult> : IActionOperation<BatchResponse>
{
    /// <summary>Finds where the request that starts at <see cref="ActionList.First"/> ends.</summary>
    /// <param name="actions">The actions.</param>
    /// <returns>The action after the last one of the request.</returns>
    public static abstract int FindEnd(ActionList actions);

    /// <summary>Writes a range of actions in the form of <c>RequestItems</c>.</summary>
    /// <param name="state">The state of the command.</param>
    /// <param name="output">Where the JSON goes.</param>
    /// <param name="first">The first action.</param>
    /// <param name="end">The action after the last one.</param>
    public static abstract void WriteRequestItems(CommandState state, PooledByteBuffer output, int first, int end);

    /// <summary>Makes the result of a batch that is done.</summary>
    /// <param name="state">The state of the command.</param>
    /// <param name="totals">What the responses said.</param>
    /// <returns>The result.</returns>
    public static abstract TResult CreateResult(CommandState state, BatchTotals totals);

    /// <summary>Makes the exception of a batch that gave up. It parses what is left, which is the one place a batch makes objects of its actions.</summary>
    /// <param name="state">The state of the command.</param>
    /// <param name="totals">What the responses said.</param>
    /// <param name="unsentFrom">The first action that is not in <see cref="ActionList.Carry"/> and was never accepted.</param>
    /// <param name="cause">The failure that ended the batch, or <see langword="null"/> if it ran out of rounds.</param>
    /// <returns>The exception.</returns>
    public static abstract Exception CreateIncomplete(CommandState state, BatchTotals totals, int unsentFrom, Exception? cause);
}
