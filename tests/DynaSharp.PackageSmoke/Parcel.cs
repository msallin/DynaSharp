using DynaSharp.Mapping;

namespace DynaSharp.PackageSmoke;

/// <summary>Stored by name, through a converter generated into the consumer's assembly.</summary>
public enum ParcelState
{
    /// <summary>In the warehouse.</summary>
    Packed,

    /// <summary>On its way.</summary>
    Sent,
}

/// <summary>A type mapped by the generator from the package, the way a user's project gets it.</summary>
[DynamoDbEntity]
[Discriminator("PARCEL")]
public partial record Parcel([property: PartitionKey] string Id, int Weight, ParcelState State);

/// <summary>A stop of a delivery round, with what only the generator gives: key builders, an index and typed attribute handles.</summary>
[DynamoDbEntity]
[Discriminator("STOP")]
[PartitionKey("ROUND#{Round}")]
[SortKey("STOP#{Number:d3}")]
[GlobalIndexKey("ByState", PartitionKey = "STATE#{State}", SortKey = "{Number:d3}")]
public partial record Stop
{
    /// <summary>Gets the delivery round.</summary>
    public required string Round { get; init; }

    /// <summary>Gets the position in the round.</summary>
    public required int Number { get; init; }

    /// <summary>Gets the state of the parcel.</summary>
    public required ParcelState State { get; init; }

    /// <summary>Gets how many attempts there were.</summary>
    public int Attempts { get; init; }
}
