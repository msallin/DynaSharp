using System.ComponentModel;

namespace DynaSharp.Mapping;

/// <summary>A property that a key of an entity is made of.</summary>
/// <param name="PropertyName">The property, as it stands in the template.</param>
/// <param name="AttributeName">The attribute of the property, or <see langword="null"/> for a property without one, which only a key holds or a getter computes.</param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DynamoDbKeyInput(string PropertyName, string? AttributeName);
