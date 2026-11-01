using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Configuration;

/// <summary>
/// Parses the INI dialect of the AWS shared config and credentials files exactly as botocore does: Python's
/// <c>RawConfigParser</c> with default settings, plus botocore's nested blocks.
/// </summary>
/// <remarks>
/// <para>
/// The rules, all verified against botocore by the vectors of the unit tests:
/// <c>#</c> and <c>;</c> start a comment only at the start of a line, so a value keeps a trailing <c>  # comment</c>.
/// Option names are lower-cased, section names are not. <c>=</c> and <c>:</c> both separate name and value, the first one in the
/// line wins. A line indented deeper than the last section or option header continues that option; the lines are joined with line
/// feeds. A blank line inside a value is kept unless the value ends there. A repeated section or option is an error.
/// Options of <c>[DEFAULT]</c> are inherited by every other section. Content before the first section header is an error.
/// A value that starts on the line after <c>name =</c> is a block: each of its lines must be <c>name = value</c>, and a line without
/// <c>=</c> makes the whole file malformed, even if the block belongs to a section nobody reads.
/// </para>
/// <para>
/// The caller decides the encoding. The default file reader decodes UTF-8 (skipping a byte order mark) and replaces
/// invalid bytes, where botocore uses the locale encoding and fails on invalid bytes.
/// </para>
/// </remarks>
internal static class IniFile
{
    private const string DefaultSectionName = "DEFAULT";

    /// <summary>Parses the text of an INI file.</summary>
    /// <param name="text">The file content.</param>
    /// <param name="fileName">The path of the file, for error messages.</param>
    /// <returns>The sections in file order, without the <c>[DEFAULT]</c> section.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// The file is malformed. The message names the file and the line, never the content. This type is used here because the files
    /// are credential sources; the region lookup catches it.
    /// </exception>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Not a comparison: Python's configparser lower-cases option names, and so does botocore's profile lookup.")]
    public static IReadOnlyList<IniSection> Parse(string text, string fileName)
    {
        var sections = new List<SectionBuilder>();
        var sectionsByName = new Dictionary<string, SectionBuilder>(StringComparer.Ordinal);
        var defaults = new SectionBuilder(DefaultSectionName);
        var addedOptions = new HashSet<(string Section, string Option)>();

        SectionBuilder? current = null;
        string? option = null;
        var indentLevel = 0;
        var firstBadLine = 0;
        var lineNumber = 0;

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.Trim();
            var isComment = trimmed.Length > 0 && trimmed[0] is '#' or ';';
            if (trimmed.Length == 0 || isComment)
            {
                // A blank line inside a value belongs to the value; a comment line does not.
                if (!isComment && current is not null && !string.IsNullOrEmpty(option))
                {
                    current.Lines[option].Add(string.Empty);
                }

                continue;
            }

            var indent = IndexOfFirstNonWhitespace(line);
            if (current is not null && !string.IsNullOrEmpty(option) && indent > indentLevel)
            {
                current.Lines[option].Add(trimmed);
                continue;
            }

            indentLevel = indent;
            if (TryParseHeader(trimmed, out var header))
            {
                if (sectionsByName.ContainsKey(header))
                {
                    throw Malformed(fileName, lineNumber, "a section appears twice");
                }

                if (header == DefaultSectionName)
                {
                    current = defaults;
                }
                else
                {
                    current = new SectionBuilder(header);
                    sections.Add(current);
                    sectionsByName.Add(header, current);
                }

                option = null;
            }
            else if (current is null)
            {
                throw Malformed(fileName, lineNumber, "content before the first [section] header");
            }
            else
            {
                var delimiter = trimmed.AsSpan().IndexOfAny('=', ':');
                if (delimiter < 0)
                {
                    // Python collects these and reports them at the end of the file, unless a fatal error comes first.
                    firstBadLine = firstBadLine == 0 ? lineNumber : firstBadLine;
                    continue;
                }

                option = trimmed[..delimiter].TrimEnd().ToLowerInvariant();
                if (option.Length == 0)
                {
                    firstBadLine = firstBadLine == 0 ? lineNumber : firstBadLine;
                }

                if (!addedOptions.Add((current.Name, option)))
                {
                    throw Malformed(fileName, lineNumber, "an option appears twice in a section");
                }

                current.Lines[option] = [trimmed[(delimiter + 1)..].Trim()];
                current.OptionLines[option] = lineNumber;
            }
        }

        if (firstBadLine != 0)
        {
            throw Malformed(fileName, firstBadLine, "expected 'name = value' or a [section] header");
        }

        var result = new List<IniSection>(sections.Count);
        foreach (var section in sections)
        {
            result.Add(new IniSection(section.Name, BuildValues(section, defaults, fileName)));
        }

        return result;
    }

    private static Dictionary<string, IniValue> BuildValues(SectionBuilder section, SectionBuilder defaults, string fileName)
    {
        var values = new Dictionary<string, IniValue>(StringComparer.Ordinal);
        foreach (var builder in new[] { defaults, section })
        {
            foreach (var (name, lines) in builder.Lines)
            {
                var text = string.Join('\n', lines).TrimEnd();
                values[name] = text is ['\n', ..]
                    ? ParseBlock(text, fileName, builder.OptionLines[name])
                    : new IniValue(text, null);
            }
        }

        return values;
    }

    // Input:  "\nmax_concurrent_requests = 20\naddressing_style = path"
    // Result: { "max_concurrent_requests": "20", "addressing_style": "path" }
    private static IniValue ParseBlock(string text, string fileName, int optionLine)
    {
        var block = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                throw Malformed(fileName, optionLine, "a nested block contains a line that is not 'name = value'");
            }

            block[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return new IniValue(text, block);
    }

    // Input:  "[profile dev] # comment"
    // Header: "profile dev"; the header ends at the last closing bracket of the line and needs at least one character.
    private static bool TryParseHeader(string line, out string header)
    {
        header = string.Empty;
        if (line[0] != '[')
        {
            return false;
        }

        var close = line.AsSpan().LastIndexOf(']');
        if (close < 2)
        {
            return false;
        }

        header = line[1..close];
        return true;
    }

    private static int IndexOfFirstNonWhitespace(string line)
    {
        var index = 0;
        while (char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return index;
    }

    private static CredentialsUnavailableException Malformed(string fileName, int line, string reason) =>
        new($"The file '{fileName}' is malformed at line {line}: {reason}.");

    private sealed class SectionBuilder(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, List<string>> Lines { get; } = [];

        public Dictionary<string, int> OptionLines { get; } = [];
    }
}
