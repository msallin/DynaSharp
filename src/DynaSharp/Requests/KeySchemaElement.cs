namespace DynaSharp.Requests;

/// <summary>
/// One attribute of a primary key.
/// </summary>
/// <param name="AttributeName">The attribute name.</param>
/// <param name="KeyType">Whether it is the partition (hash) or sort (range) key.</param>
public sealed record KeySchemaElement(string AttributeName, KeyType KeyType);
