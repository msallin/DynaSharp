using System.Diagnostics.CodeAnalysis;
using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>
/// One change of a typed reading of several entities: a <see cref="Change{T}"/> of one of the types, from <c>shop.ReadChangesAsync&lt;Customer, Order&gt;(streams, ...)</c>. Ask with
/// <c>change.Is(out Change&lt;Customer&gt;? customer)</c>, or handle every case with <c>Match</c>, which is what stays correct when a type is added.
/// </summary>
/// <remarks>
/// It has the shape of <see cref="Mixed{T1, T2, T3, T4}"/>, which cannot hold a change: its types must be entities, so that a mixed read can read them. The default value holds nothing,
/// and <c>Match</c> throws on it; a typed reading never gives one. A test builds one with the constructor of its type.
/// </remarks>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
/// <typeparam name="T3">The third type.</typeparam>
/// <typeparam name="T4">The fourth type.</typeparam>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "A change of a reading is handled, not compared, as a Mixed of a mixed read is.")]
public readonly struct MixedChange<T1, T2, T3, T4> : IChangeTypes<MixedChange<T1, T2, T3, T4>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
    where T3 : IDynamoDbDiscriminated<T3>
    where T4 : IDynamoDbDiscriminated<T4>
{
    private readonly int _type;
    private readonly object? _change;

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the first type, as a typed reading gives it, so that a test of code that
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
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the second type, as a typed reading gives it, so that a test of code that
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
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the third type, as a typed reading gives it, so that a test of code that
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
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3, T4}"/> struct that holds a change of the fourth type, as a typed reading gives it, so that a test of code that
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
        _change = change;
    }

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

    /// <summary>Gets the change if it is of the first type.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool Is([NotNullWhen(true)] out Change<T1>? change)
    {
        change = _type == 1 ? (Change<T1>)_change! : null;
        return change is not null;
    }

    /// <summary>Gets the change if it is of the second type.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool Is([NotNullWhen(true)] out Change<T2>? change)
    {
        change = _type == 2 ? (Change<T2>)_change! : null;
        return change is not null;
    }

    /// <summary>Gets the change if it is of the third type.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool Is([NotNullWhen(true)] out Change<T3>? change)
    {
        change = _type == 3 ? (Change<T3>)_change! : null;
        return change is not null;
    }

    /// <summary>Gets the change if it is of the fourth type.</summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the change is of this type.</returns>
    public bool Is([NotNullWhen(true)] out Change<T4>? change)
    {
        change = _type == 4 ? (Change<T4>)_change! : null;
        return change is not null;
    }

    /// <summary>Computes a result from the change, with one function per type, so no type can be forgotten.</summary>
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
            1 => first((Change<T1>)_change!),
            2 => second((Change<T2>)_change!),
            3 => third((Change<T3>)_change!),
            4 => fourth((Change<T4>)_change!),
            _ => throw Empty(),
        };
    }

    /// <summary>Does something with the change, with one action per type, so no type can be forgotten.</summary>
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
                first((Change<T1>)_change!);
                break;
            case 2:
                second((Change<T2>)_change!);
                break;
            case 3:
                third((Change<T3>)_change!);
                break;
            case 4:
                fourth((Change<T4>)_change!);
                break;
            default:
                throw Empty();
        }
    }

    private static InvalidOperationException Empty() =>
        new("This MixedChange is a default value, which holds nothing. Changes of a typed reading always hold one of their types.");
}
