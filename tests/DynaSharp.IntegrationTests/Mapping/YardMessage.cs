using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Who a message of a yard is about, stored under the names the earlier writer of the table used.</summary>
public enum MessageOwner
{
    /// <summary>An appointment of the yard.</summary>
    [DynamoDbEnumName("APPOINTMENT")]
    Appointment,

    /// <summary>A loading unit.</summary>
    [DynamoDbEnumName("LOADING_UNIT")]
    LoadingUnit,

    /// <summary>A topic of its own.</summary>
    [DynamoDbEnumName("TOPIC")]
    Topic,
}

/// <summary>A message of a yard in the shop table, whose partition key starts with the stored name of its owner, as the other entities of the table start with an uppercase literal.</summary>
[DynamoDbEntity]
[Discriminator("YARD_MESSAGE")]
[PartitionKey("{Owner}#{OwnerKey:raw}")]
[SortKey("MSG#{Id}")]
public sealed partial record YardMessage
{
    public required MessageOwner Owner { get; init; }

    [KeyOnly]
    public required string OwnerKey { get; init; }

    [KeyOnly]
    public required string Id { get; init; }

    public MessageOwner? ForwardedTo { get; init; }
}
