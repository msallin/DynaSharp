using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA034: a base type with a private setter, which the generated code of <see cref="SubLedger"/> cannot reach, so it would drop the owner.</summary>
public class Ledger
{
    public string Owner { get; private set; } = "nobody";
}

[DynamoDbEntity]
[Discriminator("SUB_LEDGER")]
[PartitionKey("SUB_LEDGER#{Id}")]
public partial class SubLedger : Ledger
{
    public required string Id { get; init; }
}
