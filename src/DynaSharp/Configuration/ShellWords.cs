using System.Text;

namespace DynaSharp.Configuration;

/// <summary>
/// Splits a text into words the way Python's <c>shlex.split</c> does in POSIX mode. botocore uses it to turn the header
/// <c>profile "my profile"</c> into the words <c>profile</c> and <c>my profile</c>.
/// </summary>
internal static class ShellWords
{
    /// <summary>Splits <paramref name="text"/> into words.</summary>
    /// <param name="text">The text. Words are separated by space, tab, carriage return and line feed.</param>
    /// <returns>The words, or <see langword="null"/> when a quote is not closed or the text ends with a backslash.</returns>
    /// <remarks>
    /// Single quotes keep everything. Inside double quotes a backslash escapes only a double quote and a backslash, and stays
    /// a backslash before any other character. Outside quotes a backslash escapes the next character. Quoted and unquoted parts
    /// next to each other form one word, and <c>""</c> is an empty word.
    /// </remarks>
    // Input:  profile "my profile"
    // Result: ["profile", "my profile"]
    public static string[]? Split(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index++];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                if (inWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                }

                continue;
            }

            inWord = true;
            if (c == '\\')
            {
                if (index == text.Length)
                {
                    return null;
                }

                word.Append(text[index++]);
            }
            else if (c == '\'')
            {
                var length = text.AsSpan(index).IndexOf('\'');
                if (length < 0)
                {
                    return null;
                }

                word.Append(text, index, length);
                index += length + 1;
            }
            else if (c == '"')
            {
                if (!TryReadDoubleQuoted(text, ref index, word))
                {
                    return null;
                }
            }
            else
            {
                word.Append(c);
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return [.. words];
    }

    private static bool TryReadDoubleQuoted(string text, ref int index, StringBuilder word)
    {
        while (index < text.Length)
        {
            var c = text[index++];
            if (c == '"')
            {
                return true;
            }

            if (c != '\\')
            {
                word.Append(c);
                continue;
            }

            if (index == text.Length)
            {
                return false;
            }

            var escaped = text[index++];
            if (escaped is not ('"' or '\\'))
            {
                word.Append('\\');
            }

            word.Append(escaped);
        }

        return false;
    }
}
