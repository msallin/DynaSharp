using System.Globalization;

namespace DynaSharp.Generators;

/// <summary>A recursive descent parser for DynamoDB's condition and update grammar. It only checks: the result is the list of problems.</summary>
/// <remarks>
/// <para>
/// A false error breaks a build and a missed one only moves the error to run time, so the grammar accepts everything DynamoDB might. Its rules
/// were run against DynamoDB Local, which reports the service's own messages. What it accepts here and DynamoDB rejects for other reasons is
/// meant: redundant parentheses and operand types. A reserved word as a bare name is no syntax error here but a problem of its own, DYNA220, a warning. A hole is an operand wherever it stands, because what it holds at run time
/// is not known; only a constant string is looked at.
/// </para>
/// <para>
/// The first syntax error ends the parse, because what follows it is noise. After it, <see cref="Current"/> stays on the end token, so every rule
/// falls through without a check of its own.
/// </para>
/// </remarks>
internal sealed class ExpressionParser
{
    /// <summary>
    /// The keywords of the grammar. The runtime repeats those of a condition in <c>ExpressionWriter.ConditionKeywords</c>, because it cannot reference an analyzer.
    /// <c>ExpressionWriterTests</c> keeps the two in step: a change here needs a change there, or a reason in that test.
    /// </summary>
    internal static readonly string[] Keywords = ["AND", "OR", "NOT", "BETWEEN", "IN", "SET", "REMOVE", "ADD", "DELETE"];

    private const int MaxInOperands = 100;

    private const string TooEarly = "the expression ends too early";

    private const string NoComparison = "a condition needs a comparator or a function";

    private const string Unclosed = "a parenthesis is not closed";

    private readonly IReadOnlyList<ExpressionPart> _parts;

    private readonly List<ExpressionToken> _tokens;

    private readonly List<ExpressionProblem> _problems;

    private readonly bool _inUpdate;

    private readonly bool _withNames;

    private int _index;

    private bool _failed;

    private ExpressionParser(IReadOnlyList<ExpressionPart> parts, List<ExpressionToken> tokens, List<ExpressionProblem> problems, bool inUpdate, bool withNames)
    {
        _parts = parts;
        _tokens = tokens;
        _problems = problems;
        _inUpdate = inUpdate;
        _withNames = withNames;
    }

    [Flags]
    private enum Section
    {
        None = 0,
        Set = 1,
        Remove = 2,
        Add = 4,
        Delete = 8,
    }

    private enum NodeKind
    {
        Condition,
        Operand,

        // A function that can be a condition by itself.
        Test,
    }

    private ExpressionToken Current => _tokens[_index];

    /// <summary>Checks an expression.</summary>
    /// <param name="kind">The grammar to read it with.</param>
    /// <param name="parts">The parts. Literal parts never touch, as a word could span them.</param>
    /// <param name="withNames">Whether to add a <see cref="ExpressionProblemKind.BareName"/> for each bare word a path starts with, for a caller that knows the attributes.</param>
    /// <returns>The placeholders first, then the rest as the parser met it.</returns>
    public static List<ExpressionProblem> Check(ExpressionKind kind, IReadOnlyList<ExpressionPart> parts, bool withNames = false)
    {
        var problems = new List<ExpressionProblem>();
        var tokens = ExpressionLexer.Read(parts, problems);
        var parser = new ExpressionParser(parts, tokens, problems, kind != ExpressionKind.Condition, withNames);
        switch (kind)
        {
            case ExpressionKind.Condition:
                parser.ParseCondition();
                break;
            case ExpressionKind.Update:
                parser.ParseUpdate(Section.None);
                break;
            case ExpressionKind.SetAction:
                parser.ParseUpdate(Section.Set);
                break;
        }

        return problems;
    }

    private static Section SectionOf(string word) => word.ToUpperInvariant() switch
    {
        "SET" => Section.Set,
        "REMOVE" => Section.Remove,
        "ADD" => Section.Add,
        "DELETE" => Section.Delete,
        _ => Section.None,
    };

    private void ParseCondition()
    {
        RequireCondition(ParseOr());
        if (!At(TokenKind.End))
        {
            Fail(Current, Unexpected(Current));
        }
    }

