namespace DynaSharp.Fluent;

/// <summary>What the commands that offer <c>Select</c> and <c>And</c> ask of a handle before they use up theirs.</summary>
internal static class Selection
{
    /// <summary>Stops what no read of one attribute can do, at the call.</summary>
    /// <param name="attribute">The path of the handle.</param>
    /// <exception cref="ArgumentException">The handle is a default value, or its path has a list index: DynamoDB returns a shortened list, in which the position of an element is not known.</exception>
    public static void ThrowIfNoAttribute(string attribute)
    {
        ArgumentException.ThrowIfNullOrEmpty(attribute);
        if (attribute.Contains('[', StringComparison.Ordinal))
        {
            throw new ArgumentException($"The attribute path \"{attribute}\" has a list index, and a single list element cannot be selected.", nameof(attribute));
        }
    }

    /// <summary>Adds the attribute of <c>And</c> to a read of some attributes, after the checks that leave the command as it was when they throw.</summary>
    /// <param name="state">The state of the handle.</param>
    /// <param name="version">The version of the handle.</param>
    /// <param name="operation">The operation of the command, for the message.</param>
    /// <param name="table">The table of the command, for the message.</param>
    /// <param name="attribute">The path of the handle.</param>
    /// <returns>The state, entered.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read selects it already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public static CommandState Another(CommandState? state, int version, string operation, TableModel? table, string attribute)
    {
        ThrowIfNoAttribute(attribute);
        CommandState.Check(state, version, operation, table).ThrowIfSelected(attribute);
        var entered = CommandState.Enter(state, version, operation, table);
        entered.SelectAnother(attribute);
        return entered;
    }
}
