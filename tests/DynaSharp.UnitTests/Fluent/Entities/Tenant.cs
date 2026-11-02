using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity whose discriminator lives in an attribute of another name.</summary>
[DynamoDbEntity]
[Discriminator("TENANT", AttributeName = "kind")]
internal sealed partial record Tenant([property: PartitionKey(AttributeName = "pk")] string Id);
