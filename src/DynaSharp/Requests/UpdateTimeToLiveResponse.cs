namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="UpdateTimeToLiveRequest"/>.
/// </summary>
public sealed record UpdateTimeToLiveResponse
{
    /// <summary>
    /// Gets the setting DynamoDB accepted, which is what the request asked for. <see langword="null"/> if the response carries none.
    /// </summary>
    public TimeToLiveSpecification? TimeToLiveSpecification { get; init; }
}
