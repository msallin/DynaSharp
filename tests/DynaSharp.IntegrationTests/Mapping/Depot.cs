using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A positional document inside an entity.</summary>
[DynamoDbDocument]
internal sealed partial record Depot(string City, int Dock = 1);
