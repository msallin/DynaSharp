namespace DynaSharp.Requests;

/// <summary>
/// A key-value label attached to a table, for cost allocation and access control.
/// </summary>
public sealed record TableTag
{
    /// <summary>Gets the key, 1 to 128 characters. The prefix <c>aws:</c> is reserved.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the value, up to 256 characters; may be empty.</summary>
    public required string Value { get; init; }
}
