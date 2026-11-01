namespace DynaSharp.Mapping;

/// <summary>
/// Treats an empty string like null, so that an item does not fill up with attributes that say nothing, and a value that came in as <c>""</c> does not store as a value. It goes on a <c>string?</c> property that
/// feeds no key, or on an entity or document type, where it covers the <c>string?</c> properties of that type that feed no key.
/// </summary>
/// <remarks>
/// <para>
/// An empty string is written like null: a <c>Put</c> and <see cref="Item.From{T}(T)"/> leave the attribute out, <c>Set</c> by handle is a <c>Remove</c>, <c>SetIfNotNull</c> adds nothing, and <c>SetIfNotExists</c> by
/// handle rejects it, as it rejects null. A raw path knows no handle, so it writes what it is given. Reads do not change: a stored <c>""</c>, written by other code, reads as <c>""</c>, and a missing attribute as
/// <see langword="null"/>, so a value of <c>""</c> comes back as <see langword="null"/> after a write. A condition such as <c>Attrs.Name.Eq("")</c> asks what it says, and finds what other code stored.
/// </para>
/// <para>
/// A key must not be empty, so a property that feeds a key is left alone on a type, and on a property, as on anything that is not a <c>string?</c>, this is the error <c>DYNA030</c>. It is not inherited: a derived type
/// says so again, and a nested type has its own, whether a property that holds it says anything or not.
/// </para>
/// <para>A type without this attribute pays nothing for it. On a type with it, the write of an item tests a length, and a handle costs no allocation.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class OmitEmptyAttribute : Attribute
{
}
