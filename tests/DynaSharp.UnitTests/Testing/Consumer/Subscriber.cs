using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Testing.Consumer;

/// <summary>An entity as an application declares it, mapped by the real generator.</summary>
/// <param name="Id">The subscriber.</param>
/// <param name="Email">The address, which <see cref="EmailClaim"/> keeps unique.</param>
/// <param name="Plan">What the subscriber pays for.</param>
[DynamoDbEntity]
[Discriminator("SUBSCRIBER")]
internal sealed partial record Subscriber(
    [property: PartitionKey(AttributeName = "pk")] string Id,
    [property: DynamoDbAttribute("email")] string Email,
    [property: DynamoDbAttribute("plan")] string Plan = "free");
