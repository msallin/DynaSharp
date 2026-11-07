using DynaSharp.Http;

namespace DynaSharp.Operations;

/// <summary>Runs the operations of the request API on a client, as a <see cref="RequestCall{TOperation, TRequest, TResponse}"/> of the pipeline.</summary>
internal static class OperationCalls
{
    /// <summary>
    /// Runs an operation of a public client. Not asynchronous itself: a null request and a disposed client throw at the call, not in
    /// the returned task.
    /// </summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TRequest">The request type of the operation.</typeparam>
    /// <typeparam name="TResponse">The response type of the operation.</typeparam>
    /// <param name="core">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    public static Task<TResponse> ExecuteAsync<TOperation, TRequest, TResponse>(this ClientCore core, TRequest request, CancellationToken cancellationToken)
        where TOperation : IOperation<TRequest, TResponse>
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(request);
        return core.SendAsync<RequestCall<TOperation, TRequest, TResponse>, TResponse>(new RequestCall<TOperation, TRequest, TResponse>(request), cancellationToken).AsTask();
    }
}
