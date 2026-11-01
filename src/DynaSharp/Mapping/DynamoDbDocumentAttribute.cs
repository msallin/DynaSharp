namespace DynaSharp.Mapping;

/// <summary>
/// Maps a type to a DynamoDB map, for a type nested in an entity such as an address. A document has no keys and no discriminator.
/// The type must be <c>partial</c>, as must its containing types.
/// </summary>
/// <remarks>A type is either an entity or a document, never both. An entity can be nested in another entity without this attribute.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbDocumentAttribute : Attribute
{
}
