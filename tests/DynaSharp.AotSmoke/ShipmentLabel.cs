namespace DynaSharp.AotSmoke;

/// <summary>The contract, serialized by a source-generated context.</summary>
/// <param name="To">The address.</param>
/// <param name="Weight">The weight, a number whose digits must survive.</param>
/// <param name="Note">Left out of the map when null.</param>
internal sealed record ShipmentLabel(string To, decimal Weight, string? Note);
