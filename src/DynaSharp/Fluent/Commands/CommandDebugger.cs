namespace DynaSharp.Fluent;

/// <summary>What the commands show in a debugger: the request as far as it is written, so that a developer stepping through a chain does not see byte arrays.</summary>
internal static class CommandDebugger
{
    /// <summary>Renders a command.</summary>
    /// <param name="operation">The operation of the command.</param>
    /// <param name="state">The state of the handle.</param>
    /// <param name="version">The version of the handle.</param>
    /// <param name="table">The table of the handle.</param>
    /// <returns>The text.</returns>
    public static string Describe(string operation, CommandState? state, int version, TableModel? table) =>
        state is null ? $"{operation} (default)" : $"{operation} on {table!.Name}: {state.Describe(version)}";
}
