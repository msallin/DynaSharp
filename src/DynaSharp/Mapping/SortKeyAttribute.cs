namespace DynaSharp.Mapping;

/// <summary>
/// Declares the sort key of an entity. On the type it takes a key template such as <c>ORDER#{CreatedAt}#{OrderId}</c>, stored in an
/// attribute named <c>sk</c>. On a property it takes no template and declares a plain key: the property's own attribute is the key.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB compares string keys by their UTF-8 bytes. Numbers therefore need a fixed format such as <c>{Id:d10}</c> to sort
/// numerically, and a template that has no placeholders, such as <c>PROFILE</c>, is legal. The template syntax is described on
/// <see cref="PartitionKeyAttribute"/>, and so is the format <c>raw</c>, with what it lifts and what it does not.
/// </para>
/// <para>
/// A timestamp is written in UTC and takes <c>yyyy</c>, <c>yyyy-MM</c>, <c>yyyy-MM-dd</c>, <c>yyyy-MM-ddTHH</c>, <c>yyyy-MM-ddTHH:mm</c>, <c>s</c>, <c>ms</c>, <c>us</c> or <c>ticks</c>. A bare
/// <c>{At}</c> is <c>ms</c>: it keeps milliseconds, so two times within one millisecond give one key, unless the rest of the key differs. Only <c>ticks</c> is lossless.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SortKeyAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="SortKeyAttribute"/> class, for a plain key on a property.</summary>
    public SortKeyAttribute()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SortKeyAttribute"/> class, for a key template on a type.</summary>
    /// <param name="template">The key template, for example <c>ORDER#{CreatedAt}#{OrderId}</c>.</param>
    public SortKeyAttribute(string template)
    {
        Template = template;
    }

    /// <summary>Gets the key template, or null for a plain key.</summary>
    public string? Template { get; }

    /// <summary>
    /// Gets or sets the name of the DynamoDB attribute. The default is <c>sk</c> for a template; for a plain key it is the
    /// attribute name of the property, and setting it here is the same as <see cref="DynamoDbAttributeAttribute"/>.
    /// </summary>
    public string? AttributeName { get; set; }
}
