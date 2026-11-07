using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.Mapping;

/// <summary>
/// How an item of one of several entities finds its type, built once per combination: the discriminator attribute they share and the value of each. What is wrong with
/// a combination is kept here and thrown at the call that asked for it, never from a type initializer. A mixed read adds its filter and projection on top, see
/// <see cref="Fluent.MixedSchema"/>.
/// </summary>
internal class DiscriminatorSchema
{
    private readonly byte[] _attributeUtf8;
    private readonly byte[][] _discriminatorsUtf8;
    private readonly string? _problem;
    private readonly string _typeName;

    /// <summary>Initializes a new instance of the <see cref="DiscriminatorSchema"/> class.</summary>
    /// <param name="types">What each type of the combination says about itself, in the order of the type arguments.</param>
    public DiscriminatorSchema(params ReadOnlySpan<DynamoDbEntityInfo> types)
    {
        Attribute = types[0].DiscriminatorAttributeName;
        _attributeUtf8 = Encoding.UTF8.GetBytes(Attribute);
        Discriminators = new string[types.Length];
        _discriminatorsUtf8 = new byte[types.Length][];
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            _problem ??= FindProblem(i, type);
            Discriminators[i] = type.Discriminator ?? string.Empty;
            _discriminatorsUtf8[i] = Encoding.UTF8.GetBytes(Discriminators[i]);
        }

        _typeName = $"Mixed<{string.Join(", ", Discriminators)}>";
    }

    /// <summary>Gets a value indicating whether the items of the combination can be told apart, which a single entity without a discriminator cannot.</summary>
    public bool TellsApart => _problem is null;

    /// <summary>Gets the attribute that holds the discriminator, the one of the first type.</summary>
    protected string Attribute { get; }

    /// <summary>Gets the discriminator of each type, in the order of the type arguments; empty for a type without one.</summary>
    protected string[] Discriminators { get; }

    /// <summary>
    /// Finds which type an item is. JSON has no order of attributes, so the discriminator can come last. The reader is a struct and arrives here as a copy, so
    /// it serves as a checkpoint: the caller's stays where it is, and the type reads the item from its first attribute, with no <see cref="Item"/> in between.
    /// </summary>
    /// <param name="probe">A copy of the reader, positioned after the opening brace of the item.</param>
    /// <returns>The index of the type in the combination.</returns>
    /// <exception cref="DynamoDbMappingException">The item has no discriminator, one that is not a string, or that of another type.</exception>
    public int FindType(DynamoDbReader probe)
    {
        while (probe.TryReadAttributeName())
        {
            if (probe.AttributeNameEquals(_attributeUtf8))
            {
                if (probe.ReadStartAttribute() != AttributeType.String)
                {
                    throw new DynamoDbMappingException(_typeName, Attribute);
                }

                var found = probe.GetStringUtf8();
                for (var i = 0; i < _discriminatorsUtf8.Length; i++)
                {
                    if (found.SequenceEqual(_discriminatorsUtf8[i]))
                    {
                        return i;
                    }
                }

                throw DynamoDbMappingException.OtherType(_typeName, Attribute, probe.GetString());
            }

            probe.SkipAttributeValue();
        }

        throw DynamoDbMappingException.Missing(_typeName, Attribute);
    }

    /// <summary>
    /// Finds which type an item that is already read is, by the rule of the wire overload: the discriminator attribute holds a string equal to the discriminator of the type. Asking is no
    /// read, so an item of none of the types answers -1 instead of throwing, also one without the attribute or with a value of another kind there.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The index of the type in the combination, or -1.</returns>
    /// <exception cref="ArgumentException">The combination cannot be told apart, see <see cref="FindProblem"/>.</exception>
    public int FindType(Item item)
    {
        Check();
        return item.TryGetValue(Attribute, out var value) && value.Type == AttributeType.String ? Array.IndexOf(Discriminators, value.AsString()) : -1;
    }

    /// <summary>Throws what is wrong with the combination, at the call that asked for it.</summary>
    /// <exception cref="ArgumentException">The combination cannot be told apart, see <see cref="FindProblem"/>.</exception>
    public void Check()
    {
        if (_problem is not null)
        {
            throw new ArgumentException(_problem);
        }
    }

    // The filter of a mixed read is one IN over one attribute, and a repeated value would give an item to the first of two types by accident.
    private string? FindProblem(int index, DynamoDbEntityInfo type)
    {
        if (type.Discriminator is null)
        {
            var which = Discriminators.Length == 1 ? "This type" : $"The type at position {index} of this mix";
            return $"{which} has no discriminator, so its items cannot be told from others.";
        }

        if (type.DiscriminatorAttributeName != Attribute)
        {
            return $"The types of this mix keep their discriminators in different attributes, {Attribute} and {type.DiscriminatorAttributeName}, and one filter cannot read both.";
        }

        return Array.IndexOf(Discriminators, type.Discriminator, 0, index) >= 0
            ? $"The discriminator {type.Discriminator} occurs twice in this mix, so its items cannot be told apart."
            : null;
    }

    /// <summary>
    /// The schema of one type, which is what <see cref="Item.Is{T}"/> and <see cref="Streams.ChangeRecord.As{T}"/> ask with, built once per type as that of a mix is. An
    /// entity without a discriminator has a schema with a problem, which <see cref="Item.Is{T}"/> throws and a typed change takes as "every item".
    /// </summary>
    /// <typeparam name="T">The type.</typeparam>
    internal static class Of<T>
        where T : IDynamoDbEntity<T>
    {
        /// <summary>What the type knows about its items.</summary>
        public static readonly DiscriminatorSchema Schema = new(T.Info);
    }
}
