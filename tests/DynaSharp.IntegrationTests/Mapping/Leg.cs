using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A document of documents, for a path two documents deep.</summary>
[DynamoDbDocument]
internal sealed partial record Leg(Depot From, Depot To);
