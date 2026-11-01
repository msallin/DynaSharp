namespace DynaSharp.Requests;

/// <summary>
/// How a table is billed and when it last switched to on-demand capacity.
/// </summary>
public sealed record BillingModeSummary
{
    /// <summary>Gets the billing mode of the table. <see cref="Requests.BillingMode.Unspecified"/> if DynamoDB reported none.</summary>
    public BillingMode BillingMode { get; init; }

    /// <summary>
    /// Gets when the table was last switched to <see cref="Requests.BillingMode.PayPerRequest"/>, or <see langword="null"/> if it never
    /// was. A table can be switched back and forth only once every 24 hours.
    /// </summary>
    public DateTimeOffset? LastUpdateToPayPerRequestDateTime { get; init; }
}
