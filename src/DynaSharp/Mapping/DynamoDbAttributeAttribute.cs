namespace DynaSharp.Mapping;

/// <summary>Sets the DynamoDB attribute name of a property. Without it the name is the C# property name, as <see cref="DynamoDbDefaultsAttribute.Naming"/> converts it, and unchanged by default.</summary>
/// <remarks>
/// It also forces a property to be stored that would not be otherwise. A non-public property is mapped like any other. A
/// computed property is written but never read.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbAttributeAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DynamoDbAttributeAttribute"/> class.</summary>
    /// <param name="name">The attribute name: 1 to 65535 bytes of UTF-8, and not the name of the entity's discriminator attribute.</param>
    public DynamoDbAttributeAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Gets the attribute name.</summary>
    public string Name { get; }
}
