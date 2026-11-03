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
    /// <see cref="Remove{TValue, TConverter}(Attr{TValue, TConverter})"/>, for every attribute, because a generated entity writes no attribute for a null property either. On a
    /// property with <see cref="Mapping.OmitEmptyAttribute"/> an empty string or collection is one too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Key templates.</b> The handle of a property that feeds a key template keeps that key consistent, which a raw path does not. When the command ends, all inputs of the template must be known: then the
    /// derived attribute is written in the same <c>SET</c>, and otherwise sending throws <see cref="InvalidOperationException"/> naming the template and the <c>Set</c> calls to add, because the index
    /// would go stale. DynamoDB cannot build a key from what an item holds, so an input that does not change is set as well, to the value the item holds. An input of a key of the table is known from
    /// the key the update was given, and it cannot be set, because DynamoDB cannot update a key, so an index key that repeats it needs nothing more: that is the place for an input that never changes. A key given as strings is parsed
    /// for that, so a value that its text does not hold, such as a timestamp a key cuts off at the day, comes from a generated key or not at all: then use a <c>Put</c> of the changed entity,
    /// or a raw path. A write by handle to a property that a computed input is derived from, see <see cref="Mapping.ComputedFromAttribute"/>, also to a path inside it, throws in the same way, because no update can run a getter, and so does a write by handle to such a computed input itself.
    /// </para>
    /// <para>
    /// <b>Sparse indexes.</b> An index takes an item only with all its keys, so setting one key of a sparse index needs the other one in the same update, or sending throws <see cref="InvalidOperationException"/>
    /// naming it, because the item would silently stay out of the index. A key of literals, of any index, and a key made only of inputs of the table key are no such keys: they are
    /// written in the same <c>SET</c>, and removed with the other key.
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

    /// <summary>
    /// Sets an attribute if there is a value, and adds nothing if there is none, which is what an update of optional fields wants: <c>.SetIfNotNull(Customer.Attrs.Email, request.Email)</c>. With a value it is
    /// <see cref="Set{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>, with all it says about keys. With <see langword="null"/> the attribute stays as it is, where <c>Set</c> would remove it, and a
    /// property that feeds a key counts as untouched. On a property with <see cref="Mapping.OmitEmptyAttribute"/> an empty string or collection is no value either.
    /// </summary>
    /// <remarks>An update that ends up with no action is still an <c>UpdateItem</c>: it checks its conditions, creates the item if none has the key, and writes what the entity owes it, such as its discriminator.</remarks>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value, or <see langword="null"/> to leave the attribute alone.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf SetIfNotNull<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>;

    /// <summary>
    /// Sets an attribute of a value type if the nullable value has a value, and adds nothing if it has none. With a value it costs what
    /// <see cref="Set{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/> costs. See <see cref="SetIfNotNull{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value, or <see langword="null"/> to leave the attribute alone.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf SetIfNotNull<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
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
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, on a property with <see cref="Mapping.OmitEmptyAttribute"/>, which counts that as null, or anything else that <see cref="Serialization.IDynamoDbConverter{T}.IsOmitted"/> of the converter counts as null.</exception>
    public TSelf SetIfNotExists<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>;

    /// <summary>Adds to a number, or adds elements to a set (<c>ADD</c>). An attribute that does not exist starts from zero or the empty set.</summary>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The number or the set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Add(string path, AttributeValue value);

    /// <summary>
    /// Adds to a number, or adds elements to a set (<c>ADD</c>). An attribute that does not exist starts from zero or the empty set. A retry after a lost response repeats the addition to a number. <c>MaxAttempts(1)</c> makes it
    /// at most once, not exact: a lost response then reports a failure after the addition was applied. Only a condition, such as a version, makes it exact.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter, of a number or a set: <c>ADD</c> takes nothing else, so an attribute of another type is a compile error.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The number or the set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Add<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>, IAddableConverter;

    /// <summary>Adds to a nullable number (<c>ADD</c>), see <see cref="Add{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter, of a number: <c>ADD</c> takes nothing else, so an attribute of another type is a compile error.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The number.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Add<TValue, TConverter>(Attr<TValue, NullableHandleConverter<TValue, TConverter>> attribute, TValue value)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue>, IAddableConverter;

    /// <summary>Adds elements to a nullable set (<c>ADD</c>), see <see cref="Add{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter, of a set: <c>ADD</c> takes nothing else, so an attribute of another type is a compile error.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The set.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Add<TValue, TConverter>(Attr<TValue, NullableReferenceHandleConverter<TValue, TConverter>> attribute, TValue value)
        where TValue : class
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
