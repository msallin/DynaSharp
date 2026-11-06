namespace DynaSharp.Mapping;

/// <summary>
/// Maps a type to a DynamoDB map, for a type nested in an entity such as an address. A document has no keys and no discriminator.
/// The type must be <c>partial</c>, as must its containing types.
/// </summary>
/// <remarks>
/// <para>
/// A document stores what an entity stores: every <c>public</c> instance property that can be read and that has a setter or a matching
/// constructor parameter becomes an attribute of the map, and null values are left out of it. A get-only computed property is not stored, and
/// its getter computes it again after a read; <see cref="DynamoDbAttributeAttribute"/> on it stores it, written and never read.
/// </para>
/// <para>A type is either an entity or a document, never both. An entity can be nested in another entity without this attribute, which <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA031</see> reports as a warning.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbDocumentAttribute : Attribute
{
}
