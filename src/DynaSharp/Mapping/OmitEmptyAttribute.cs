namespace DynaSharp.Mapping;

/// <summary>
/// Treats an empty string or an empty collection like null, so that an item does not fill up with attributes that say nothing. It goes on a <see cref="string"/>, list, set, dictionary or array property, nullable or
/// not, that feeds no key, or on an entity or document type, where it covers those properties of that type that feed no key. <c>[OmitEmpty(false)]</c> takes it back for a property or a type, where a type or
/// <see cref="DynamoDbDefaultsAttribute.OmitEmpty"/> asked for it.
/// </summary>
/// <remarks>
/// <para>
/// An empty value is written like null: a <c>Put</c>, a batch, a transaction and <see cref="Item.From{T}(T)"/> leave the attribute out, <c>Set</c> by handle is a <c>Remove</c>, <c>SetIfNotNull</c> adds nothing, and
/// <c>SetIfNotExists</c> by handle rejects it, as it rejects null. A raw path knows no handle, so it writes what it is given. A condition such as <c>Attrs.Name.Eq("")</c> asks what it says, and finds what other code stored.
/// </para>
/// <para>
/// Reads do not change. A stored <c>""</c>, written by other code, reads as <c>""</c>, and a missing attribute reads as it always does: a nullable property as <see langword="null"/>, a set as empty, and anything else
/// keeps its initial value. So an empty value comes back as what the property starts as, which is <see langword="null"/> for a <c>string?</c> and a filled list for a property that starts with one. <c>Select</c> of a
/// property that is not nullable throws for a missing attribute, which <c>DYNA209</c> points out, unless it is a set.
/// </para>
/// <para>
/// That goes for lists, sets, dictionaries and arrays too, so an empty list does not read back as empty: it reads as <see langword="null"/> or as the initial value. Null and <c>[]</c> can mean different things, such as
/// not asked and asked with no answer, and <c>[OmitEmpty(false)]</c> on the property stores the empty one.
/// </para>
/// <para>
/// This is wider than the rule of the AWS SDK, which leaves out an empty string and keeps an empty list of documents, an empty dictionary and an empty <c>byte[]</c>. So code that has to store what the SDK stored takes
/// <c>[OmitEmpty(false)]</c> on those.
/// </para>
/// <para>
/// A key must not be empty, and a read that cannot do without a value could never read an empty one back, which is a <c>required</c> property that is neither nullable nor a set, and a constructor parameter of the same
/// kind without a default. On a type and through <see cref="DynamoDbDefaultsAttribute.OmitEmpty"/> such a property stays written, and on a property, as on anything that is no string or collection, this is the error
/// <c>DYNA030</c>. It is not inherited: a derived type says so again, and a nested type has its own, whether a property that holds it says anything or not.
/// </para>
/// <para>A type without this attribute pays nothing for it. On a type with it, the write of an item tests a length or a count, and a handle costs no allocation.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class OmitEmptyAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="OmitEmptyAttribute"/> class, which leaves empty values out.</summary>
    public OmitEmptyAttribute()
        : this(true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OmitEmptyAttribute"/> class.</summary>
    /// <param name="enabled">Whether empty values are left out. False writes them, whatever the type or <see cref="DynamoDbDefaultsAttribute.OmitEmpty"/> says.</param>
    public OmitEmptyAttribute(bool enabled)
    {
        Enabled = enabled;
    }

    /// <summary>Gets a value indicating whether empty values are left out.</summary>
    public bool Enabled { get; }
}
