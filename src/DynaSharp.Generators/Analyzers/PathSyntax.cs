namespace DynaSharp.Generators;

/// <summary>
/// The syntax of a document path given as a string. Every name becomes a placeholder, so a name may hold any character except <c>.</c> and
/// <c>[</c>; this is not the grammar of paths in expression text.
/// </summary>
/// <remarks>Keep this in step with <c>ExpressionWriter.AppendPath</c>, which throws on exactly what this rejects. A test compares the two.</remarks>
internal static class PathSyntax
{
    private static readonly char[] Separators = ['.', '['];

    /// <summary>Checks a path.</summary>
    /// <remarks>
    /// <code>
    /// status, address.city, lines[2].sku, matrix[0][1]  valid
    /// a..b, a., .a, [0], a[, a[], a[x], a[0]b           not valid
    /// </code>
    /// </remarks>
    /// <param name="path">The path.</param>
    /// <returns>Whether DynaSharp accepts it.</returns>
    public static bool IsValid(string path)
    {
        var position = 0;
        while (true)
        {
            var stop = path.IndexOfAny(Separators, position);
            if ((stop < 0 ? path.Length : stop) == position)
            {
                return false;
            }

            if (stop < 0)
            {
                return true;
            }

            position = stop;
            while (path[position] == '[')
            {
                var close = path.IndexOf(']', position);
                if (close < position + 2 || !AreDigits(path, position + 1, close))
                {
                    return false;
                }

                position = close + 1;
                if (position == path.Length)
                {
                    return true;
                }
            }

            if (path[position] != '.')
            {
                return false;
            }

            position++;
        }
    }

    /// <summary>The first name of a valid path: <c>status</c> for <c>status.code[1]</c>.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The name.</returns>
    public static string Root(string path)
    {
        var stop = path.IndexOfAny(Separators);
        return stop < 0 ? path : path.Substring(0, stop);
    }

    private static bool AreDigits(string path, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (path[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
