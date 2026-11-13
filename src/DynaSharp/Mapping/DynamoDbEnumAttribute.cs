namespace DynaSharp.Mapping;

/// <summary>
/// Says how the converter that the source generator writes for an enum reads a stored name. <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> makes a name the enum does not have, such as one
/// that a newer version of a service wrote during a rollout, read as if the attribute were not in the item, where it would throw otherwise.
/// </summary>
/// <remarks>
/// <para>
/// It changes reads alone. A write stores the name of the member as before, or its <see cref="DynamoDbEnumNameAttribute"/>, so the stored bytes are the same with and without it, and adding it or taking it off
/// needs no migration. A name the enum does not have is one that is no stored name of its members.
/// </para>
/// <para>
/// As if the attribute were not in the item means what a read does for that, which is what a converter that answers <see cref="Serialization.IDynamoDbConverter{T}.CountsAsMissing"/> gives: a property keeps its
/// initial value, a nullable one too, so one without an initializer reads as <see langword="null"/>, a <c>required</c> member throws <see cref="DynamoDbMappingException"/>, and an input of a key that holds it is
/// restored from that key. An empty string is no name either, so it counts as missing too. A value that is not a string, such as a number, still throws, because that is no name at all.
/// </para>
/// <para>
/// It is the property that is lenient, not every place the enum is read. An element of a list, a set or a map still throws for a name the enum does not have: dropping it would give a collection that is shorter than
/// what is stored, which the next <c>Put</c> of the entity writes back, and a list has positions. A plain key still throws, because a read that took it for missing would give an entity with another key, and
/// <c>Keys.X.Parse</c> of a key template does not change. <c>Select</c> of the handle gives what it gives for a missing attribute: null for a nullable property, and for any other a throw that <see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA209</see>
/// reports where it sees the call.
/// </para>
/// <para>
/// It is for the converter that the generator writes. An enum with a <see cref="DynamoDbConverterAttribute"/> of its own has no such converter, a <c>[Flags]</c> enum is stored as a number, which has no unknown
/// name, and <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA033</see> reports both. A property with a <see cref="DynamoDbConverterAttribute"/>, or a type with a <see cref="DynamoDbDefaultConverterAttribute"/>, reads through that converter and not through
/// this.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbEnumAttribute : Attribute
{
    /// <summary>Gets or sets what a stored name that the enum does not have means, <see cref="UnknownEnumName.Throw"/> by default. See the remarks of this class.</summary>
    public UnknownEnumName UnknownName { get; set; }
}
