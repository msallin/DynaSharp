namespace DynaSharp.Generators;

/// <summary>What the attribute on a type declares it to be.</summary>
internal enum MappedKind
{
    /// <summary>A type with keys, marked <c>[DynamoDbEntity]</c>.</summary>
    Entity,

    /// <summary>A nested type without keys, marked <c>[DynamoDbDocument]</c>.</summary>
    Document,
}
