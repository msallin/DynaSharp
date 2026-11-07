namespace DynaSharp.Testing.Fake;

/// <summary>
/// Splits the text of an expression into tokens. White space is a space, a tab, a carriage return or a line feed, and separates tokens without being one.
/// <code>
/// Text:   size(#tags) &lt;= :max AND a.b[0] &lt;&gt; :v
/// Tokens: size ( #tags ) &lt;= :max AND a . b [ 0 ] &lt;&gt; :v End
/// </code>
/// </summary>
/// <remarks>
/// The keywords and functions are spelled here again, independent of the client's <c>ExpressionWords</c> and of the generator's grammar, which the fake must not echo. A unit test keeps
/// the lists equal to theirs.
/// </remarks>
internal static class ExpressionTokenizer
{
    /// <summary>The keywords of a condition, matched in any case.</summary>
    public static readonly string[] ConditionKeywords = ["AND", "OR", "NOT", "BETWEEN", "IN"];

    /// <summary>The sections of an update, matched in any case.</summary>
    public static readonly string[] UpdateSections = ["SET", "REMOVE", "ADD", "DELETE"];

    /// <summary>The functions of a condition, matched case-sensitively.</summary>
    public static readonly string[] ConditionFunctions = ["size", "attribute_exists", "attribute_not_exists", "attribute_type", "begins_with", "contains"];

    /// <summary>The functions of the <c>SET</c> section of an update, matched case-sensitively.</summary>
    public static readonly string[] UpdateFunctions = ["if_not_exists", "list_append"];

    /// <summary>Reads the tokens of a text.</summary>
    /// <param name="text">The expression.</param>
    /// <returns>The tokens, the last of which is <see cref="ExpressionTokenKind.End"/> at the end of the text.</returns>
    public static List<ExpressionToken> Tokenize(string text)
    {
        var tokens = new List<ExpressionToken>();
        var position = 0;
        while (position < text.Length)
        {
            var start = position;
            if (text[position] is ' ' or '\t' or '\r' or '\n')
            {
                position++;
                continue;
            }

            var kind = Read(text, ref position);
            tokens.Add(new ExpressionToken(kind, start, position - start));
        }

        tokens.Add(new ExpressionToken(ExpressionTokenKind.End, text.Length, 0));
        return tokens;
    }

    /// <summary>Tells whether a character can be part of a word or a placeholder.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for an ASCII letter or digit and for an underscore.</returns>
    public static bool IsWordCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';

    private static ExpressionTokenKind Read(string text, ref int position)
    {
        var first = text[position++];
        switch (first)
        {
            case '(':
                return ExpressionTokenKind.OpenParenthesis;
            case ')':
                return ExpressionTokenKind.CloseParenthesis;
            case '[':
                return ExpressionTokenKind.OpenBracket;
            case ']':
                return ExpressionTokenKind.CloseBracket;
            case ',':
                return ExpressionTokenKind.Comma;
            case '.':
                return ExpressionTokenKind.Dot;
            case '+' or '-':
                return ExpressionTokenKind.Sign;
            case '=':
                return ExpressionTokenKind.Equal;
            case '<':
                SkipIf(text, ref position, '=', '>');
                return ExpressionTokenKind.Comparator;
            case '>':
                SkipIf(text, ref position, '=', '=');
                return ExpressionTokenKind.Comparator;
            case '#':
                return ReadPlaceholder(text, ref position, ExpressionTokenKind.NamePlaceholder);
            case ':':
                return ReadPlaceholder(text, ref position, ExpressionTokenKind.ValuePlaceholder);
            default:
                return ReadWordOrNumber(first, text, ref position);
        }
    }

    // "12" is a number and "a12" a word; "1a" is the number 1 and then the word a, which no rule of the grammar takes. A word starts with a letter, as DynamoDB
    // Local requires: "_a" is an invalid character and the word a. A placeholder may start with any word character: "#_a", ":1".
    private static ExpressionTokenKind ReadWordOrNumber(char first, string text, ref int position)
    {
        if (char.IsAsciiDigit(first))
        {
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            return ExpressionTokenKind.Number;
        }

        if (!char.IsAsciiLetter(first))
        {
            return ExpressionTokenKind.Invalid;
        }

        SkipWord(text, ref position);
        return ExpressionTokenKind.Word;
    }

    // A lone # or : is no placeholder: "# = :v" has an invalid character where a name goes.
    private static ExpressionTokenKind ReadPlaceholder(string text, ref int position, ExpressionTokenKind kind)
    {
        var start = position;
        SkipWord(text, ref position);
        return position == start ? ExpressionTokenKind.Invalid : kind;
    }

    private static void SkipWord(string text, ref int position)
    {
        while (position < text.Length && IsWordCharacter(text[position]))
        {
            position++;
        }
    }

    private static void SkipIf(string text, ref int position, char first, char second)
    {
        if (position < text.Length && (text[position] == first || text[position] == second))
        {
            position++;
        }
    }
}
