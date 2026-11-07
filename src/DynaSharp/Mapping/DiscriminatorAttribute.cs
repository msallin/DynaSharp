namespace DynaSharp.Mapping;

/// <summary>
/// Sets the discriminator of an entity: the value stored in each of its items, in an attribute named <c>_type</c>, that tells which entity an item is, so that several entities can share
/// a table and a typed read gets its own items. It is required on every entity, as <c>[Discriminator("ORDER")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Required.</b> A default of the type name would turn renaming a class into a silent migration. <see cref="DynamoDbDefaultsAttribute.DiscriminatorOptOut"/> opts out for the entities of an
/// assembly that do not have it.
/// </para>
/// <para>
/// <b>Reads.</b> Typed reads check the attribute and fail with a mapping exception naming the type they found, because a query over an overloaded
/// prefix can return other types. Typed queries and scans filter on it. For a table written by other tools, which has no such
/// attribute, set <see cref="OptOut"/> instead of a value: the type then takes no part in mixed queries. An item has no order of attributes,
/// so an attribute the two types share with values of different shapes can fail first, under its own name.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DiscriminatorAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DiscriminatorAttribute"/> class, which opts out with <see cref="OptOut"/>.</summary>
    public DiscriminatorAttribute()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DiscriminatorAttribute"/> class.</summary>
    /// <param name="value">The value stored in every item of the type. It cannot be empty.</param>
    public DiscriminatorAttribute(string value)
    {
        Value = value;
    }

    /// <summary>Gets the value stored in every item of the type, or null after opting out.</summary>
    public string? Value { get; }

    /// <summary>Gets or sets the name of the attribute that holds the value. The default is <c>_type</c>.</summary>
    public string? AttributeName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the type has no discriminator at all, for a table written by other tools: nothing writes <c>_type</c>, a query or a scan does not filter on it, and the type takes no part
    /// in mixed reads. <c>[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]</c>, <see cref="DynamoDbDefaultsAttribute.DiscriminatorOptOut"/>, says the same once for every entity of an assembly that has no
    /// <see cref="DiscriminatorAttribute"/>.
    /// </summary>
    public bool OptOut { get; set; }
}
