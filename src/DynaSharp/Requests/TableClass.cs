namespace DynaSharp.Requests;

/// <summary>
/// The storage class of a table, which trades storage cost against the cost of reads and writes.
/// </summary>
/// <remarks>
/// In a <see cref="CreateTableRequest"/>, <see cref="Unspecified"/> sends nothing and DynamoDB uses <see cref="Standard"/>.
/// </remarks>
public enum TableClass
{
    /// <summary>Not set. In a request it sends nothing; in a response it is a class this library does not know.</summary>
    Unspecified = 0,

    /// <summary>The default class, DynamoDB name <c>STANDARD</c>.</summary>
    Standard,

    /// <summary>Cheaper storage and more expensive requests, DynamoDB name <c>STANDARD_INFREQUENT_ACCESS</c>, for tables that are rarely read.</summary>
    StandardInfrequentAccess,
}
