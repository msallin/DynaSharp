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
    /// <typeparam name="T">A generated entity or document.</typeparam>
    /// <param name="tableName">The table.</param>
    /// <param name="key">The key attributes, such as <c>StubResponse.KeyOf(Customer.Keys.Of("alice"))</c>.</param>
    /// <returns>The entity, or the default of <typeparamref name="T"/> if there is no item: <see langword="null"/> for a class.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">The key does not match the schema of the table.</exception>
    /// <exception cref="DynamoDbMappingException">The item is not one the entity can read.</exception>
    public T? Get<T>(string tableName, Item key)
        where T : IDynamoDbDocument<T> => Get(tableName, key) is { } item ? item.To<T>() : default;

    /// <summary>Gets every item of a table, in the order of <see cref="Items(string)"/>, read as entities.</summary>
    /// <typeparam name="T">A generated entity or document.</typeparam>
    /// <param name="tableName">The table.</param>
    /// <returns>The entities, a snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="DynamoDbMappingException">An item is not one the entity can read: a table of several entities reads each with its own type.</exception>
    public IReadOnlyList<T> Items<T>(string tableName)
        where T : IDynamoDbDocument<T> => [.. Items(tableName).Select(static item => item.To<T>())];
}
