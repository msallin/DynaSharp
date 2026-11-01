namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="DescribeTimeToLiveRequest"/>.
/// </summary>
public sealed record DescribeTimeToLiveResponse
{
    /// <summary>
    /// Gets the Time to Live state of the table. DynamoDB reports one for every table, also for one that never had Time to Live
    /// (<see cref="TimeToLiveStatus.Disabled"/>); <see langword="null"/> only if the response carries none.
    /// </summary>
    public TimeToLiveDescription? TimeToLiveDescription { get; init; }
}
