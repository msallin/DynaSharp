using System.Diagnostics.CodeAnalysis;
using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>
/// One change of a typed read of several entities: a <see cref="Change{T}"/> of one of the types, from <c>shop.ReadChangesAsync&lt;Customer, Order&gt;(streams, ...)</c>. Ask with
/// <c>change.Is(out Change&lt;Customer&gt;? customer)</c>, or handle every case with <c>Match</c>, which is what stays correct when a type is added.
/// </summary>
/// <remarks>
/// It is to <see cref="Change{T}"/> what <see cref="Mixed{T1, T2}"/> of a mixed read is to an entity, with the same members. That one is a document, which a read fills and <see cref="Item.From{T}"/> writes;
/// this one holds a change, which is neither. The default value holds nothing,
/// and <c>Match</c> throws on it; a typed read never gives one. A test builds one with the constructor of its type.
/// </remarks>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
public readonly struct MixedChange<T1, T2> : IChangeTypes<MixedChange<T1, T2>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
{
    private readonly int _type;
    private readonly object? _change;

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
        _change = change;
    }

    /// <inheritdoc/>
    static int IChangeTypes<MixedChange<T1, T2>>.FindType(Item? image) => ChangeTyping.FindType(Mixed<T1, T2>.Schema, image);

    /// <inheritdoc/>
    static MixedChange<T1, T2> IChangeTypes<MixedChange<T1, T2>>.Create(int index, ChangeRecord record, bool before, bool after) => index switch
    {
        0 => new(1, ChangeTyping.Create<T1>(record, before, after)),
        _ => new(2, ChangeTyping.Create<T2>(record, before, after)),
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

    /// <summary>Computes a result from the change, with one function per type, so no type can be forgotten.</summary>
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
            1 => first((Change<T1>)_change!),
            2 => second((Change<T2>)_change!),
            _ => throw Empty(),
        };
    }

    /// <summary>Does something with the change, with one action per type, so no type can be forgotten.</summary>
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
                first((Change<T1>)_change!);
                break;
            case 2:
                second((Change<T2>)_change!);
                break;
            default:
                throw Empty();
        }
    }

    private static InvalidOperationException Empty() =>
        new("This MixedChange is a default value, which holds nothing. Changes of a typed read always hold one of their types.");
}
