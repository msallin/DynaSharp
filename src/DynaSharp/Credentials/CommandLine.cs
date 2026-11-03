using System.Text;
using DynaSharp.Configuration;

namespace DynaSharp.Credentials;

/// <summary>
/// Splits the <c>credential_process</c> setting of a profile into a program and its arguments, as botocore's <c>compat_shell_split</c>
/// does, so that the process starts without a shell: on Windows by the rules with which the C runtime reads a command line, elsewhere
/// like a POSIX shell (Python's <c>shlex.split</c>).
/// </summary>
internal static class CommandLine
{
    /// <summary>Splits a command line into words.</summary>
    /// <param name="text">The command line.</param>
    /// <param name="windows">
    /// <see langword="true"/> for the rules of Windows, <see langword="false"/> for those of a POSIX shell. The caller passes
    /// <see cref="OperatingSystem.IsWindows"/>, which botocore asks too.
    /// </param>
    /// <returns>The words, or <see langword="null"/> when a quote is not closed (or, on POSIX, the text ends with a backslash).</returns>
    public static string[]? Split(string text, bool windows) => windows ? SplitWindows(text) : ShellWords.Split(text);

    /// <summary>botocore's <c>_windows_shell_split</c>.</summary>
    /// <remarks>
    /// Only space and tab separate words, and only double quotes quote. A backslash is literal unless a run of them leads up to a
    /// double quote: then each pair is one backslash, and an odd one left over makes the quote literal. <c>""</c> is an empty word.
    /// </remarks>
    // Input:  "C:\Program Files\creds.exe" --role "a b" say\"hi\"
    // Result: ["C:\Program Files\creds.exe", "--role", "a b", "say\"hi\""]
    internal static string[]? SplitWindows(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        var quoted = false;
        var backslashes = 0;
        foreach (var c in text)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                word.Append('\\', backslashes / 2);
                var escaped = backslashes % 2 == 1;
                backslashes = 0;
                inWord = true;
                if (escaped)
                {
                    word.Append('"');
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            word.Append('\\', backslashes);
            inWord |= backslashes > 0;
            backslashes = 0;
            if (c is ' ' or '\t' && !quoted)
            {
                if (inWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                }

                continue;
            }

            word.Append(c);
            inWord = true;
        }

        if (quoted)
        {
            return null;
        }

        word.Append('\\', backslashes);
        if (inWord || backslashes > 0)
        {
            words.Add(word.ToString());
        }

        return [.. words];
    }
}
