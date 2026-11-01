using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Testing.Consumer;

/// <summary>The marker item that keeps an address unique: a put with <c>attribute_not_exists</c> in the transaction that takes the address.</summary>
/// <param name="Email">The address, which is the key.</param>
/// <param name="SubscriberId">Who holds it.</param>
[DynamoDbEntity]
[Discriminator("EMAIL")]
internal sealed partial record EmailClaim(
    [property: PartitionKey(AttributeName = "pk")] string Email,
    [property: DynamoDbAttribute("subscriberId")] string SubscriberId);
