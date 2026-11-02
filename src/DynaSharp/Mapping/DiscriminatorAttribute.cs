namespace DynaSharp.Mapping;

/// <summary>
/// Names an entity type in its items, in a dedicated attribute named <c>_type</c>. It is required on every entity: a default of the
/// type name would turn renaming a class into a silent migration. <see cref="DynamoDbDefaultsAttribute.NoDiscriminator"/> opts out for the entities of an assembly that do not have it.
/// </summary>
/// <remarks>
/// Typed reads check the attribute and fail with a mapping exception naming the type they found, because a query over an overloaded
/// prefix can return other types. Typed queries and scans filter on it. For a table written by other tools, which has no such
/// attribute, set <see cref="OptOut"/> instead of a value: the type then takes no part in mixed queries.
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

    /// <summary>Gets or sets a value indicating whether the type has no discriminator at all, for a table written by other tools.</summary>
    public bool OptOut { get; set; }
}
