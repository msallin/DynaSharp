namespace DynaSharp.Generators;

/// <summary>A member of an enum.</summary>
/// <param name="Name">The stored name: the text of its <c>[DynamoDbEnumName]</c>, or the name of the member without one.</param>
/// <param name="Identifier">The name as code.</param>
/// <param name="IsCanonical">Whether this is the first member with its value. A switch over aliases would not compile (CS8510).</param>
internal sealed record EnumMember(string Name, string Identifier, bool IsCanonical);
