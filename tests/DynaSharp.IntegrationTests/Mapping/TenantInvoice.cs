using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>What an invoice goes through.</summary>
internal enum TenantInvoiceStatus
{
    Open,
    Paid,
}

/// <summary>The multi-tenant shape: the key of the index mixes an input of the table key with a property that changes.</summary>
[DynamoDbEntity]
[Discriminator("TENANT_INVOICE")]
[PartitionKey("TENANT#{TenantId}")]
[SortKey("INV#{Month}#{Number:d6}")]
[GlobalIndexKey("GSI1", PartitionKey = "TENANT#{TenantId}#STATUS#{Status}", SortKey = "{DueDate}")]
internal sealed partial record TenantInvoice
{
    public required string TenantId { get; init; }

    public required string Month { get; init; }

    public required int Number { get; init; }

    public TenantInvoiceStatus Status { get; init; }

    public DateOnly DueDate { get; init; }
}
