using System.Text;

namespace DynaSharp.Fluent;

/// <summary>
/// The words of a condition, and with them the check that an expression names an attribute at all: one that compares values only is a constant that DynamoDB accepts, so
/// <see cref="ExpressionWriter.CommitPending"/> refuses it. The lists repeat the grammar of the generator, which ships as an analyzer that this assembly cannot reference, and
/// <c>ExpressionWordsTests</c> keeps the two in step and names where they differ on purpose.
/// </summary>
internal static class ExpressionWords
{
    /// <summary>The keywords of a condition, which count in any case. They repeat <c>ExpressionParser.Keywords</c> of the generator.</summary>
    internal static readonly string[] ConditionKeywords = ["AND", "OR", "NOT", "BETWEEN", "IN"];

    /// <summary>
    /// The functions of a condition, which count in this case only. They repeat <c>ExpressionFunction.All</c> of the generator. The functions of an update are left out: this list serves the check of
    /// a condition.
    /// </summary>
    internal static readonly string[] ConditionFunctions = ["size", "attribute_exists", "attribute_not_exists", "attribute_type", "begins_with", "contains"];

    /// <summary>
    /// Tells whether a byte can be part of a word. The lexer of the generator repeats this for text a compiler sees. Unlike there, a byte from 0x80 counts here: the
    /// text is UTF-8, so that is part of a name that is not ASCII, and it is for DynamoDB to reject that name, not for this check to call it no name at all.
    /// </summary>
    /// <param name="value">The byte.</param>
    /// <returns><see langword="true"/> for a letter, a digit, <c>_</c> and a byte of a character that is not ASCII.</returns>
    internal static bool IsWordByte(byte value) => value >= 0x80 || value == (byte)'_' || char.IsAsciiLetterOrDigit((char)value);

    /// <summary>
    /// Tells whether an expression names an attribute. A generated name is a <c>#n</c> placeholder and only a name is. Without one, what is left to look for is a name in the literal text of
    /// the expression: a word that is no keyword, no function and no value placeholder.
    /// </summary>
    /// <param name="text">The expression, with its names and values as placeholders.</param>
    /// <returns><see langword="true"/> if it names an attribute.</returns>
    internal static bool NamesAnAttribute(ReadOnlySpan<byte> text)
    {
        if (text.Contains((byte)'#'))
        {
            return true;
        }

        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordByte(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && IsWordByte(text[i]))
            {
                i++;
            }

            var isPlaceholder = start > 0 && text[start - 1] == (byte)':';
            if (!isPlaceholder && !IsExpressionWord(text[start..i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExpressionWord(ReadOnlySpan<byte> word)
    {
        foreach (var keyword in ConditionKeywords)
        {
            if (Ascii.EqualsIgnoreCase(word, keyword))
            {
                return true;
            }
        }

        foreach (var function in ConditionFunctions)
        {
            if (Ascii.Equals(word, function))
            {
                return true;
            }
        }

        return false;
    }
}
