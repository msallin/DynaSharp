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

    /// <summary>
    /// Finds UTF-8 text among names: <c>switch (name.Length)</c>, a <c>case</c> per byte length, and in it a test per name of that length, in ordinal order. A test compares the bytes, which it gives as
    /// the condition of an <c>if</c>, and leaves the case with a jump of its own when it matches; the case breaks when none does.
    /// </summary>
    /// <param name="span">The <c>ReadOnlySpan&lt;byte&gt;</c> to look up, as code.</param>
    /// <param name="items">What to look for.</param>
    /// <param name="name">The name of an item.</param>
    /// <param name="test">Writes the test of an item, given the condition that its name matches.</param>
    public void SwitchOnUtf8<T>(string span, IEnumerable<T> items, Func<T, string> name, Action<T, string> test)
    {
        Line($"switch ({span}.Length)");
        Open();
        foreach (var length in items.OrderBy(name, StringComparer.Ordinal).GroupBy(item => Encoding.UTF8.GetByteCount(name(item))).OrderBy(static group => group.Key))
        {
            Line($"case {CodeText.Number(length.Key)}:");
            Indent();
            foreach (var item in length)
            {
                test(item, $"global::System.MemoryExtensions.SequenceEqual({span}, {CodeText.Utf8(name(item))})");
            }

            Line("break;");
            Outdent();
        }

        Close();
        Blank();
    }
}
