namespace DynaSharp.Mapping;

/// <summary>
/// Maps a type to a DynamoDB item. The source generator implements the mapping, so the type must be <c>partial</c>, as must its
/// containing types, and it needs a partition key and a <see cref="DiscriminatorAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// [DynamoDbEntity]
/// [Discriminator("PRODUCT")]
/// [PartitionKey("PRODUCT#{Sku}")]
/// [SortKey("DETAILS")]
/// public partial record Product
/// {
///     public required string Sku { get; init; }
///     public required string Name { get; init; }
///     public required decimal Price { get; init; }
/// }
/// </code>
/// </example>
/// <remarks>
/// Every <c>public</c> instance property that can be read and that has a setter or a matching constructor parameter becomes an
/// attribute named like the property. Null values are left out of the item. So is an empty set, which DynamoDB cannot store, and a read gives a missing set back as an empty one, so <c>Tags = []</c> round-trips.
/// An empty string, list or map is stored, unless <see cref="OmitEmptyAttribute"/> leaves it out. The attribute applies to the marked type only: a
/// derived entity declares its own keys and discriminator, and inherits the mapped properties of its base.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbEntityAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the table the entity lives in, which <see cref="DynamoDbClient.Table{T}()"/> opens. It wins over <see cref="DynamoDbDefaultsAttribute.Table"/> of the assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An explicit name wins.</b> <see cref="DynamoDbClient.Table{T}(string)"/> opens the table it is given and ignores the declaration, which is how a test gives each run a table of its own. Only a
    /// declared name goes through <see cref="DynamoDbClientOptions.TableNaming"/>, which turns <c>appdata</c> into <c>dev-appdata</c> for an environment.
    /// </para>
    /// <para>
    /// <b>DynamoDB's rules apply.</b> The name has 3 to 255 characters out of <c>a-z</c>, <c>A-Z</c>, <c>0-9</c>, <c>_</c>, <c>-</c> and <c>.</c>; anything else is
    /// <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA039</see> at the attribute. <see langword="null"/>, the default, declares nothing, and the entity takes the table of the assembly, if it has one.
    /// </para>
    /// <para>
    /// <b>One table, one key schema.</b> Two entities of a project that declare one table with different names of the partition key or the sort key are
    /// <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA040</see>, and <see cref="DynamoDbClient.Table{T}()"/> of an entity that declares no table is
    /// <see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA238</see>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [DynamoDbEntity(Table = "orders")]
    /// [Discriminator("ORDER")]
    /// [PartitionKey("CUSTOMER#{CustomerId}")]
    /// [SortKey("ORDER#{OrderId}")]
    /// public partial record Order
    /// {
    ///     public required string CustomerId { get; init; }
    ///     public required string OrderId { get; init; }
    /// }
    /// </code>
    /// <code>
    /// var orders = client.Table&lt;Order&gt;(); // the table "orders", or what TableNaming makes of it
    /// </code>
    /// </example>
    public string? Table { get; set; }
}
