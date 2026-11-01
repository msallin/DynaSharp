namespace DynaSharp.Mapping;

/// <summary>
/// Maps a type to a DynamoDB item. The source generator implements the mapping, so the type must be <c>partial</c>, as must its
/// containing types, and it needs a partition key and a <see cref="DiscriminatorAttribute"/>.
/// </summary>
/// <remarks>
/// Every <c>public</c> instance property that can be read and that has a setter or a matching constructor parameter becomes an
/// attribute named like the property. Null values are left out of the item. The attribute applies to the marked type only: a
/// derived entity declares its own keys and discriminator, and inherits the mapped properties of its base.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbEntityAttribute : Attribute
{
}
