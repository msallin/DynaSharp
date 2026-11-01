using System.Diagnostics.CodeAnalysis;
using DynaSharp.Mapping;

namespace DynaSharp.Benchmarks;

/// <summary>A second type in the partition of <see cref="BenchOrder"/>, for mixed reads: the same item as far as the wire goes, of which it reads a few attributes.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Generated code creates it, and dotnet format analyzes without generated code.")]
[DynamoDbEntity]
[Discriminator("Invoice", AttributeName = "type")]
internal sealed partial class BenchInvoice
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Sk { get; init; }

    [DynamoDbAttribute("total")]
    public required decimal Total { get; init; }
}
