namespace DynaSharp.Mapping;

/// <summary>
/// Stores an enum member under a name of its own in place of the member's name: in an attribute, in a key template, and in a condition with the enum in a hole. Reading that name gives the member back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everywhere the enum is stored by name.</b> The converter that the source generator writes for the enum uses it for every write and every read: a property, an element of a set or a list, a placeholder of a
/// key and <c>Keys.X.Parse</c> of it, and a value in a condition, a filter or a key condition. A placeholder with <c>d&lt;w&gt;</c> writes the value of the member and ignores the name.
/// </para>
/// <para>
/// <b>Names another writer used.</b> It is how an entity matches what another system stores, such as <c>APPOINTMENT</c> beside the member <c>Appointment</c>. A read compares the bytes as they are, with no case
/// folding, and only the stored name reads: the member's own name no longer does. Changing a stored name later is a data migration, as renaming an attribute is.
/// </para>
/// <para>
/// <b>What is checked.</b> An empty name, one with a control character, two members of different values with one stored name, or a stored name that is another member's name, is an error
/// (<see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA044</see> and DYNA045), and so is a name that holds a delimiter of a key template the enum stands in (DYNA125). A <c>[Flags]</c> enum is
/// stored as a number, and an enum with a <see cref="DynamoDbConverterAttribute"/> of its own does not use the generated converter, so the attribute has no effect there (DYNA046). With
/// <see cref="DynamoDbEnumAttribute.UnknownName"/>, a name that is no stored name of the enum counts as missing.
/// </para>
/// <para>
/// <b>Aliases.</b> Members of one value share their stored form: a write stores the name of the first of them, and each of their names reads as that value.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public enum Channel
/// {
///     [DynamoDbEnumName("WEB")] Web,
///     [DynamoDbEnumName("STORE")] Store,
/// }
/// </code>
/// </example>
/// <param name="name">The name to store the member under. It cannot be empty or hold a control character.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbEnumNameAttribute(string name) : Attribute
{
    /// <summary>Gets the name the member is stored under.</summary>
    public string Name { get; } = name;
}
