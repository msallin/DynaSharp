namespace DynaSharp.Requests;

/// <summary>
/// The role of an attribute in a primary key.
/// </summary>
public enum KeyType
{
    /// <summary>A role this library does not know. Never valid in a request.</summary>
    Unknown = 0,

    /// <summary>Partition key, DynamoDB name <c>HASH</c>.</summary>
    Hash,

    /// <summary>Sort key, DynamoDB name <c>RANGE</c>.</summary>
    Range,
}
