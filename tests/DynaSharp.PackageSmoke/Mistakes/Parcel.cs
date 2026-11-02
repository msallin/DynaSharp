using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA117: a key that starts empty, which a service only learns of from a build if the rule is a warning.</summary>
[DynamoDbEntity]
[Discriminator("PARCEL")]
[PartitionKey("PARCEL#{Id}")]
public partial class Parcel
{
    public string Id { get; set; } = string.Empty;
}
