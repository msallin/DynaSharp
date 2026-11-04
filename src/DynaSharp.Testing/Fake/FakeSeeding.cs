using DynaSharp.Mapping;

namespace DynaSharp.Testing;

/// <content>The direct API for entities: they go in through <c>WriteAttributes</c> and come out through the reading code of the entity, as <see cref="StubResponse"/> and <see cref="RecordedRequest.ReadItem{T}()"/> do.</content>
public sealed partial class FakeDynamoDb
{
    /// <summary>
    /// Puts an entity, as a <c>PutItem</c> of <see cref="Item.From{T}(T)"/> does: with the keys, the discriminator and the version the entity writes. A version is stored as the entity holds it, not
    /// raised as a <c>Put</c> command raises it.
    /// </summary>
    /// <typeparam name="T">A generated entity or document.</typeparam>
    /// <param name="tableName">The table.</param>
    /// <param name="entity">The entity.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">DynamoDB would reject the item.</exception>
    public void Put<T>(string tableName, T entity)
        where T : IDynamoDbDocument<T> => Put(tableName, Item.From(entity));

    /// <summary>Gets an item by its key and reads it as an entity.</summary>
    /// <remarks>
    /// A key names one item, so an item of another entity on that key is a mistake of the test and throws, as <c>Get(key).ItemAsync()</c> of a client throws for it. Unlike <see cref="Items{T}(string)"/>, it
    /// does not skip it.
    /// </remarks>
    /// <typeparam name="T">A generated entity or document.</typeparam>
    /// <param name="tableName">The table.</param>
    /// <param name="key">The key attributes, such as <c>StubResponse.KeyOf(Customer.Keys.Of("alice"))</c>.</param>
    /// <returns>The entity, or the default of <typeparamref name="T"/> if there is no item: <see langword="null"/> for a class.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">The key does not match the schema of the table.</exception>
    /// <exception cref="DynamoDbMappingException">The item is not one the entity can read, also when it is of another entity.</exception>
    public T? Get<T>(string tableName, Item key)
        where T : IDynamoDbDocument<T> => Get(tableName, key) is { } item ? item.To<T>() : default;

    /// <summary>
    /// Gets the items of a table that are of an entity, in the order of <see cref="Items(string)"/>, read as entities. On a table of several entities it gives those of <typeparamref name="T"/> and skips
    /// the others, as a typed <c>Scan()</c> does with its filter on the discriminator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For an entity with a discriminator, an item is of it when its discriminator attribute, <c>_type</c> or the one the entity names, is a string equal to the discriminator, which is what
    /// <see cref="Item.Is{T}"/> tests. Every other item is skipped: one of another entity, and one without the attribute, which a scan of the client does not return either. An item that is of the entity and
    /// that it cannot read throws, because that is a bug of the model or of a writer.
    /// </para>
    /// <para>
    /// For an entity without a discriminator, with <c>[Discriminator(OptOut = true)]</c>, and for a document, nothing tells the items apart, so every item is read, and one that the type cannot read throws.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">A generated entity or document.</typeparam>
    /// <param name="tableName">The table.</param>
    /// <returns>The entities, a snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="DynamoDbMappingException">An item of the entity, or with a type without a discriminator any item, is not one the type can read.</exception>
    public IReadOnlyList<T> Items<T>(string tableName)
        where T : IDynamoDbDocument<T>
    {
        var items = Items(tableName);
        if (T.EntityInfo is not { Discriminator: { } discriminator } info)
        {
            return [.. items.Select(static item => item.To<T>())];
        }

        var attribute = info.DiscriminatorAttributeName;
        return [.. items.Where(item => item.TryGetValue(attribute, out var value) && value.Type == AttributeType.String && value.AsString() == discriminator).Select(static item => item.To<T>())];
    }
}
