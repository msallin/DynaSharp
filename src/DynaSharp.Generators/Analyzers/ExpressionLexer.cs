namespace DynaSharp.Generators;

/// <summary>
/// Splits the parts of an expression into tokens and finds the placeholders.
/// <code>
/// Parts:  "size(" {Attr("tags")} ") &lt; 10"
/// Tokens: size ( hole ) &lt; 10
/// </code>
/// A word is ASCII: DynamoDB Local and dynoxide both reject anything else in a raw name. A leading underscore stays a word, although DynamoDB
/// Local rejects it, because dynoxide does not and a missed error costs less here than a false one.
/// </summary>
internal static class ExpressionLexer
{
    /// <summary>Reads all parts.</summary>
    /// <param name="parts">The parts.</param>
    /// <param name="problems">Gets a problem for each placeholder.</param>
    /// <returns>The tokens, the last of which is <see cref="TokenKind.End"/>.</returns>
    public static List<ExpressionToken> Read(IReadOnlyList<ExpressionPart> parts, List<ExpressionProblem> problems)
    {
        var tokens = new List<ExpressionToken>();
        for (var part = 0; part < parts.Count; part++)
        {
            if (parts[part].Hole == HoleKind.None)
            {
                ReadText(parts[part].Text, part, tokens, problems);
            }
            else
            {
                tokens.Add(new ExpressionToken(TokenKind.Hole, part, 0, 0));
            }
        }

        tokens.Add(new ExpressionToken(TokenKind.End, ExpressionProblem.WholeExpression, 0, 0));
        return tokens;
    }

    /// <summary>
    /// Tells whether a character can be part of a word. The runtime repeats this in <c>ExpressionWords.IsWordByte</c>, because it cannot reference an analyzer, and differs from
    /// 0x80 on: it reads UTF-8 and leaves a name that is not ASCII to DynamoDB. <c>ExpressionWordsTests</c> keeps the two in step.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for an ASCII letter or digit and for an underscore.</returns>
    internal static bool IsWordCharacter(char character) => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' || IsDigit(character);

    /// <summary>Tells whether a character is an ASCII digit, which no name starts with.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for 0 to 9.</returns>
    internal static bool IsDigit(char character) => character is >= '0' and <= '9';

    private static void ReadText(string text, int part, List<ExpressionToken> tokens, List<ExpressionProblem> problems)
    {
        var position = 0;
        while (position < text.Length)
        {
            var start = position;
            var kind = ReadToken(text, ref position);
            if (kind != TokenKind.Space)
            {
                tokens.Add(new ExpressionToken(kind, part, start, position - start));
            }

            if (kind is TokenKind.NamePlaceholder or TokenKind.ValuePlaceholder)
            {
                problems.Add(new ExpressionProblem(ExpressionProblemKind.Placeholder, part, start, position - start, text.Substring(start, position - start)));
            }
        }
    }

    private static TokenKind ReadToken(string text, ref int position)
    {
        var first = text[position++];
        switch (first)
        {
            case ' ' or '\t' or '\r' or '\n':
                return TokenKind.Space;
            case '(':
                return TokenKind.OpenParenthesis;
            case ')':
                return TokenKind.CloseParenthesis;
            case '[':
                return TokenKind.OpenBracket;
            case ']':
                return TokenKind.CloseBracket;
            case ',':
                return TokenKind.Comma;
            case '.':
                return TokenKind.Dot;
            case '+' or '-':
                return TokenKind.Sign;
            case '=':
                return TokenKind.Equal;
            case '<':
                SkipIf(text, ref position, '=', '>');
                return TokenKind.Comparator;
            case '>':
                SkipIf(text, ref position, '=', '=');
                return TokenKind.Comparator;
            case '#':
                return ReadPlaceholder(text, ref position, TokenKind.NamePlaceholder);
            case ':':
                return ReadPlaceholder(text, ref position, TokenKind.ValuePlaceholder);
            default:
                return ReadWordOrNumber(first, text, ref position);
        }
    }

    private static TokenKind ReadWordOrNumber(char first, string text, ref int position)
    {
        if (IsDigit(first))
        {
            while (position < text.Length && IsDigit(text[position]))
            {
                position++;
            }

            return TokenKind.Number;
        }

        if (!IsWordCharacter(first))
        {
            return TokenKind.Invalid;
        }

        SkipWord(text, ref position);
        return TokenKind.Word;
    }

    // A lone # or : is no placeholder, and DynamoDB rejects it as a character.
    private static TokenKind ReadPlaceholder(string text, ref int position, TokenKind kind)
    {
        var start = position;
        SkipWord(text, ref position);
        return position == start ? TokenKind.Invalid : kind;
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
