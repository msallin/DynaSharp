namespace DynaSharp.Requests;

/// <summary>
/// How a table is charged for reads and writes.
/// </summary>
/// <remarks>
/// In a <see cref="CreateTableRequest"/>, <see cref="Unspecified"/> sends nothing and DynamoDB creates a table with
/// <see cref="Provisioned"/> capacity.
/// </remarks>
public enum BillingMode
{
    /// <summary>Not set. In a request it sends nothing; in a response it is a mode this library does not know.</summary>
    Unspecified = 0,

    /// <summary>Provisioned capacity, DynamoDB name <c>PROVISIONED</c>: the table has fixed read and write capacity units (see <see cref="ProvisionedThroughput"/>).</summary>
    Provisioned,

    /// <summary>On-demand capacity, DynamoDB name <c>PAY_PER_REQUEST</c>: DynamoDB scales with the traffic and charges per request.</summary>
    PayPerRequest,
}
