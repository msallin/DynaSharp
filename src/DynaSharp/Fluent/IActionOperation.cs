using DynaSharp.Http;

namespace DynaSharp.Fluent;

/// <summary>
/// A DynamoDB operation of several actions as a command runs it: a batch or a transaction. They differ from the operations of one item, see
/// <see cref="ICommandOperation{TResponse}"/>, in that a request can name several tables, and a batch sends several requests over one state.
/// </summary>
/// <typeparam name="TResponse">What one request returns.</typeparam>
internal interface IActionOperation<TResponse>
{
    /// <summary>Gets the operation name as DynamoDB knows it.</summary>
    public static abstract string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the call owns the state, so that the pipeline gives it back after the last attempt. A batch does not: its
    /// next request needs the state, and <see cref="BatchDriver"/> gives it back.
    /// </summary>
    public static abstract bool EndsCommand { get; }

    /// <summary>Provides the body of the request, once per call.</summary>
    /// <param name="state">The state of the command.</param>
    /// <param name="buffers">The buffers of the call, for a body that is not in the state.</param>
    /// <returns>The body.</returns>
    public static abstract ArraySegment<byte> WriteBody(CommandState state, CallBuffers buffers);

    /// <summary>Reads the body of a successful response.</summary>
    /// <param name="utf8Json">The body, which is valid during the call.</param>
    /// <param name="state">The state of the command.</param>
    /// <returns>The response.</returns>
    public static abstract TResponse ReadResponse(ReadOnlySpan<byte> utf8Json, CommandState state);

    /// <summary>Gets the capacity a response reports, for the span and the metric.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The capacity.</returns>
    public static abstract IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(TResponse response);
}
