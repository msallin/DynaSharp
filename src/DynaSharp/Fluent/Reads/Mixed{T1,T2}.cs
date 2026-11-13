using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// One item of a mixed read: an item of one of several types, from <c>shop.Query(pk).Of&lt;Customer, Order&gt;()</c>. It is a C# 15 union of its types, so a <c>switch</c> over
/// it handles every type and the compiler warns when one is missing; in C# 14, <c>Match</c> does the same.
/// </summary>
/// <example>
/// <code>
/// await foreach (var item in shop.Query(Order.Keys.Pk.Of("alice")).Of&lt;Customer, Order&gt;().ItemsAsync(ct))
/// {
///     Console.WriteLine(item switch
///     {
///         Customer profile => profile.Name,
///         Order placed => $"#{placed.Number}",
///     });
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>A union.</b> A <c>switch</c> names each type, <c>item switch { Customer c => ..., Order o => ... }</c>, and warns with <c>CS8509</c> when one is missing, also after a type is
/// added to <c>Of&lt;...&gt;()</c>. <c>item is Order order</c> asks for one type, and so does <c>TryGetValue(out Order? order)</c>, which C# 14 calls directly. <c>Match</c> takes one
/// function per type and is the exhaustive form for C# 14, or where a function is at hand.
/// </para>
/// <para>
/// <b>The default value holds nothing.</b> <c>default(Mixed&lt;Customer, Order&gt;)</c> has <see cref="HasValue"/> <see langword="false"/> and a <see cref="Value"/> of
/// <see langword="null"/>, so it matches the <see langword="null"/> pattern; a <c>switch</c> without a <see langword="null"/> arm throws <see cref="SwitchExpressionException"/> on it,
/// and <c>Match</c> throws <see cref="InvalidOperationException"/>. A mixed read never gives one.
/// </para>
/// <para>
/// <b>Equality and text.</b> Two values are equal when they hold items of the same type that are equal by the <c>Equals</c> of that type, which compares the values
/// of a <c>record</c>, and two default values are equal. <c>ToString()</c> is the text of the item, and empty for the default value.
/// </para>
/// <para>
/// <b>Two equal types.</b> There is one constructor per type, and the type of the argument picks it, so <c>Mixed&lt;Customer, Customer&gt;</c> makes the call ambiguous, which
/// does not compile; a read of it could not tell the items apart either, and throws <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// <b>A document.</b> It writes the attributes of the item it holds, so <see cref="Item.From{T}"/> works on it.
/// </para>
/// </remarks>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
[Union]
public readonly struct Mixed<T1, T2> : IMixed<Mixed<T1, T2>>, IEquatable<Mixed<T1, T2>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
{
    /// <summary>What this mix knows about its types.</summary>
    internal static readonly MixedSchema Schema = new(T1.Info, T2.Info);

    private readonly int _type;
    private readonly T1 _first;
    private readonly T2 _second;

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixed{T1, T2}"/> struct that holds an item of the first type, as a mixed read gives it, so that a test of code that
    /// handles the items of a read builds its input: <c>new Mixed&lt;Customer, Order&gt;(customer)</c>.
    /// </summary>
    /// <remarks>In C# 15 the item converts to the union as well: <c>Mixed&lt;Customer, Order&gt; item = customer;</c>.</remarks>
    /// <param name="item">The item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Mixed(T1 item)
        : this(1, MixedSchema.Required(item, nameof(item)), default!)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixed{T1, T2}"/> struct that holds an item of the second type, as a mixed read gives it, so that a test of code that
    /// handles the items of a read builds its input: <c>new Mixed&lt;Customer, Order&gt;(order)</c>.
    /// </summary>
    /// <remarks>In C# 15 the item converts to the union as well: <c>Mixed&lt;Customer, Order&gt; item = order;</c>.</remarks>
    /// <param name="item">The item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Mixed(T2 item)
        : this(2, default!, MixedSchema.Required(item, nameof(item)))
    {
    }

    private Mixed(int type, T1 first, T2 second)
    {
        _type = type;
        _first = first;
        _second = second;
    }

    static MixedSchema IMixed<Mixed<T1, T2>>.Schema => Schema;

    /// <summary>Gets the item it holds, or <see langword="null"/> for the default value.</summary>
    /// <value>The item, as an <see cref="object"/>.</value>
    public object? Value => _type switch
    {
        1 => _first,
        2 => _second,
        _ => null,
    };

    /// <summary>Gets a value indicating whether it holds an item, which is <see langword="false"/> only for the default value.</summary>
    /// <value><see langword="true"/> if it holds an item.</value>
    public bool HasValue => _type != 0;

    /// <summary>Tells whether two values hold items of the same type that are equal, or are both the default value.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public static bool operator ==(Mixed<T1, T2> left, Mixed<T1, T2> right) => left.Equals(right);

    /// <summary>Tells whether two values hold items of different types, or items that are not equal, or only one of them is the default value.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are not equal.</returns>
    public static bool operator !=(Mixed<T1, T2> left, Mixed<T1, T2> right) => !left.Equals(right);

    static void IDynamoDbDocument<Mixed<T1, T2>>.WriteAttributes(DynamoDbWriter writer, Mixed<T1, T2> entity)
    {
        switch (entity._type)
        {
            case 1:
                T1.WriteAttributes(writer, entity._first);
                break;
            case 2:
                T2.WriteAttributes(writer, entity._second);
                break;
            default:
                throw MixedSchema.Empty();
        }
    }

    static Mixed<T1, T2> IDynamoDbDocument<Mixed<T1, T2>>.ReadAttributes(ref DynamoDbReader reader) => Schema.FindType(reader) switch
    {
        0 => new(1, T1.ReadAttributes(ref reader), default!),
        _ => new(2, default!, T2.ReadAttributes(ref reader)),
    };

    /// <summary>Gets the item if it is of the first type, which is what the pattern <c>item is Customer customer</c> asks in C# 15.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T1 value)
    {
        value = _first;
        return _type == 1;
    }

    /// <summary>Gets the item if it is of the second type, which is what the pattern <c>item is Order order</c> asks in C# 15.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T2 value)
    {
        value = _second;
        return _type == 2;
    }

    /// <summary>Computes a result from the item, with one function per type, so no type can be forgotten: the form of a <c>switch</c> over the item for C# 14.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="first">What to do with an item of the first type.</param>
    /// <param name="second">What to do with an item of the second type.</param>
    /// <returns>The result of the function of the type of the item.</returns>
    /// <exception cref="ArgumentNullException">A function is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public TResult Match<TResult>(Func<T1, TResult> first, Func<T2, TResult> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return _type switch
        {
            1 => first(_first),
            2 => second(_second),
            _ => throw MixedSchema.Empty(),
        };
    }

    /// <summary>Does something with the item, with one action per type, so no type can be forgotten: the form of a <c>switch</c> over the item for C# 14.</summary>
    /// <param name="first">What to do with an item of the first type.</param>
    /// <param name="second">What to do with an item of the second type.</param>
    /// <exception cref="ArgumentNullException">An action is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public void Match(Action<T1> first, Action<T2> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        switch (_type)
        {
            case 1:
                first(_first);
                break;
            case 2:
                second(_second);
                break;
            default:
                throw MixedSchema.Empty();
        }
    }

    /// <summary>Tells whether this value holds an item of the same type as <paramref name="other"/> and an equal one, by the <c>Equals</c> of the item's type, such as the value equality of a <c>record</c>; two default values are equal.</summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public bool Equals(Mixed<T1, T2> other) => _type == other._type && _type switch
    {
        1 => EqualityComparer<T1>.Default.Equals(_first, other._first),
        2 => EqualityComparer<T2>.Default.Equals(_second, other._second),
        _ => true,
    };

    /// <summary>Tells whether <paramref name="obj"/> is a <see cref="Mixed{T1, T2}"/> equal to this one.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if it is an equal value.</returns>
    public override bool Equals(object? obj) => obj is Mixed<T1, T2> other && Equals(other);

    /// <summary>Gets a hash code from the type of the item and the item.</summary>
    /// <returns>The hash code, 0 for the default value.</returns>
    public override int GetHashCode() => _type switch
    {
        1 => HashCode.Combine(1, _first),
        2 => HashCode.Combine(2, _second),
        _ => 0,
    };

    /// <summary>Gets the text of the item it holds, its own <c>ToString()</c>.</summary>
    /// <returns>The text of the item, which holds its values; empty for the default value, as for a <see cref="Nullable{T}"/> without a value.</returns>
    public override string ToString() => _type switch
    {
        1 => _first.ToString()!,
        2 => _second.ToString()!,
        _ => string.Empty,
    };
}
