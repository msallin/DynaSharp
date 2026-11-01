namespace DynaSharp.Requests;

/// <summary>
/// The Time to Live state of a table as DynamoDB reports it.
/// </summary>
public sealed record TimeToLiveDescription
{
    /// <summary>
    /// Gets the state. A change takes up to an hour to take effect, so right after <c>UpdateTimeToLive</c> it is
    /// <see cref="Requests.TimeToLiveStatus.Enabling"/> or <see cref="Requests.TimeToLiveStatus.Disabling"/>.
    /// <see cref="Requests.TimeToLiveStatus.Unknown"/> if DynamoDB reported none or one this library does not know.
    /// </summary>
    public TimeToLiveStatus TimeToLiveStatus { get; init; }

    /// <summary>
    /// Gets the name of the attribute that holds the expiry time. <see langword="null"/> when DynamoDB reports none, as for a table
    /// that never had Time to Live.
    /// </summary>
    public string? AttributeName { get; init; }
}
