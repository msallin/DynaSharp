using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity as a struct, of a table without a sort key, without a discriminator: what a table written by other tools looks like.</summary>
[DynamoDbEntity]
[Discriminator(OptOut = true)]
internal readonly partial record struct Session([property: PartitionKey(AttributeName = "id")] string Id);
