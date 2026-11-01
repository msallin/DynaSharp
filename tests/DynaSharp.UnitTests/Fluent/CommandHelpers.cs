using DynaSharp.Expressions;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>The helpers an application writes over the composition interfaces: one body for every command, without a box.</summary>
internal static class CommandHelpers
{
    public static TCommand OnlyIfOwned<TCommand>(this TCommand command, string owner)
        where TCommand : IConditional<TCommand>, allows ref struct => command.If(Attr("owner").Eq(owner));

    public static TCommand NotDeleted<TCommand>(this TCommand command)
        where TCommand : IFilterable<TCommand>, allows ref struct => command.Filter(Attr("deletedAt").NotExists());

    public static TCommand Audited<TCommand>(this TCommand command, string user)
        where TCommand : IUpdatable<TCommand>, allows ref struct => command.Set("updatedBy", user);

    public static TCommand Once<TCommand>(this TCommand command)
        where TCommand : IRetryable<TCommand>, allows ref struct => command.MaxAttempts(1);

    public static TCommand Touched<TCommand, TValue, TConverter>(this TCommand command, Attr<TValue, TConverter> attribute, TValue value)
        where TCommand : IUpdatable<TCommand>, allows ref struct
        where TConverter : DynaSharp.Serialization.IDynamoDbConverter<TValue>, DynaSharp.Serialization.IAddableConverter =>
        command.Set(attribute, value).SetIfNotExists(attribute, value).Add(attribute, value).Delete(attribute, value);

    public static TCommand Filled<TCommand, TValue, TConverter>(this TCommand command, Attr<TValue, TConverter> attribute, TValue? value)
        where TCommand : IUpdatable<TCommand>, allows ref struct
        where TValue : struct
        where TConverter : DynaSharp.Serialization.IDynamoDbConverter<TValue> => command.Set(attribute, value);

    public static TCommand AllUntyped<TCommand>(this TCommand command)
        where TCommand : IUpdatable<TCommand>, allows ref struct =>
        command.SetIfNotExists("a", 1).Add("b", 2).Delete("c", AttributeValue.FromStringSet(["x"])).Remove("d");

    /// <summary>Finishes a command the way a terminal and the pipeline do, without sending it, and gives the state back.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The length of the body.</returns>
    public static int FinishAndRelease(CommandHandle command)
    {
        var state = DynaSharp.Fluent.WriteRules.Settle(DynaSharp.Fluent.CommandState.Enter(command.State, command.Version, command.Operation, command.Table));
        var version = state.Version;
        var length = state.Finish().Count;
        state.Release(version);
        return length;
    }
}
