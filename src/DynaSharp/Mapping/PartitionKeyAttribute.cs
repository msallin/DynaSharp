namespace DynaSharp.Mapping;

/// <summary>
/// Declares the partition key of an entity. On the type it takes a key template such as <c>USER#{UserId}</c>, stored in an
/// attribute named <c>pk</c>. On a property it takes no template and declares a plain key: the property's own attribute is the key.
/// </summary>
/// <remarks>
/// <para>
/// Literals in a template are printable ASCII, <c>{{</c> and <c>}}</c> are literal braces, and a placeholder is
/// <c>{Property}</c> or <c>{Property:format}</c>. A variable-width value that contains a character next to a placeholder throws
/// <see cref="KeyFormatException"/>, because <c>1#ORDER</c> in <c>USER#{Id}</c> could address another item. The format <c>raw</c>
/// lets them through for a <c>string</c> that is the last thing in the template, such as <c>appointment#{Key:raw}</c>, where they can
/// address nothing else. DynamoDB keeps a partition key for the life of the item, so a template input cannot be updated.
/// </para>
/// <para>
/// A timestamp is written in UTC and takes <c>yyyy</c>, <c>yyyy-MM</c>, <c>yyyy-MM-dd</c>, <c>yyyy-MM-ddTHH</c>, <c>yyyy-MM-ddTHH:mm</c>, <c>s</c>, <c>ms</c>, <c>us</c> or <c>ticks</c>. A bare
/// <c>{At}</c> is <c>ms</c>: it keeps milliseconds, so two times within one millisecond give one key, unless the rest of the key differs. Only <c>ticks</c> is lossless.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class PartitionKeyAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="PartitionKeyAttribute"/> class, for a plain key on a property.</summary>
    public PartitionKeyAttribute()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PartitionKeyAttribute"/> class, for a key template on a type.</summary>
    /// <param name="template">The key template, for example <c>USER#{UserId}</c>.</param>
    public PartitionKeyAttribute(string template)
    {
        Template = template;
    }

    /// <summary>Gets the key template, or null for a plain key.</summary>
    public string? Template { get; }

    /// <summary>
    /// Gets or sets the name of the DynamoDB attribute. The default is <c>pk</c> for a template; for a plain key it is the
    /// attribute name of the property, and setting it here is the same as <see cref="DynamoDbAttributeAttribute"/>.
    /// </summary>
    public string? AttributeName { get; set; }
}
