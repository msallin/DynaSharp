using System.Text.Json.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>The source-generated serializer of the contract, which Native AOT needs.</summary>
[JsonSerializable(typeof(ShipmentLabel))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ShipmentJson : JsonSerializerContext;
