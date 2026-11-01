using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A command that changes attributes, for a helper that works on all of them, see <see cref="IConditional{TSelf}"/>. Audit fields are the
/// usual case: <c>command.Set("updatedBy", user)</c>.
/// </summary>
/// <typeparam name="TSelf">The command.</typeparam>
public interface IUpdatable<TSelf>
    where TSelf : IUpdatable<TSelf>, allows ref struct
{
    /// <summary>Sets an attribute, creating it if it does not exist (<c>SET</c>).</summary>
    /// <param name="path">The name or document path. The parent of a nested path must exist.</param>
    /// <param name="value">The value.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Set(string path, AttributeValue value);

    /// <summary>
    /// Sets an attribute, creating it if it does not exist (<c>SET</c>), and keeps every key made from it consistent, or throws when the command ends. A null value is a
    /// <see cref="Remove{TValue, TConverter}(Attr{TValue, TConverter})"/>, for every attribute, because a generated entity writes no attribute for a null property either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Key templates.</b> The handle of a property that feeds a key template keeps that key consistent, which a raw path does not. When the command ends, all inputs of the template must be set: then the
    /// derived attribute is written in the same <c>SET</c>, and otherwise sending throws <see cref="InvalidOperationException"/> naming the template and what is missing, because the index
    /// would go stale. An input of a key of the table is rejected the same way, because DynamoDB cannot update a key.
    /// </para>
    /// <para>
    /// <b>Sparse indexes.</b> An index takes an item only with all its keys, so setting one key of a sparse index needs the other one in the same update, or sending throws <see cref="InvalidOperationException"/>
    /// naming it, because the item would silently stay out of the index. A key that an update cannot write, such as one made of an input of the table key, makes such an item enter the
    /// index by a <c>Put</c>, which writes every key. A key of literals, of any index, is no such key: it is written in the same <c>SET</c>, and removed with the other key.
    /// </para>
    /// <para>
    /// <b>Add and SetIfNotExists.</b> By the handle of such a property they throw too: the value they leave is not known here, so the key cannot follow. In a transaction the check runs
    /// when the action ends, at the next verb.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the attribute.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Set<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>;

    /// <summary>
    /// Sets an attribute of a value type from a nullable value, which is what a nullable property holds, or removes it when there is no value: a <see cref="Nullable{T}"/> with a value is
    /// <see cref="Set{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/> at the same cost, and without one it is <see cref="Remove{TValue, TConverter}(Attr{TValue, TConverter})"/>. The rules of a
    /// property that feeds a key are the ones of those two.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the attribute.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Set<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue>;

    /// <summary>Sets an attribute unless it exists (<c>SET a = if_not_exists(a, value)</c>).</summary>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The value.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf SetIfNotExists(string path, AttributeValue value);

    /// <summary>Sets an attribute unless it exists (<c>SET a = if_not_exists(a, value)</c>).</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf SetIfNotExists<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>;

    /// <summary>Adds to a number, or adds elements to a set (<c>ADD</c>). An attribute that does not exist starts from zero or the empty set.</summary>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The number or the set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Add(string path, AttributeValue value);

    /// <summary>Adds to a number, or adds elements to a set (<c>ADD</c>). An attribute that does not exist starts from zero or the empty set.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter, of a number or a set: <c>ADD</c> takes nothing else, so an attribute of another type is a compile error.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The number or the set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Add<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>, IAddableConverter;

    /// <summary>Removes elements from a set (<c>DELETE</c>). A set that ends up empty disappears, as DynamoDB has no empty sets.</summary>
    /// <param name="path">The name or document path.</param>
    /// <param name="elements">The elements, as a set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Delete(string path, AttributeValue elements);

    /// <summary>Removes elements from a set (<c>DELETE</c>). A set that ends up empty disappears, as DynamoDB has no empty sets.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="elements">The elements, as a set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Delete<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue elements)
        where TConverter : IDynamoDbConverter<TValue>;

    /// <summary>Removes an attribute (<c>REMOVE</c>), or an element of a list, which moves the elements after it. Removing what does not exist is no error.</summary>
    /// <param name="path">The name or document path.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Remove(string path);

    /// <summary>
    /// Removes an attribute (<c>REMOVE</c>), by its handle. Removing what does not exist is no error. A property that feeds a key of a sparse index takes the key attributes
    /// of that index with it, so that the item leaves the index; see <see cref="Set{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Remove<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue>;
}
