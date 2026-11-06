using System.Diagnostics.CodeAnalysis;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// One item of a mixed read: an item of one of several types, from <c>shop.Query(pk).Of&lt;Customer, Order&gt;()</c>. Ask with <c>item.Is(out Customer customer)</c>, or
/// handle every case with <see cref="Match{TResult}"/>, which is what stays correct when a type is added.
/// </summary>
/// <remarks>
/// The default value holds nothing, and <see cref="Match{TResult}"/> throws on it; a mixed read
/// never gives one. As a document it writes the attributes of the item it holds, so <see cref="Item.From{T}"/> works on it.
/// </remarks>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
public readonly struct Mixed<T1, T2> : IDynamoDbDocument<Mixed<T1, T2>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
{
    /// <summary>What this mix knows about its types.</summary>
    internal static readonly MixedSchema Schema = new(T1.Info, T2.Info);

    private readonly int _type;
    private readonly T1 _first;
    private readonly T2 _second;

    private Mixed(int type, T1 first, T2 second)
    {
        _type = type;
        _first = first;
        _second = second;
    }

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

    /// <summary>Gets the item if it is of the first type.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool Is([MaybeNullWhen(false)] out T1 value)
    {
        value = _first;
        return _type == 1;
    }

    /// <summary>Gets the item if it is of the second type.</summary>
    /// <param name="value">The item, or the default value of the type.</param>
    /// <returns><see langword="true"/> if the item is of this type.</returns>
    public bool Is([MaybeNullWhen(false)] out T2 value)
    {
        value = _second;
        return _type == 2;
    }

    /// <summary>Computes a result from the item, with one function per type, so no type can be forgotten.</summary>
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

    /// <summary>Does something with the item, with one action per type, so no type can be forgotten.</summary>
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
}
