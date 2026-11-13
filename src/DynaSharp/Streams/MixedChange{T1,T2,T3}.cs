using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>One change of a typed read of 3 entities, a C# 15 union of their changes, as <see cref="MixedChange{T1, T2}"/> is of 2.</summary>
/// <inheritdoc cref="MixedChange{T1, T2}" path="/remarks"/>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
/// <typeparam name="T3">The third type.</typeparam>
[Union]
public readonly struct MixedChange<T1, T2, T3> : IChangeTypes<MixedChange<T1, T2, T3>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
    where T3 : IDynamoDbDiscriminated<T3>
{
    private readonly int _type;

    /// <summary>
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3}"/> struct that holds a change of the first type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice&gt;(new Change&lt;Customer&gt; { Kind = OperationType.Insert, After = customer, Record = new ChangeRecord() })</c>.
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
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3}"/> struct that holds a change of the second type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice&gt;(new Change&lt;Order&gt; { Kind = OperationType.Insert, After = order, Record = new ChangeRecord() })</c>.
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
    /// Initializes a new instance of the <see cref="MixedChange{T1, T2, T3}"/> struct that holds a change of the third type, as a typed read gives it, so that a test of code that
    /// handles the changes of a reading builds its input: <c>new MixedChange&lt;Customer, Order, Invoice&gt;(new Change&lt;Invoice&gt; { Kind = OperationType.Insert, After = invoice, Record = new ChangeRecord() })</c>.
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

    /// <inheritdoc/>
    static int IChangeTypes<MixedChange<T1, T2, T3>>.FindType(Item? image) => ChangeTyping.FindType(Mixed<T1, T2, T3>.Schema, image);

    /// <inheritdoc/>
    static MixedChange<T1, T2, T3> IChangeTypes<MixedChange<T1, T2, T3>>.Create(int index, ChangeRecord record, bool before, bool after) => index switch
    {
        0 => new(1, ChangeTyping.Create<T1>(record, before, after)),
        1 => new(2, ChangeTyping.Create<T2>(record, before, after)),
        _ => new(3, ChangeTyping.Create<T3>(record, before, after)),
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

    /// <summary>Computes a result from the change, with one function per type, so no type can be forgotten: the form of a <c>switch</c> over the change for C# 14.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="first">What to do with a change of the first type.</param>
    /// <param name="second">What to do with a change of the second type.</param>
    /// <param name="third">What to do with a change of the third type.</param>
    /// <returns>The result of the function of the type of the change.</returns>
    /// <exception cref="ArgumentNullException">A function is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public TResult Match<TResult>(Func<Change<T1>, TResult> first, Func<Change<T2>, TResult> second, Func<Change<T3>, TResult> third)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        return _type switch
        {
            1 => first((Change<T1>)Value!),
            2 => second((Change<T2>)Value!),
            3 => third((Change<T3>)Value!),
            _ => throw Empty(),
        };
    }

    /// <summary>Does something with the change, with one action per type, so no type can be forgotten: the form of a <c>switch</c> over the change for C# 14.</summary>
    /// <param name="first">What to do with a change of the first type.</param>
    /// <param name="second">What to do with a change of the second type.</param>
    /// <param name="third">What to do with a change of the third type.</param>
    /// <exception cref="ArgumentNullException">An action is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public void Match(Action<Change<T1>> first, Action<Change<T2>> second, Action<Change<T3>> third)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
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
            default:
                throw Empty();
        }
    }

    private static InvalidOperationException Empty() =>
        new("This MixedChange is a default value, which holds nothing. Changes of a typed read always hold one of their types.");
}