    private Node ParseOr()
    {
        var left = ParseAnd();
        while (AtKeyword("OR"))
        {
            RequireCondition(left);
            Advance();
            RequireCondition(ParseAnd());
            left = Node.Condition;
        }

        return left;
    }

    private Node ParseAnd()
    {
        var left = ParseNot();
        while (AtKeyword("AND"))
        {
            RequireCondition(left);
            Advance();
            RequireCondition(ParseNot());
            left = Node.Condition;
        }

        return left;
    }

    private Node ParseNot()
    {
        if (!AtKeyword("NOT"))
        {
            return ParsePrimary();
        }

        Advance();
        RequireCondition(ParseNot());
        return Node.Condition;
    }

    // ( can open a condition or an operand, and only what follows the ) tells which: (a = :v) AND b = :w, or (a) = :v.
    private Node ParsePrimary()
    {
        Node left;
        if (At(TokenKind.OpenParenthesis))
        {
            Advance();
            left = ParseOr();
            Expect(TokenKind.CloseParenthesis, Unclosed);
        }
        else
        {
            left = ParseOperand();
        }

        return left.Kind == NodeKind.Condition ? left : ParseComparison(left);
    }

    private Node ParseComparison(Node left)
    {
        var isComparator = At(TokenKind.Equal) || At(TokenKind.Comparator);
        var isBetween = AtKeyword("BETWEEN");
        var isIn = AtKeyword("IN");
        if (!isComparator && !isBetween && !isIn)
        {
            return left.Kind == NodeKind.Test ? Node.Condition : left;
        }

        var operation = Current;
        if (left.Kind == NodeKind.Test)
        {
            Fail(operation, "a function that is a condition cannot be compared");
            return Node.Condition;
        }

        Flag(left);
        Advance();
        if (isComparator)
        {
            ParseOperandInParentheses();
        }
        else if (isBetween)
        {
            ParseBetween();
        }
        else
        {
            ParseIn(operation);
        }

        return Node.Condition;
    }

    private void ParseBetween()
    {
        ParseOperandInParentheses();
        if (AtKeyword("AND"))
        {
            Advance();
        }
        else
        {
            Fail(Current, "BETWEEN needs AND between its bounds");
        }

        ParseOperandInParentheses();
    }

    private void ParseIn(ExpressionToken keyword)
    {
        Expect(TokenKind.OpenParenthesis, "IN needs its operands in parentheses");
        var count = 0;
        do
        {
            ParseOperandInParentheses();
            count++;
        }
        while (Accept(TokenKind.Comma));

        Expect(TokenKind.CloseParenthesis, Unclosed);
        if (count > MaxInOperands)
        {
            Fail(keyword, "IN takes 1 to 100 operands, and this one has " + count.ToString(CultureInfo.InvariantCulture));
        }
    }

    private Node ParseOperandInParentheses()
    {
        if (At(TokenKind.OpenParenthesis))
        {
            Advance();
            var inner = ParseOperandInParentheses();
            Expect(TokenKind.CloseParenthesis, Unclosed);
            return inner;
        }

        var first = Current;
        var operand = ParseOperand();
        if (operand.Kind == NodeKind.Test)
        {
            Fail(first, "'" + TextOf(first) + "' is a condition, so it cannot be an operand");
        }

        return operand;
    }

    private Node ParseOperand()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Hole:
                return ParseHole(token);
            case TokenKind.NamePlaceholder:
                Advance();
                ParseTail();
                return Node.Operand;
            case TokenKind.ValuePlaceholder:
                Advance();
                return Node.Operand;
            case TokenKind.Word when !IsKeyword(token):
                Advance();
                if (At(TokenKind.OpenParenthesis))
                {
                    return ParseFunction(token);
                }

