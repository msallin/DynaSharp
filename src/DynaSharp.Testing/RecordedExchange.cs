namespace DynaSharp.Testing;

/// <summary>
/// One request that a <see cref="RecordingHandler"/> sent and what came back: a response, or the exception that took its place.
/// </summary>
public sealed class RecordedExchange
{
    internal RecordedExchange(RecordedRequest request, RecordedResponse? response, Exception? error)
    {
        Request = request;
        Response = response;
        Error = error;
    }

    /// <summary>Gets the request as it went over the wire, signature headers included.</summary>
    public RecordedRequest Request { get; }

    /// <summary>
    /// Gets the response, or <see langword="null"/> if the request failed before a response was read completely: see
    /// <see cref="Error"/>. An error response from DynamoDB is a response like any other.
    /// </summary>
    public RecordedResponse? Response { get; }

    /// <summary>
    /// Gets the exception the inner handler threw instead of returning a response, for example an <see cref="HttpRequestException"/>
    /// for a refused connection or an <see cref="OperationCanceledException"/> for a timeout; <see langword="null"/> if there is a
    /// <see cref="Response"/>.
    /// </summary>
    public Exception? Error { get; }
}
