using System.ComponentModel;

namespace DynaSharp.Mapping;

/// <summary>A property that a key of an entity is made of.</summary>
/// <param name="PropertyName">The property, as it stands in the template.</param>
/// <param name="AttributeName">The attribute of the property, or <see langword="null"/> for a property without one, which only a key holds or a getter computes.</param>
/// <param name="ComputedFromAttributes">
/// The attributes the getter of a computed property reads, as <see cref="ComputedFromAttribute"/> names them, or <see langword="null"/> for any other input. A write by handle to one of them
/// changes this input to a value no update can know.
/// </param>
/// <param name="ToAttributeValue">
/// What the converter of the property stores for a value of this input. It is there for an input of a key of the table with an attribute of its own, the one kind that an update can be asked to store,
/// and <see langword="null"/> for any other.
/// </param>
/// <param name="IsCutOff">
/// Whether the text of this key holds less than the value, such as a timestamp in <c>{At:yyyy-MM-dd}</c>, which is enough for an index key of the same format and not for the attribute of the property.
/// </param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DynamoDbKeyInput(
    string PropertyName,
    string? AttributeName,
    IReadOnlyList<string>? ComputedFromAttributes = null,
    Func<object, AttributeValue>? ToAttributeValue = null,
    bool IsCutOff = false);