                NoteName(token, isRoot: true);
                ParseTail();
                return Node.Operand;
            default:
                Fail(token, Unexpected(token));
                return Node.Operand;
        }
    }

    // {"address"}.city renders :v0.city, so a constant string with a path after it can only be a name.
    private Node ParseHole(ExpressionToken token)
    {
        Advance();
        var hole = HoleNode(token);
        if (!At(TokenKind.Dot) && !At(TokenKind.OpenBracket))
        {
            return hole;
        }

        Flag(hole);
        ParseTail();
        return Node.Operand;
    }

    private Node ParseFunction(ExpressionToken name)
    {
        var text = TextOf(name);
        if (!ExpressionFunction.All.TryGetValue(text, out var function))
        {
            Fail(name, "unknown function '" + text + "'; function names are case sensitive");
            return Node.Operand;
        }

        if (function.InUpdate != _inUpdate)
        {
            Fail(name, "'" + text + "' is not allowed in " + (_inUpdate ? "an update" : "a condition"));
            return Node.Operand;
        }

        Advance();
        var count = 0;
        do
        {
            var operand = ParseOperandInParentheses();
            if (count == 0 && function.HasPath)
            {
                Flag(operand);
            }

            count++;
        }
        while (Accept(TokenKind.Comma));

        Expect(TokenKind.CloseParenthesis, Unclosed);
        if (count != function.Operands)
        {
            Fail(name, "'" + text + "' takes " + function.Operands.ToString(CultureInfo.InvariantCulture) + " operands, not " + count.ToString(CultureInfo.InvariantCulture));
        }

        return function.IsTest ? Node.Test : Node.Operand;
    }

    private void ParseTail()
    {
        while (true)
        {
            if (At(TokenKind.Dot))
            {
                Advance();
                ParseName(isRoot: false);
            }
            else if (At(TokenKind.OpenBracket))
            {
                Advance();
                Expect(TokenKind.Number, "a list index is a number");
                Expect(TokenKind.CloseBracket, "a list index is not closed");
            }
            else
            {
                return;
            }
        }
    }

    private void ParsePath()
    {
        ParseName(isRoot: true);
        ParseTail();
    }

    private void ParseName(bool isRoot)
    {
        var token = Current;
        if (token.Kind == TokenKind.Hole)
        {
            Flag(HoleNode(token));
            Advance();
        }
        else if (token.Kind == TokenKind.NamePlaceholder)
        {
            Advance();
        }
        else if (token.Kind == TokenKind.Word && !IsKeyword(token))
        {
            NoteName(token, isRoot);
            Advance();
        }
        else
        {
            Fail(token, Unexpected(token));
        }
    }

    private void ParseUpdate(Section first)
    {
        var seen = first;
        if (first == Section.Set)
        {
            ParseSetActions();
        }

        while (!At(TokenKind.End))
        {
            var token = Current;
            var section = token.Kind == TokenKind.Word ? SectionOf(TextOf(token)) : Section.None;
            if (section == Section.None)
            {
                Fail(token, Unexpected(token));
            }
            else if ((seen & section) != 0)
            {
                Fail(token, "the " + TextOf(token) + " section can only be used once");
            }
            else
            {
                seen |= section;
                Advance();
                ParseSection(section);
            }
        }

        if (seen == Section.None)
        {
            Fail(Current, TooEarly);
        }
    }

    private void ParseSection(Section section)
    {
        switch (section)
        {
            case Section.Set:
                ParseSetActions();
                break;
            case Section.Remove:
                do
                {
                    ParsePath();
                }
                while (Accept(TokenKind.Comma));
                break;
            default:
                do
                {
                    ParsePath();
                    ParseActionValue();
                }
                while (Accept(TokenKind.Comma));
                break;
        }
    }

    private void ParseSetActions()
    {
        do
        {
            ParsePath();
            Expect(TokenKind.Equal, "a SET action needs =");
            ParseSum();
        }
        while (Accept(TokenKind.Comma));
    }

    // DynamoDB takes one operator: b + c, (b + c) and (b) + (c), but neither b + c + d nor (b + c) + d.
    private (Node Operand, bool IsSum) ParseSum()
    {
        Node left;
        if (At(TokenKind.OpenParenthesis))
        {
            Advance();
            var inner = ParseSum();
            Expect(TokenKind.CloseParenthesis, Unclosed);
            if (inner.IsSum)
            {
                return inner;
            }

            left = inner.Operand;
        }
        else
        {
            left = ParseOperand();
        }

        if (!At(TokenKind.Sign))
        {
            return (left, false);
        }

        Flag(left);
        Advance();
        ParseOperandInParentheses();
        return (Node.Operand, true);
    }

    // ADD and DELETE take a value, which in an expression can only be a placeholder.
    private void ParseActionValue()
    {
        var token = Current;
        if (token.Kind == TokenKind.ValuePlaceholder || (token.Kind == TokenKind.Hole && _parts[token.Part].Hole != HoleKind.Name))
        {
            Advance();
        }
        else
        {
            Fail(token, "ADD and DELETE need a value after the path");
        }
    }

    private Node HoleNode(ExpressionToken token) => _parts[token.Part].Hole == HoleKind.StringConstant ? Node.StringHole(token.Part) : Node.Operand;

    private bool At(TokenKind kind) => Current.Kind == kind;

    private bool AtKeyword(string keyword) => Current.Kind == TokenKind.Word && IsWord(Current, keyword);

    private bool IsKeyword(ExpressionToken token)
    {
        foreach (var keyword in Keywords)
        {
            if (IsWord(token, keyword))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWord(ExpressionToken token, string keyword) =>
        token.Length == keyword.Length && string.Compare(_parts[token.Part].Text, token.Start, keyword, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private void Advance()
    {
        if (_index < _tokens.Count - 1)
        {
            _index++;
        }
    }

    private bool Accept(TokenKind kind)
    {
        if (!At(kind))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void Expect(TokenKind kind, string reason)
    {
        if (!Accept(kind))
        {
            Fail(Current, reason);
        }
    }

    private void RequireCondition(Node node)
    {
        if (node.Kind != NodeKind.Condition)
        {
            Fail(Current, At(TokenKind.End) ? NoComparison : Unexpected(Current));
        }
    }

    private void Flag(Node node)
    {
        if (node.StringPart >= 0)
        {
            _problems.Add(new ExpressionProblem(ExpressionProblemKind.StringInNamePosition, node.StringPart, 0, 0, _parts[node.StringPart].Text));
        }
    }

    // A word in the place of a name: status in status = :v and in a.status = :v. DynamoDB rejects a reserved one wherever it stands, and only the first name of a path is an attribute of the item.
    // The function name size is the exception: DynamoDB takes it as a name in any case where no parenthesis follows.
    private void NoteName(ExpressionToken token, bool isRoot)
    {
        var text = _parts[token.Part].Text;
        if (ReservedWords.Contains(text, token.Start, token.Length) && !(token.Length == 4 && string.Compare(text, token.Start, "size", 0, 4, StringComparison.OrdinalIgnoreCase) == 0))
        {
            _problems.Add(new ExpressionProblem(ExpressionProblemKind.ReservedWord, token.Part, token.Start, token.Length, TextOf(token)));
        }
        else if (isRoot && _withNames)
        {
            _problems.Add(new ExpressionProblem(ExpressionProblemKind.BareName, token.Part, token.Start, token.Length, TextOf(token)));
        }
    }

    private void Fail(ExpressionToken token, string reason)
    {
        if (!_failed)
        {
            _failed = true;
            _problems.Add(new ExpressionProblem(ExpressionProblemKind.Syntax, token.Part, token.Start, token.Length, reason));
            _index = _tokens.Count - 1;
        }
    }

    private string Unexpected(ExpressionToken token) => token.Kind switch
    {
        TokenKind.End => TooEarly,
        TokenKind.Hole => "a hole cannot stand here",
        TokenKind.Number => "a number cannot be written in the text; put it in a hole",
        _ => "unexpected '" + TextOf(token) + "'",
    };

    private string TextOf(ExpressionToken token) => _parts[token.Part].Text.Substring(token.Start, token.Length);

    // StringPart is the part of a constant string that is the whole operand, otherwise -1.
    private readonly record struct Node(NodeKind Kind, int StringPart)
    {
        public static Node Condition { get; } = new(NodeKind.Condition, -1);

        public static Node Operand { get; } = new(NodeKind.Operand, -1);

        public static Node Test { get; } = new(NodeKind.Test, -1);

        public static Node StringHole(int part) => new(NodeKind.Operand, part);
    }
}
