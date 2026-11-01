namespace DynaSharp.Mapping;

/// <summary>
/// Stores a property only inside a key and restores it by parsing that key, which saves the bytes of a separate attribute.
/// </summary>
/// <remarks>
/// At least one key template must hold the property without loss. Text and numbers do, and a timestamp needs the format
/// <c>ticks</c>: every other format truncates. No format keeps the offset of a <see cref="DateTimeOffset"/>, which an attribute does
/// not store either. Generated projections include every key a property is restored from. A property without this attribute is stored, and a read
/// restores it the same way only if its attribute is missing, which is what an item created by an update looks like; a key that does not parse then restores nothing.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class KeyOnlyAttribute : Attribute
{
}
