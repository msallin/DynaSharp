using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

internal enum InvoiceState
{
    Open,
    Paid,
}

/// <summary>The multi-tenant shape: an index key that mixes an input of the table key with a property that changes.</summary>
[DynamoDbEntity]
[Discriminator("INVOICE")]
[PartitionKey("TENANT#{TenantId}")]
[SortKey("INV#{Month}#{Number:d6}")]
[GlobalIndexKey("ByStatus", PartitionKey = "TENANT#{TenantId}#STATUS#{Status}", SortKey = "{DueDate}")]
internal sealed partial record TenantInvoice
{
    public required string TenantId { get; init; }

    public required string Month { get; init; }

    public required int Number { get; init; }

    public InvoiceState Status { get; init; }

    public DateOnly DueDate { get; init; }

    public string? Note { get; init; }
}
