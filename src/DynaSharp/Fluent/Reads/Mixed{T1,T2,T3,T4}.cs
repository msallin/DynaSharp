using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>One item of a mixed read of 4 types, a C# 15 union of them, as <see cref="Mixed{T1, T2}"/> is of 2.</summary>
/// <inheritdoc cref="Mixed{T1, T2}" path="/remarks"/>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
/// <typeparam name="T3">The third type.</typeparam>
/// <typeparam name="T4">The fourth type.</typeparam>
[Union]
public readonly struct Mixed<T1, T2, T3, T4> : IMixed<Mixed<T1, T2, T3, T4>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
    where T3 : IDynamoDbDiscriminated<T3>
    where T4 : IDynamoDbDiscriminated<T4>
{
    /// <summary>What this mix knows about its types.</summary>
    internal static readonly MixedSchema Schema = new(T1.Info, T2.Info, T3.Info, T4.Info);

    private readonly int _type;
    private readonly T1 _first;
    private readonly T2 _second;
    private readonly T3 _third;
    private readonly T4 _fourth;

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixed{T1, T2, T3, T4}"/> struct that holds an item of the first type, as a mixed read gives it, so that a test of code that
    /// handles the items of a read builds its input.
    /// </summary>
    /// <remarks>In C# 15 the item converts to the union as well.</remarks>
    /// <param name="item">The item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Mixed(T1 item)
        : this(1, MixedSchema.Required(item, nameof(item)), default!, default!, default!)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixed{T1, T2, T3, T4}"/> struct that holds an item of the second type, as a mixed read gives it, so that a test of code that
    /// handles the items of a read builds its input.
    /// </summary>
    /// <remarks>In C# 15 the item converts to the union as well.</remarks>
    /// <param name="item">The item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Mixed(T2 item)
        : this(2, default!, MixedSchema.Required(item, nameof(item)), default!, default!)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixed{T1, T2, T3, T4}"/> struct that holds an item of the third type, as a mixed read gives it, so that a test of code that
    /// handles the items of a read builds its input.
    /// </summary>
    /// <remarks>In C# 15 the item converts to the union as well.</remarks>
    /// <param name="item">The item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Mixed(T3 item)
        : this(3, default!, default!, MixedSchema.Required(item, nameof(item)), default!)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixed{T1, T2, T3, T4}"/> struct that holds an item of the fourth type, as a mixed read gives it, so that a test of code that
    /// handles the items of a read builds its input.
    /// </summary>
    /// <remarks>In C# 15 the item converts to the union as well.</remarks>
    /// <param name="item">The item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Mixed(T4 item)
        : this(4, default!, default!, default!, MixedSchema.Required(item, nameof(item)))
    {
    }

    private Mixed(int type, T1 first, T2 second, T3 third, T4 fourth)
    {
        _type = type;
        _first = first;
        _second = second;
        _third = third;
        _fourth = fourth;
    }

    static MixedSchema IMixed<Mixed<T1, T2, T3, T4>>.Schema => Schema;

    /// <summary>Gets the item it holds, or <see langword="null"/> for the default value.</summary>
    /// <value>The item, as an <see cref="object"/>.</value>
    public object? Value => _type switch
    {
        1 => _first,
        2 => _second,
        3 => _third,
        4 => _fourth,
        _ => null,
    };

    /// <summary>Gets a value indicating whether it holds an item, which is <see langword="false"/> only for the default value.</summary>
    /// <value><see langword="true"/> if it holds an item.</value>
    public bool HasValue => _type != 0;

    static void IDynamoDbDocument<Mixed<T1, T2, T3, T4>>.WriteAttributes(DynamoDbWriter writer, Mixed<T1, T2, T3, T4> entity)
    {
        switch (entity._type)
        {
            case 1:
                T1.WriteAttributes(writer, entity._first);
                break;
            case 2:
                T2.WriteAttributes(writer, entity._second);
                break;
            case 3:
                T3.WriteAttributes(writer, entity._third);
                break;
            case 4:
                T4.WriteAttributes(writer, entity._fourth);
                break;
            default:
                throw MixedSchema.Empty();
        }
    }

    static Mixed<T1, T2, T3, T4> IDynamoDbDocument<Mixed<T1, T2, T3, T4>>.ReadAttributes(ref DynamoDbReader reader) => Schema.FindType(reader) switch
    {
        0 => new(1, T1.ReadAttributes(ref reader), default!, default!, default!),
        1 => new(2, default!, T2.ReadAttributes(ref reader), default!, default!),
        2 => new(3, default!, default!, T3.ReadAttributes(ref reader), default!),
        _ => new(4, default!, default!, default!, T4.ReadAttributes(ref reader)),
    };

    /// <summary>Gets the item if it is of the first type, which is what a type pattern of it asks in C# 15.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T1 value)
    {
        value = _first;
        return _type == 1;
    }

    /// <summary>Gets the item if it is of the second type, which is what a type pattern of it asks in C# 15.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T2 value)
    {
        value = _second;
        return _type == 2;
    }

    /// <summary>Gets the item if it is of the third type, which is what a type pattern of it asks in C# 15.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T3 value)
    {
        value = _third;
        return _type == 3;
    }

    /// <summary>Gets the item if it is of the fourth type, which is what a type pattern of it asks in C# 15.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T4 value)
    {
        value = _fourth;
        return _type == 4;
    }

    /// <summary>Computes a result from the item, with one function per type, so no type can be forgotten: the form of a <c>switch</c> over the item for C# 14.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="first">What to do with an item of the first type.</param>
    /// <param name="second">What to do with an item of the second type.</param>
    /// <param name="third">What to do with an item of the third type.</param>
    /// <param name="fourth">What to do with an item of the fourth type.</param>
    /// <returns>The result of the function of the type of the item.</returns>
    /// <exception cref="ArgumentNullException">A function is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public TResult Match<TResult>(Func<T1, TResult> first, Func<T2, TResult> second, Func<T3, TResult> third, Func<T4, TResult> fourth)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(fourth);
        return _type switch
        {
            1 => first(_first),
            2 => second(_second),
            3 => third(_third),
            4 => fourth(_fourth),
            _ => throw MixedSchema.Empty(),
        };
    }

    /// <summary>Does something with the item, with one action per type, so no type can be forgotten: the form of a <c>switch</c> over the item for C# 14.</summary>
    /// <param name="first">What to do with an item of the first type.</param>
    /// <param name="second">What to do with an item of the second type.</param>
    /// <param name="third">What to do with an item of the third type.</param>
    /// <param name="fourth">What to do with an item of the fourth type.</param>
    /// <exception cref="ArgumentNullException">An action is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is the default value, which holds nothing.</exception>
    public void Match(Action<T1> first, Action<T2> second, Action<T3> third, Action<T4> fourth)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(fourth);
        switch (_type)
        {
            case 1:
                first(_first);
                break;
            case 2:
                second(_second);
                break;
            case 3:
                third(_third);
                break;
            case 4:
                fourth(_fourth);
                break;
            default:
                throw MixedSchema.Empty();
        }
    }
}
