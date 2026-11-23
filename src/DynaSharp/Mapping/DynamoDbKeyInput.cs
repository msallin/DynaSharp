using System.ComponentModel;

namespace DynaSharp.Mapping;

/// <summary>A property that a key of an entity is made of.</summary>
/// <param name="propertyName">The property, as it stands in the template.</param>
/// <param name="attributeName">The attribute of the property, or <see langword="null"/> for a property without one, which only a key holds or a getter computes.</param>
/// <param name="computedFromAttributes">
/// The attributes the getter of a computed property reads, as <see cref="ComputedFromAttribute"/> names them, or <see langword="null"/> for any other input. A write by handle to one of them
/// changes this input to a value no update can know.
/// </param>
/// <param name="toAttributeValue">
/// What the converter of the property stores for a value of this input. It is there for an input of a key of the table with an attribute of its own, the one kind that an update can be asked to store,
/// and <see langword="null"/> for any other.
/// </param>
/// <param name="isCutOff">
/// Whether the text of this key holds less than the value, such as a timestamp in <c>{At:yyyy-MM-dd}</c>, which is enough for an index key of the same format and not for the attribute of the property.
/// </param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DynamoDbKeyInput(
    string propertyName,
    string? attributeName,
    IReadOnlyList<string>? computedFromAttributes = null,
    Func<object, AttributeValue>? toAttributeValue = null,
    bool isCutOff = false)
{
    /// <summary>Gets the property, as it stands in the template.</summary>
    public string PropertyName { get; } = propertyName;

    /// <summary>Gets the attribute of the property, or <see langword="null"/> for a property without one.</summary>
    public string? AttributeName { get; } = attributeName;

    /// <summary>Gets the attributes the getter of a computed property reads, or <see langword="null"/> for any other input.</summary>
    public IReadOnlyList<string>? ComputedFromAttributes { get; } = computedFromAttributes;

    /// <summary>Gets what the converter of the property stores for a value of this input, or <see langword="null"/> for an input an update is never asked to store.</summary>
    public Func<object, AttributeValue>? ToAttributeValue { get; } = toAttributeValue;

    /// <summary>Gets a value indicating whether the text of this key holds less than the value.</summary>
    public bool IsCutOff { get; } = isCutOff;
}
