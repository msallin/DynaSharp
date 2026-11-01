namespace DynaSharp.Requests;

/// <summary>
/// Which attributes of the table a secondary index copies, besides the key attributes of the table and of the index.
/// </summary>
public enum ProjectionType
{
    /// <summary>A projection this library does not know. Never valid in a request.</summary>
    Unknown = 0,

    /// <summary>Every attribute of the item, DynamoDB name <c>ALL</c>. Fastest to read, but the index is as large as the table.</summary>
    All,

    /// <summary>Only the key attributes, DynamoDB name <c>KEYS_ONLY</c>. The smallest and cheapest index.</summary>
    KeysOnly,

    /// <summary>The key attributes plus the attributes named in <see cref="IndexProjection.NonKeyAttributes"/>, DynamoDB name <c>INCLUDE</c>.</summary>
    Include,
}
