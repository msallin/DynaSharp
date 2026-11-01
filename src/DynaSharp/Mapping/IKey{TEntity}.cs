using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Mapping;

/// <summary>
/// A primary key of <typeparamref name="TEntity"/>. <c>customers.Get(Customer.Keys.Of("alice"))</c> infers the key type from the argument and
/// checks it against the table, so a key of another entity is a compile error.
/// </summary>
/// <typeparam name="TEntity">The entity the key belongs to.</typeparam>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "The type parameter is the point: it ties a key to its entity at compile time.")]
public interface IKey<TEntity> : IKey
    where TEntity : IDynamoDbDocument<TEntity>;
