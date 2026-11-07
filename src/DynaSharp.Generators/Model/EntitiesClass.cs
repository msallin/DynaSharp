namespace DynaSharp.Generators;

/// <summary>The class <c>DynamoDbEntities</c> of an assembly, internal in the namespace <c>DynaSharp</c>, with <c>IncludingAll()</c>.</summary>
/// <param name="Entities">The entities by <see cref="RegisteredEntity.FullName"/>, in ordinal order, so that the file does not change with the order of the source files.</param>
internal sealed record EntitiesClass(EquatableArray<string> Entities);
