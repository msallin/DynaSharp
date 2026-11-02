using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA120: a value that the table takes and an index refuses, which a service only learns of from a build if the rule is a warning.</summary>
[DynamoDbEntity]
[Discriminator("TICKET")]
[PartitionKey("TICKET#{Code:raw}")]
[GlobalIndexKey("ByCode", PartitionKey = "CODE#{Code}")]
public partial class Ticket
{
    public required string Code { get; set; }
}
