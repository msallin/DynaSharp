using System.ComponentModel;
using DynaSharp.Fluent;

namespace DynaSharp.Expressions;

/// <summary>
/// A command under construction, as the compiler hands it to an <see cref="ExpressionHandler"/>: every command converts to one implicitly.
/// It has no members and is never named in code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct CommandHandle
{
    internal CommandHandle(CommandState? state, int version, string operation, TableModel? table)
    {
        State = state;
        Version = version;
        Operation = operation;
        Table = table;
    }

    internal CommandState? State { get; }

    internal int Version { get; }

    internal string Operation { get; }

    internal TableModel? Table { get; }
}
