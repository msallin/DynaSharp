using System.Text.Json;
using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A nested document with JSON in it.</summary>
[DynamoDbDocument]
public sealed partial record ConsignmentStop(JsonElement Address);
