using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Writes generated code as indented lines: an emitter says how deep a line goes and never spells the spaces. Lines end in <c>\n</c> on every platform,
/// so the output does not depend on the build machine.
/// </summary>
/// <param name="text">Where the code goes.</param>
/// <param name="depth">How deep it starts. The default is for members of a type: a body sits at one indent, however deep the type is nested.</param>
internal sealed class IndentedCode(StringBuilder text, int depth = 1)
{
    private const int IndentWidth = 4;

    private int _depth = depth;

    public void Line(string line) => text.Append(' ', _depth * IndentWidth).Append(line).Append('\n');

    public void Blank() => text.Append('\n');

    /// <summary>Writes the blank line between two members of a list, which the first one goes without.</summary>
    /// <param name="isFirst">Whether no member was written yet. It starts out <see langword="true"/> and this clears it.</param>
    public void Separate(ref bool isFirst)
    {
        if (!isFirst)
        {
            Blank();
        }

        isFirst = false;
    }

    /// <summary>Goes one level deeper where no brace does: the statements of a <c>case</c>, the arguments of a call.</summary>
    public void Indent() => _depth++;

    /// <summary>Ends what <see cref="Indent"/> started.</summary>
    public void Outdent() => _depth--;

    public void Open()
    {
        Line("{");
        Indent();
    }

    /// <summary>Ends what <see cref="Open"/> started.</summary>
    /// <param name="closer">The closing line, which is more than a brace after an initializer or a switch expression: <c>},</c> or <c>};</c>.</param>
    public void Close(string closer = "}")
    {
        Outdent();
        Line(closer);
    }
}
