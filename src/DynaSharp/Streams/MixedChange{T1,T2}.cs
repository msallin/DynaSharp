using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>
/// One change of a typed read of several entities: a <see cref="Change{T}"/> of one of the types, from <c>shop.ReadChangesAsync&lt;Customer, Order&gt;(streams, ...)</c>. It is a
/// C# 15 union of the changes, so a <c>switch</c> over it handles every type and the compiler warns when one is missing; in C# 14, <c>Match</c> does the same.
/// </summary>
/// <example>
/// <code>
/// await foreach (var change in shop.ReadChangesAsync&lt;Customer, Order&gt;(streams, ShardIteratorType.Latest, cancellationToken: ct))
/// {
///     Console.WriteLine(change switch
///     {
///         Change&lt;Customer&gt; profile => $"customer {profile.Kind}",
///         Change&lt;Order&gt; placed => $"order {placed.Kind} {placed.After?.Total}",
///     });
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>A union.</b> It is to <see cref="Change{T}"/> what <see cref="Mixed{T1, T2}"/> of a mixed read is to an entity, with the same members: a <c>switch</c> names each
/// <c>Change&lt;T&gt;</c> and warns with <c>CS8509</c> when one is missing, <c>change is Change&lt;Order&gt; order</c> and <c>TryGetValue(out Change&lt;Order&gt;? order)</c> ask for
/// one, and <c>Match</c> is the exhaustive form for C# 14. <c>Mixed</c> is a document, which a read fills and <see cref="Item.From{T}"/> writes; this one holds a change, which is neither.
/// </para>
/// <para>
/// <b>The default value holds nothing.</b> It has <see cref="HasValue"/> <see langword="false"/> and a <see cref="Value"/> of <see langword="null"/>, so it matches the
/// <see langword="null"/> pattern; a <c>switch</c> without a <see langword="null"/> arm throws <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/> on it, and
/// <c>Match</c> throws <see cref="InvalidOperationException"/>. A typed read never gives one.
/// </para>
/// <para>
/// <b>Equality and text.</b> Two values are equal when they hold changes of the same type that are equal, as records, and two default values are equal.
/// <c>ToString()</c> is the text of the change, and empty for the default value.
/// </para>
/// <para>
/// <b>Built by a test.</b> A test builds one with the constructor of its type, or in C# 15 by converting the change: <c>MixedChange&lt;Customer, Order&gt; change = new Change&lt;Order&gt; { ... };</c>.
/// Two equal types make that call ambiguous, which does not compile, and a reading of them could not tell them apart either: it throws <see cref="ArgumentException"/>.
/// </para>
/// </remarks>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
[Union]
public readonly struct MixedChange<T1, T2> : IChangeTypes<MixedChange<T1, T2>>, IEquatable<MixedChange<T1, T2>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
{
    private readonly int _type;

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2}"/> struct that holds a change of the first type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order&gt;(new Change&lt;Customer&gt; { Kind = OperationType.Insert, After = customer, Record = new ChangeRecord() })</c>.
    /// </summary>
    /// <remarks>
    /// There is one constructor per type, and the type of the argument picks it. Two equal types make the call ambiguous, which does not compile, and a reading of them could not tell
    /// them apart either: it throws <see cref="ArgumentException"/>.
    /// </remarks>
    /// <param name="change">The change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
    public MixedChange(Change<T1> change)
        : this(1, change ?? throw new ArgumentNullException(nameof(change)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2}"/> struct that holds a change of the second type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order&gt;(new Change&lt;Order&gt; { Kind = OperationType.Insert, After = order, Record = new ChangeRecord() })</c>.
    /// </summary>
    /// <remarks>
    /// There is one constructor per type, and the type of the argument picks it. Two equal types make the call ambiguous, which does not compile, and a reading of them could not tell
    /// them apart either: it throws <see cref="ArgumentException"/>.
    /// </remarks>
    /// <param name="change">The change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
    public MixedChange(Change<T2> change)
        : this(2, change ?? throw new ArgumentNullException(nameof(change)))
    {
    }

    private MixedChange(int type, object change)
    {
        _type = type;
        Value = change;
    }

    /// <summary>Gets the change it holds, or <see langword="null"/> for the default value.</summary>
    /// <value>The <see cref="Change{T}"/>, as an <see cref="object"/>.</value>
    public object? Value { get; }

    /// <summary>Gets a value indicating whether it holds a change, which is <see langword="false"/> only for the default value.</summary>
    /// <value><see langword="true"/> if it holds a change.</value>
    public bool HasValue => _type != 0;

    /// <summary>Tells whether two values hold changes of the same type that are equal, or are both the default value.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public static bool operator ==(MixedChange<T1, T2> left, MixedChange<T1, T2> right) => left.Equals(right);

    /// <summary>Tells whether two values hold changes of different types, or changes that are not equal, or only one of them is the default value.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are not equal.</returns>
    public static bool operator !=(MixedChange<T1, T2> left, MixedChange<T1, T2> right) => !left.Equals(right);

    /// <inheritdoc/>
    static int IChangeTypes<MixedChange<T1, T2>>.FindType(Item? image) => ChangeTyping.FindType(Mixed<T1, T2>.Schema, image);

    /// <inheritdoc/>
    static MixedChange<T1, T2> IChangeTypes<MixedChange<T1, T2>>.Create(int index, ChangeRecord record, bool before, bool after) => index switch
    {
        0 => new(1, ChangeTyping.Create<T1>(record, before, after)),
        _ => new(2, ChangeTyping.Create<T2>(record, before, after)),
    };

    /// <summary>Gets the change if it is of the first type, which is what a type pattern of its <see cref="Change{T}"/> asks in C# 15.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool TryGetValue([NotNullWhen(true)] out Change<T1>? change)
    {
        change = _type == 1 ? (Change<T1>)Value! : null;
        return change is not null;
    }

    /// <summary>Gets the change if it is of the second type, which is what a type pattern of its <see cref="Change{T}"/> asks in C# 15.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool TryGetValue([NotNullWhen(true)] out Change<T2>? change)
    {
        change = _type == 2 ? (Change<T2>)Value! : null;
        return change is not null;
    }

    /// <summary>Computes a result from the change, with one function per type, so no type can be forgotten: the form of a <c>switch</c> over the change for C# 14.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="first">What to do with a change of the first type.</param>
    /// <param name="second">What to do with a change of the second type.</param>
    /// <returns>The result of the function of the type of the change.</returns>
    /// <exception cref="ArgumentNullException">A function is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public TResult Match<TResult>(Func<Change<T1>, TResult> first, Func<Change<T2>, TResult> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return _type switch
        {
            1 => first((Change<T1>)Value!),
            2 => second((Change<T2>)Value!),
            _ => throw Empty(),
        };
    }

    /// <summary>Does something with the change, with one action per type, so no type can be forgotten: the form of a <c>switch</c> over the change for C# 14.</summary>
    /// <param name="first">What to do with a change of the first type.</param>
    /// <param name="second">What to do with a change of the second type.</param>
    /// <exception cref="ArgumentNullException">An action is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public void Match(Action<Change<T1>> first, Action<Change<T2>> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        switch (_type)
        {
            case 1:
                first((Change<T1>)Value!);
                break;
            case 2:
                second((Change<T2>)Value!);
                break;
            default:
                throw Empty();
        }
    }

    /// <summary>Tells whether this value holds a change of the same type as <paramref name="other"/> and an equal one, by the value equality of <see cref="Change{T}"/>; two default values are equal.</summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public bool Equals(MixedChange<T1, T2> other) => _type == other._type && Equals(Value, other.Value);

    /// <summary>Tells whether <paramref name="obj"/> is a <see cref="MixedChange{T1, T2}"/> equal to this one.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if it is an equal value.</returns>
    public override bool Equals(object? obj) => obj is MixedChange<T1, T2> other && Equals(other);

    /// <summary>Gets a hash code from the type of the change and the change.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_type, Value);

    /// <summary>Gets the text of the change it holds, its own <c>ToString()</c>.</summary>
    /// <returns>The text of the change, which holds its values; empty for the default value, as for a <see cref="Nullable{T}"/> without a value.</returns>
    public override string ToString() => _type == 0 ? string.Empty : Value!.ToString()!;

    private static InvalidOperationException Empty() =>
        new("This MixedChange is a default value, which holds nothing. Changes of a typed read always hold one of their types.");
}
