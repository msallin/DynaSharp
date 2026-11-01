using System.Text;

namespace DynaSharp.Generators;

/// <summary>Writes the members of a type as indented lines, for code nested deeper than the flat text of <see cref="CodeText"/> reads well.</summary>
/// <param name="text">The body of the type.</param>
internal sealed class IndentedCode(StringBuilder text)
{
    private const int IndentWidth = 4;

    // Members of a type: a body sits at one indent, however deep the type is nested.
    private int _depth = 1;

    public void Line(string line) => text.Append(' ', _depth * IndentWidth).Line(line);

    public void Blank() => text.Line();

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

    public void Open()
    {
        Line("{");
        _depth++;
    }

    public void Close()
    {
        _depth--;
        Line("}");
    }
}
