namespace DynaSharp.Fluent;

/// <summary>
/// What <c>TransactWriteItems</c> and <c>TransactGetItems</c> have in common: a list of actions, each with a table and expressions of its own. Actions go
/// to the body as they are added, and one stays open until the next starts, so that its clauses can follow.
/// </summary>
internal static class Transaction
{
    /// <summary>The most actions DynamoDB takes in a transaction.</summary>
    public const int MaxActions = 100;

    /// <summary>Starts a transaction.</summary>
    /// <param name="model">What stands for its tables.</param>
    /// <returns>The state of the command.</returns>
    public static CommandState Start(TableModel model)
    {
        var state = model.BeginActions();
        state.Body.Append("{\"TransactItems\":["u8);
        return state;
    }

    /// <summary>Ends the action before, if there is one, and starts the next.</summary>
    /// <param name="state">The state of the command.</param>
    /// <param name="table">The table of the action.</param>
    /// <param name="kind">The start of the action, such as <c>{"Put":</c>.</param>
    /// <returns>The index of the action, which is the index of its entry in what DynamoDB answers.</returns>
    /// <exception cref="InvalidOperationException">The transaction holds the most actions it can.</exception>
    public static int AddAction(CommandState state, TableModel table, ReadOnlySpan<byte> kind)
    {
        var actions = state.Actions;
        var index = actions.Count;
        if (index == MaxActions)
        {
            throw new InvalidOperationException($"A transaction holds at most {MaxActions} actions, and this would be one more. DynamoDB rejects it, so split the work, or use a batch if it need not be atomic.");
        }

        var body = state.Body;
        if (index > 0)
        {
            state.EndAction();
            body.Append((byte)',');
        }

        actions.Add(table, body.WrittenCount);
        state.Table = table;
        body.Append(kind);
        body.Append(table.BodyStart);
        return index;
    }

    /// <summary>Records the key the action just added names its item by, and refuses it if an action before it names the same item.</summary>
    /// <param name="state">The state of the command, whose body ends with the key.</param>
    /// <exception cref="InvalidOperationException">An action before it names the same item.</exception>
    public static void AddKey(CommandState state)
    {
        var body = state.Body.WrittenSpan;
        var actions = state.Actions;
        var earlier = actions.AddKey(state.KeyStart, body);
        if (earlier >= 0)
        {
            throw new InvalidOperationException(
                $"Action {actions.Count} of this transaction names the same item of table {state.Table.Name} as action {earlier + 1} ({actions.VerbOf(earlier, body)}), and DynamoDB refuses a transaction with two actions on one item. Put the conditions and changes on one action.");
        }
    }

    /// <summary>Ends the last action and the list. The options follow from <see cref="CommandState.Finish"/>.</summary>
    /// <param name="state">The state of the command.</param>
    public static void EndActions(CommandState state)
    {
        var actions = state.Actions;
        if (actions.Count > 0)
        {
            state.EndAction();
        }

        state.Body.Append((byte)']');
        actions.End = actions.Count;
    }
}
