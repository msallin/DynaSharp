using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>One change of a typed read of 4 entities, a C# 15 union of their changes, as <see cref="MixedChange{T1, T2}"/> is of 2.</summary>
/// <inheritdoc cref="MixedChange{T1, T2}" path="/remarks"/>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
/// <typeparam name="T3">The third type.</typeparam>
/// <typeparam name="T4">The fourth type.</typeparam>
[Union]
public readonly struct MixedChange<T1, T2, T3, T4> : IChangeTypes<MixedChange<T1, T2, T3, T4>>, IEquatable<MixedChange<T1, T2, T3, T4>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
    where T3 : IDynamoDbDiscriminated<T3>
    where T4 : IDynamoDbDiscriminated<T4>
{
    private readonly int _type;

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the first type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice, Payment&gt;(new Change&lt;Customer&gt; { Kind = OperationType.Insert, After = customer, Record = new ChangeRecord() })</c>.
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
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the second type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice, Payment&gt;(new Change&lt;Order&gt; { Kind = OperationType.Insert, After = order, Record = new ChangeRecord() })</c>.
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

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the third type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice, Payment&gt;(new Change&lt;Invoice&gt; { Kind = OperationType.Insert, After = invoice, Record = new ChangeRecord() })</c>.
    /// </summary>
    /// <remarks>
    /// There is one constructor per type, and the type of the argument picks it. Two equal types make the call ambiguous, which does not compile, and a reading of them could not tell
    /// them apart either: it throws <see cref="ArgumentException"/>.
    /// </remarks>
    /// <param name="change">The change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
    public MixedChange(Change<T3> change)
        : this(3, change ?? throw new ArgumentNullException(nameof(change)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the fourth type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice, Payment&gt;(new Change&lt;Payment&gt; { Kind = OperationType.Insert, After = payment, Record = new ChangeRecord() })</c>.
    /// </summary>
    /// <remarks>
    /// There is one constructor per type, and the type of the argument picks it. Two equal types make the call ambiguous, which does not compile, and a reading of them could not tell
    /// them apart either: it throws <see cref="ArgumentException"/>.
    /// </remarks>
    /// <param name="change">The change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
    public MixedChange(Change<T4> change)
        : this(4, change ?? throw new ArgumentNullException(nameof(change)))
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
    public static bool operator ==(MixedChange<T1, T2, T3, T4> left, MixedChange<T1, T2, T3, T4> right) => left.Equals(right);

    /// <summary>Tells whether two values hold changes of different types, or changes that are not equal, or only one of them is the default value.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are not equal.</returns>
    public static bool operator !=(MixedChange<T1, T2, T3, T4> left, MixedChange<T1, T2, T3, T4> right) => !left.Equals(right);

    /// <inheritdoc/>
    static int IChangeTypes<MixedChange<T1, T2, T3, T4>>.FindType(Item? image) => ChangeTyping.FindType(Mixed<T1, T2, T3, T4>.Schema, image);

    /// <inheritdoc/>
    static MixedChange<T1, T2, T3, T4> IChangeTypes<MixedChange<T1, T2, T3, T4>>.Create(int index, ChangeRecord record, bool before, bool after) => index switch
    {
        0 => new(1, ChangeTyping.Create<T1>(record, before, after)),
        1 => new(2, ChangeTyping.Create<T2>(record, before, after)),
        2 => new(3, ChangeTyping.Create<T3>(record, before, after)),
        _ => new(4, ChangeTyping.Create<T4>(record, before, after)),
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

    /// <summary>Gets the change if it is of the third type, which is what a type pattern of its <see cref="Change{T}"/> asks in C# 15.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool TryGetValue([NotNullWhen(true)] out Change<T3>? change)
    {
        change = _type == 3 ? (Change<T3>)Value! : null;
        return change is not null;
    }

    /// <summary>Gets the change if it is of the fourth type, which is what a type pattern of its <see cref="Change{T}"/> asks in C# 15.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool TryGetValue([NotNullWhen(true)] out Change<T4>? change)
    {
        change = _type == 4 ? (Change<T4>)Value! : null;
        return change is not null;
    }

    /// <summary>Computes a result from the change, with one function per type, so no type can be forgotten: the form of a <c>switch</c> over the change for C# 14.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="first">What to do with a change of the first type.</param>
    /// <param name="second">What to do with a change of the second type.</param>
    /// <param name="third">What to do with a change of the third type.</param>
    /// <param name="fourth">What to do with a change of the fourth type.</param>
    /// <returns>The result of the function of the type of the change.</returns>
    /// <exception cref="ArgumentNullException">A function is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public TResult Match<TResult>(Func<Change<T1>, TResult> first, Func<Change<T2>, TResult> second, Func<Change<T3>, TResult> third, Func<Change<T4>, TResult> fourth)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(fourth);
        return _type switch
        {
            1 => first((Change<T1>)Value!),
            2 => second((Change<T2>)Value!),
            3 => third((Change<T3>)Value!),
            4 => fourth((Change<T4>)Value!),
            _ => throw Empty(),
        };
    }

    /// <summary>Does something with the change, with one action per type, so no type can be forgotten: the form of a <c>switch</c> over the change for C# 14.</summary>
    /// <param name="first">What to do with a change of the first type.</param>
    /// <param name="second">What to do with a change of the second type.</param>
    /// <param name="third">What to do with a change of the third type.</param>
    /// <param name="fourth">What to do with a change of the fourth type.</param>
    /// <exception cref="ArgumentNullException">An action is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public void Match(Action<Change<T1>> first, Action<Change<T2>> second, Action<Change<T3>> third, Action<Change<T4>> fourth)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(fourth);
        switch (_type)
        {
            case 1:
                first((Change<T1>)Value!);
                break;
            case 2:
                second((Change<T2>)Value!);
                break;
            case 3:
                third((Change<T3>)Value!);
                break;
            case 4:
                fourth((Change<T4>)Value!);
                break;
            default:
                throw Empty();
        }
    }

    /// <summary>Tells whether this value holds a change of the same type as <paramref name="other"/> and an equal one, by the value equality of <see cref="Change{T}"/>; two default values are equal.</summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public bool Equals(MixedChange<T1, T2, T3, T4> other) => _type == other._type && Equals(Value, other.Value);

    /// <summary>Tells whether <paramref name="obj"/> is a <see cref="MixedChange{T1, T2, T3, T4}"/> equal to this one.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if it is an equal value.</returns>
    public override bool Equals(object? obj) => obj is MixedChange<T1, T2, T3, T4> other && Equals(other);

    /// <summary>Gets a hash code from the type of the change and the change.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_type, Value);

    /// <summary>Gets the text of the change it holds, its own <c>ToString()</c>.</summary>
    /// <returns>The text of the change, which holds its values; empty for the default value, as for a <see cref="Nullable{T}"/> without a value.</returns>
    public override string ToString() => _type == 0 ? string.Empty : Value!.ToString()!;

    private static InvalidOperationException Empty() =>
        new("This MixedChange is a default value, which holds nothing. Changes of a typed read always hold one of their types.");
}
