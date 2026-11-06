namespace DynaSharp.Generators;

/// <summary>The class <c>DynamoDbEntities</c> of an assembly, with <c>IncludingAll()</c>.</summary>
/// <param name="Namespace">The namespace as code.</param>
/// <param name="Accessibility">Either <c>public</c>, when every entity is, or <c>internal</c>.</param>
/// <param name="Entities">The entities by <see cref="RegisteredEntity.FullName"/>, in ordinal order, so that the file does not change with the order of the source files.</param>
internal sealed record EntitiesClass(string Namespace, string Accessibility, EquatableArray<string> Entities);
