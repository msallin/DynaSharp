using DynaSharp.Expressions;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What a mixed read knows about its types, built once per combination: how an item finds its type (see <see cref="DiscriminatorSchema"/>), the filter that lets only
/// them through, and what a projection needs. It is a static of the <c>Mixed</c> struct, so what is wrong with a combination is kept and thrown at the call that asked
/// for it, never from a type initializer.
/// </summary>
internal sealed class MixedSchema : DiscriminatorSchema
{
    private readonly string[] _requiredNames;

    /// <summary>Initializes a new instance of the <see cref="MixedSchema"/> class.</summary>
    /// <param name="types">What each type of the combination says about itself, in the order of the type arguments.</param>
    public MixedSchema(params ReadOnlySpan<DynamoDbEntityInfo> types)
        : base(types)
    {
        List<string> required = [Attribute];
        foreach (var type in types)
        {
            Require(required, type.PartitionKeyName);
            Require(required, type.SortKeyName);
        }

        _requiredNames = [.. required];
    }

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
    /// <exception cref="ArgumentException">The combination cannot be told apart, see <see cref="DiscriminatorSchema.Check"/>.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public CommandState StartQuery(CommandHandle command)
    {
        var state = Enter(command);
        if (!state.Table.IsKey(Attribute))
        {
            AddFilter(state);
        }

        return state;
    }

    /// <summary>Turns a scan of items into a mixed read: the filter gains the discriminators, on any attribute, and the handle of the read is used up.</summary>
    /// <param name="command">The scan.</param>
    /// <returns>The state of the read, for a command of the mix.</returns>
    /// <exception cref="ArgumentException">The combination cannot be told apart, see <see cref="DiscriminatorSchema.Check"/>.</exception>
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
        expressions.AppendAttributeName(text, Attribute);
        text.Append(" IN ("u8);
        var discriminators = Discriminators;
        for (var i = 0; i < discriminators.Length; i++)
        {
            if (i > 0)
            {
                text.Append(", "u8);
            }

            expressions.AppendValue<string, StringConverter>(text, discriminators[i]);
        }

        text.Append((byte)')');
        expressions.CommitPending(Clause.Filter);
    }
}
