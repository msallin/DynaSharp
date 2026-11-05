using System.Text;
using DynaSharp.Expressions;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What a mixed read knows about its types, built once per combination: the filter that lets only them through, what a projection needs, and how an item
/// finds its type. It is a static of the <c>Mixed</c> struct, so what is wrong with a combination is kept here and thrown at the call that asked for it,
/// never from a type initializer.
/// </summary>
internal sealed class MixedSchema
{
    private readonly string _attribute;
    private readonly byte[] _attributeUtf8;
    private readonly string[] _discriminators;
    private readonly byte[][] _discriminatorsUtf8;
    private readonly string[] _requiredNames;
    private readonly string? _problem;
    private readonly string _typeName;

    /// <summary>Initializes a new instance of the <see cref="MixedSchema"/> class.</summary>
    /// <param name="types">What each type of the combination says about itself, in the order of the type arguments.</param>
    public MixedSchema(params ReadOnlySpan<DynamoDbEntityInfo> types)
    {
        _attribute = types[0].DiscriminatorAttributeName;
        _attributeUtf8 = Encoding.UTF8.GetBytes(_attribute);
        _discriminators = new string[types.Length];
        _discriminatorsUtf8 = new byte[types.Length][];
        List<string> required = [_attribute];
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var discriminator = type.Discriminator;
            _problem ??= FindProblem(i, type);
            _discriminators[i] = discriminator ?? string.Empty;
            _discriminatorsUtf8[i] = Encoding.UTF8.GetBytes(_discriminators[i]);
            Require(required, type.PartitionKeyName);
            Require(required, type.SortKeyName);
        }

        _requiredNames = [.. required];
        _typeName = $"Mixed<{string.Join(", ", _discriminators)}>";
    }

    /// <summary>Gets a value indicating whether the items of the combination can be told apart, which a single entity without a discriminator cannot.</summary>
    public bool TellsApart => _problem is null;

    /// <summary>Gets what an empty value says when it is asked for what it holds.</summary>
    /// <returns>The exception.</returns>
    public static InvalidOperationException Empty() =>
        new("This Mixed is a default value, which holds nothing. Items of a mixed read always hold one of their types.");

    /// <summary>
    /// Turns a query of items into a mixed read: the filter gains the discriminators, and the handle of the read is used up. DynamoDB refuses a filter on a key of what a query reads, so a
    /// query of a table or index keyed by the discriminator goes without, as a typed query does, and its key condition says which items come back.
    /// </summary>
    /// <param name="command">The query.</param>
    /// <returns>The state of the read, for a command of the mix.</returns>
    /// <exception cref="ArgumentException">The combination cannot be told apart, see <see cref="FindProblem"/>.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public CommandState StartQuery(CommandHandle command)
    {
        var state = Enter(command);
        if (!state.Table.IsKey(_attribute))
        {
            AddFilter(state);
        }

        return state;
    }

    /// <summary>Turns a scan of items into a mixed read: the filter gains the discriminators, on any attribute, and the handle of the read is used up.</summary>
    /// <param name="command">The scan.</param>
    /// <returns>The state of the read, for a command of the mix.</returns>
    /// <exception cref="ArgumentException">The combination cannot be told apart, see <see cref="FindProblem"/>.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public CommandState StartScan(CommandHandle command)
    {
        var state = Enter(command);
        AddFilter(state);
        return state;
    }

    /// <summary>
    /// Completes the projection with what the types need. The read goes through a table without a schema, whose model requires nothing, so it happens here, when
    /// the chain ends. The handle stays good for the terminal that follows.
    /// </summary>
    /// <param name="command">The read.</param>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public void CompleteProjection(CommandHandle command)
    {
        CommandState.Check(command.State, command.Version, command.Operation, command.Table).Expressions.CompleteProjection(_requiredNames);
    }

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
                    throw new DynamoDbMappingException(_typeName, _attribute);
                }

                var found = probe.GetStringUtf8();
                for (var i = 0; i < _discriminatorsUtf8.Length; i++)
                {
                    if (found.SequenceEqual(_discriminatorsUtf8[i]))
                    {
                        return i;
                    }
                }

                throw DynamoDbMappingException.OtherType(_typeName, _attribute, probe.GetString());
            }

            probe.SkipAttributeValue();
        }

        throw DynamoDbMappingException.Missing(_typeName, _attribute);
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
        return item.TryGetValue(_attribute, out var value) && value.Type == AttributeType.String ? Array.IndexOf(_discriminators, value.AsString()) : -1;
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

    private static void Require(List<string> required, string? name)
    {
        if (name is not null && !required.Contains(name))
        {
            required.Add(name);
        }
    }

    private CommandState Enter(CommandHandle command)
    {
        Check();
        return CommandState.Enter(command.State, command.Version, command.Operation, command.Table);
    }

    // #n0 IN (:v0, :v1)
    private void AddFilter(CommandState state)
    {
        var expressions = state.Expressions;
        var text = expressions.StartPending();
        expressions.AppendAttributeName(text, _attribute);
        text.Append(" IN ("u8);
        for (var i = 0; i < _discriminators.Length; i++)
        {
            if (i > 0)
            {
                text.Append(", "u8);
            }

            expressions.AppendValue<string, StringConverter>(text, _discriminators[i]);
        }

        text.Append((byte)')');
        expressions.CommitPending(Clause.Filter);
    }

    // The filter is one IN over one attribute, and a repeated value would give an item to the first of two types by accident.
    private string? FindProblem(int index, DynamoDbEntityInfo type)
    {
        if (type.Discriminator is null)
        {
            var which = _discriminators.Length == 1 ? "This type" : $"The type at position {index} of this mix";
            return $"{which} has no discriminator, so its items cannot be told from others.";
        }

        if (type.DiscriminatorAttributeName != _attribute)
        {
            return $"The types of this mix keep their discriminators in different attributes, {_attribute} and {type.DiscriminatorAttributeName}, and one filter cannot read both.";
        }

        return Array.IndexOf(_discriminators, type.Discriminator, 0, index) >= 0
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
        public static readonly MixedSchema Schema = new(T.Info);
    }
}
