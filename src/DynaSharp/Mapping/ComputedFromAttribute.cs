namespace DynaSharp.Mapping;

/// <summary>
/// Names the stored properties a computed property is derived from, so that it can feed a key template of an index. A getter is code the generator cannot read, so this is the promise it goes by: the
/// getter reads what is named here and nothing else that an update can change.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Put</c>, <c>Item.From</c> and a batch run the getter, so the key is right. An update has no instance to run it on, which is why an index template takes a computed property only with this attribute:
/// a write by handle to a named property, also to a path inside it such as <c>Attrs.Message.At(Msg.Attrs.Arrival)</c>, throws <see cref="InvalidOperationException"/> when the update ends, and so does a write by handle to a
/// computed property with an attribute of its own, where the key would follow the update and not the getter, and an update of another input of the same template, because the key cannot follow either way. Use a <c>Put</c> of the changed entity. <c>SetIfNotNull</c> of null writes nothing and stays legal.
/// </para>
/// <para>
/// A raw path is not checked, as everywhere: <c>.Set("message", value)</c> goes out, and the index keeps the key it had. On a key of the table this attribute adds the same check, and there DynamoDB
/// cannot update the key anyway.
/// </para>
/// <para>
/// Each name is a property of the same type with an attribute of its own: not computed, not <see cref="DynamoDbIgnoreAttribute"/> and not <see cref="KeyOnlyAttribute"/>, and not the
/// <see cref="VersionAttribute"/>, which every write moves without a handle. One level only, and a named property may feed another key. The build reports what breaks this as <c>DYNA119</c>,
/// and a write that will throw as <c>DYNA219</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ComputedFrom(nameof(Arrival))]
/// public DateOnly ArrivalDay => DateOnly.FromDateTime(Arrival.UtcDateTime);
/// </code>
/// </example>
/// <param name="properties">The names of the properties, for which <c>nameof</c> is the way to write them.</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ComputedFromAttribute(params string[] properties) : Attribute
{
    /// <summary>Gets the names of the properties.</summary>
    public IReadOnlyList<string> Properties { get; } = properties;
}
