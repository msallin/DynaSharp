using System.Globalization;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A recursive descent parser for DynamoDB's expression grammar, to the trees of <see cref="ConditionNode"/>, <see cref="UpdateAction"/> and <see cref="DocumentPath"/>. It resolves
/// every placeholder as it meets it, so a missing one fails the parse and a used one counts for <see cref="PlaceholderScope.ThrowIfUnused"/>.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is the one DynamoDB Local accepts: keywords in any case, functions in lower case only, parentheses around any condition and any operand, <c>+</c> and <c>-</c> once per
/// <c>SET</c> value, each update section once. What it checks beyond the grammar is what DynamoDB checks before it reads an item: reserved words as bare names, the number of operands
/// of a function, a document path where a function needs one, the type of a constant where an operator needs one, the bounds of a <c>BETWEEN</c> of two constants, and overlapping paths.
/// </para>
/// <para>
/// The first error ends the parse with the message of DynamoDB, <c>Invalid ConditionExpression: Syntax error; token: "b", near: ":v b"</c> for a syntax error, where <c>near</c> spans the
/// token before and the token after.
/// </para>
/// </remarks>
internal sealed class ExpressionParser
{
    private const int MaxInOperands = 100;

    private static readonly string[] TypeNames = ["S", "N", "B", "BOOL", "NULL", "L", "M", "SS", "NS", "BS"];

    // The names of the types in the messages about ADD and DELETE. A set never needs one: both take a set.
    private static readonly Dictionary<AttributeType, string> LongTypeNames = new()
    {
        [AttributeType.String] = "STRING",
        [AttributeType.Number] = "NUMBER",
        [AttributeType.Binary] = "BINARY",
        [AttributeType.Boolean] = "BOOLEAN",
        [AttributeType.Null] = "NULL",
        [AttributeType.List] = "LIST",
        [AttributeType.Map] = "MAP",
    };

    // The types an operator or a function takes. Fields, so that no call site converts a method group, whose cached delegate is a branch of its own.
    private static readonly Func<AttributeType, bool> IsList = static type => type == AttributeType.List;
    private static readonly Func<AttributeType, bool> IsNumber = static type => type == AttributeType.Number;
    private static readonly Func<AttributeType, bool> IsString = static type => type == AttributeType.String;
    private static readonly Func<AttributeType, bool> IsStringOrBinary = static type => type is AttributeType.String or AttributeType.Binary;

    private static readonly Func<AttributeType, bool> IsScalarKey = static type => type is AttributeType.String or AttributeType.Number or AttributeType.Binary;
    private static readonly Func<AttributeType, bool> IsNumberOrSet = static type => type == AttributeType.Number || AttributeSets.IsSet(type);
    private static readonly Func<AttributeType, bool> IsSet = AttributeSets.IsSet;

    // What each function of the tokenizer's lists is, as an enum, so that the parser switches over a closed set and not over strings.
    private static readonly Dictionary<string, FunctionShape> Shapes = new(StringComparer.Ordinal)
    {
        ["size"] = FunctionShape.Size,
        ["attribute_exists"] = FunctionShape.AttributeExists,
        ["attribute_not_exists"] = FunctionShape.AttributeNotExists,
        ["attribute_type"] = FunctionShape.AttributeType,
        ["begins_with"] = FunctionShape.BeginsWith,
        ["contains"] = FunctionShape.Contains,
        ["if_not_exists"] = FunctionShape.IfNotExists,
        ["list_append"] = FunctionShape.ListAppend,
    };

    private readonly string _text;
    private readonly string _member;
    private readonly PlaceholderScope _scope;
    private readonly bool _inUpdate;
    private readonly List<ExpressionToken> _tokens;
    private int _index;

    private ExpressionParser(string text, string member, PlaceholderScope scope, bool inUpdate)
    {
        _text = text;
        _member = member;
        _scope = scope;
        _inUpdate = inUpdate;
        _tokens = ExpressionTokenizer.Tokenize(text);
    }

    // The functions with one operand come first.
    private enum FunctionShape
    {
        Size,
        AttributeExists,
        AttributeNotExists,
        AttributeType,
        BeginsWith,
        Contains,
        IfNotExists,
        ListAppend,
    }

    private ExpressionToken Current => _tokens[_index];

    /// <summary>Parses a condition: a <c>ConditionExpression</c>, a <c>FilterExpression</c> or a <c>KeyConditionExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="member">The member of the request, which every message names.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static ConditionNode ParseCondition(string text, string member, PlaceholderScope scope)
    {
        var parser = new ExpressionParser(text, member, scope, inUpdate: false);
        parser.ThrowIfEmpty();
        var condition = parser.RequireCondition(parser.ParseOr());
        parser.Expect(ExpressionTokenKind.End);
        return condition;
    }

    /// <summary>Parses an <c>UpdateExpression</c>: its sections in any order, each once, and the actions of each.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The actions, in the order of the expression.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static IReadOnlyList<UpdateAction> ParseUpdate(string text, PlaceholderScope scope)
    {
        var parser = new ExpressionParser(text, Expressions.UpdateExpression, scope, inUpdate: true);
        parser.ThrowIfEmpty();
        var actions = new List<UpdateAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.At(ExpressionTokenKind.End))
        {
            var section = (parser.Current.Kind == ExpressionTokenKind.Word
                ? Array.Find(ExpressionTokenizer.UpdateSections, candidate => candidate.Equals(parser.Text(parser.Current), StringComparison.OrdinalIgnoreCase))
                : null) ?? throw parser.Syntax(parser._index);

            if (!seen.Add(section))
            {
                throw FakeError.Validation($"Invalid {Expressions.UpdateExpression}: The \"{section}\" section can only be used once in an update expression;");
            }

            parser.Advance();
            do
            {
                actions.Add(parser.ParseAction(section));
            }
            while (parser.Accept(ExpressionTokenKind.Comma));
        }

        DocumentPath.ThrowIfOverlapping([.. actions.Select(static action => action.Path)], Expressions.UpdateExpression);
        return actions;
    }

    /// <summary>Parses a <c>ProjectionExpression</c>: document paths separated by commas, none of which touches another.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The paths, in the order of the expression.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static IReadOnlyList<DocumentPath> ParseProjection(string text, PlaceholderScope scope)
    {
        var parser = new ExpressionParser(text, Expressions.ProjectionExpression, scope, inUpdate: false);
        parser.ThrowIfEmpty();
        var paths = new List<DocumentPath>();
        do
        {
            paths.Add(parser.ParsePath());
        }
        while (parser.Accept(ExpressionTokenKind.Comma));

        parser.Expect(ExpressionTokenKind.End);
        DocumentPath.ThrowIfOverlapping(paths, Expressions.ProjectionExpression);
        return paths;
    }

    private static Comparator ComparatorOf(string text) => text switch
    {
        "=" => Comparator.Equal,
        "<>" => Comparator.NotEqual,
        "<" => Comparator.LessThan,
        "<=" => Comparator.LessThanOrEqual,
        ">" => Comparator.GreaterThan,
        _ => Comparator.GreaterThanOrEqual,
    };

    private static string Show(AttributeValue value) => value.Type switch
    {
        AttributeType.String => "{S:" + value.AsString() + "}",
        AttributeType.Number => "{N:" + value.AsNumber() + "}",
        AttributeType.Binary => "{B:" + Convert.ToBase64String(value.AsBinary().Span) + "}",
        _ => "{" + AttributeRules.Descriptor(value.Type) + "}",
    };

    private Term ParseOr()
    {
        var left = ParseAnd();
        while (AtKeyword("OR"))
        {
            var first = RequireCondition(left);
            Advance();
            left = Term.Of(new ConditionNode.Or(first, RequireCondition(ParseAnd())));
        }

        return left;
    }

    private Term ParseAnd()
    {
        var left = ParseNot();
        while (AtKeyword("AND"))
        {
            var first = RequireCondition(left);
            Advance();
            left = Term.Of(new ConditionNode.And(first, RequireCondition(ParseNot())));
        }

        return left;
    }

    private Term ParseNot()
    {
        if (!AtKeyword("NOT"))
        {
            return ParsePrimary();
        }

        Advance();
        return Term.Of(new ConditionNode.Not(RequireCondition(ParseNot())));
    }

    // A ( opens a condition or an operand, and only what follows the ) tells which: (a = :v) AND b = :w, or (a) = :v.
    private Term ParsePrimary()
    {
        Term left;
        if (Accept(ExpressionTokenKind.OpenParenthesis))
        {
            left = ParseOr();
            Expect(ExpressionTokenKind.CloseParenthesis);
        }
        else
        {
            left = ParseOperandTerm();
        }

        return left is { Condition: not null, IsTest: false } ? left : ParseComparison(left);
    }

    private Term ParseComparison(Term left)
    {
        var isComparator = At(ExpressionTokenKind.Equal) || At(ExpressionTokenKind.Comparator);
        if (!isComparator && !AtKeyword("BETWEEN") && !AtKeyword("IN"))
        {
            return left.IsTest ? Term.Of(left.Condition!) : left;
        }

        // attribute_exists(a) = :v: a function that is a condition is no operand.
        if (left.Operand is not { } operand)
        {
            throw Syntax(_index);
        }

        var keyword = Text(Current);
        Advance();
        if (isComparator)
        {
            var comparator = ComparatorOf(keyword);
            var right = ParseOperandInParentheses();
            if (comparator is not (Comparator.Equal or Comparator.NotEqual))
            {
                // An order holds between strings, numbers and binaries only; DynamoDB Local rejects s < :l with a list before the item (Vectors/conditions.json).
                RequireType(operand, keyword, IsScalarKey);
                RequireType(right, keyword, IsScalarKey);
            }

            return Term.Of(new ConditionNode.Compare(comparator, operand, right));
        }

        return Term.Of(keyword.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase) ? ParseBetween(operand) : ParseIn(operand));
    }

    private ConditionNode.Between ParseBetween(Operand operand)
    {
        var lower = ParseOperandInParentheses();
        if (!AtKeyword("AND"))
        {
            throw Syntax(_index);
        }

        Advance();
        var upper = ParseOperandInParentheses();
        if (lower.ConstantValue is { } low && upper.ConstantValue is { } high)
        {
            var bounds = $"lower bound operand: AttributeValue: {Show(low)}, upper bound operand: AttributeValue: {Show(high)}";
            if (low.Type != high.Type)
            {
                throw FakeError.Validation($"Invalid {_member}: The BETWEEN operator requires same data type for lower and upper bounds; {bounds}");
            }

            if (AttributeComparer.TryCompare(low, high, out var order) && order > 0)
            {
                throw FakeError.Validation($"Invalid {_member}: The BETWEEN operator requires upper bound to be greater than or equal to lower bound; {bounds}");
            }
        }

        return new ConditionNode.Between(operand, lower, upper);
    }

    private ConditionNode.In ParseIn(Operand operand)
    {
        Expect(ExpressionTokenKind.OpenParenthesis);
        var candidates = new List<Operand>();
        do
        {
            candidates.Add(ParseOperandInParentheses());
        }
        while (Accept(ExpressionTokenKind.Comma));

        Expect(ExpressionTokenKind.CloseParenthesis);
        if (candidates.Count > MaxInOperands)
        {
            throw FakeError.Validation($"Invalid {_member}: The IN operator is provided with too many operands; number of operands: {candidates.Count}");
        }

        return new ConditionNode.In(operand, candidates);
    }

    private Operand ParseOperandInParentheses()
    {
        if (Accept(ExpressionTokenKind.OpenParenthesis))
        {
            var inner = ParseOperandInParentheses();
            Expect(ExpressionTokenKind.CloseParenthesis);
            return inner;
        }

        var start = _index;
        return ParseOperandTerm().Operand ?? throw Syntax(start);
    }

    private Term ParseOperandTerm()
    {
        var token = Current;
        switch (token.Kind)
        {
            case ExpressionTokenKind.ValuePlaceholder:
                Advance();
                return Term.Of(new Operand.Constant(_scope.ResolveValue(Text(token), _member)));
            case ExpressionTokenKind.NamePlaceholder:
                return Term.Of(new Operand.Attribute(ParsePath()));
            case ExpressionTokenKind.Word when !IsKeyword(token):
                return _tokens[_index + 1].Kind == ExpressionTokenKind.OpenParenthesis ? ParseFunction() : Term.Of(new Operand.Attribute(ParsePath()));
            default:
                throw Syntax(_index);
        }
    }

    private Term ParseFunction()
    {
        var name = Text(Current);
        var ofCondition = ExpressionTokenizer.ConditionFunctions.Contains(name);
        var ofUpdate = ExpressionTokenizer.UpdateFunctions.Contains(name);
        if (!ofCondition && !ofUpdate)
        {
            throw FakeError.Validation($"Invalid {_member}: Invalid function name; function: {name}");
        }

        if (ofUpdate != _inUpdate)
        {
            throw FakeError.Validation($"Invalid {_member}: The function is not allowed in {(_inUpdate ? "an update" : "a condition")} expression; function: {name}");
        }

        Advance();
        Advance();
        var operands = new List<Operand>();
        do
        {
            operands.Add(ParseOperandInParentheses());
        }
        while (Accept(ExpressionTokenKind.Comma));

        Expect(ExpressionTokenKind.CloseParenthesis);
        var shape = Shapes[name];
        var expected = shape <= FunctionShape.AttributeNotExists ? 1 : 2;
        if (operands.Count != expected)
        {
            throw FakeError.Validation($"Invalid {_member}: Incorrect number of operands for operator or function; operator or function: {name}, number of operands: {operands.Count}");
        }

        return shape switch
        {
            FunctionShape.Size => Term.Of(new Operand.Size(operands[0])),
            FunctionShape.AttributeExists => Test(ConditionFunction.AttributeExists, name, PathOperand(operands[0], name)),
            FunctionShape.AttributeNotExists => Test(ConditionFunction.AttributeNotExists, name, PathOperand(operands[0], name)),
            FunctionShape.AttributeType => Test(ConditionFunction.AttributeType, name, PathOperand(operands[0], name), TypeName(operands[1])),
            FunctionShape.BeginsWith => Test(ConditionFunction.BeginsWith, name, RequireType(operands[0], name, IsStringOrBinary), RequireType(operands[1], name, IsStringOrBinary)),
            FunctionShape.Contains => Test(ConditionFunction.Contains, name, operands[0], operands[1]),
            FunctionShape.IfNotExists => Term.Of(new Operand.IfNotExists(PathOf(operands[0], name), operands[1])),
            _ => Term.Of(new Operand.ListAppend(RequireType(operands[0], name, IsList), RequireType(operands[1], name, IsList))),
        };

        static Term Test(ConditionFunction kind, string name, params Operand[] operands) => Term.Test(new ConditionNode.Function(kind, name, operands));
    }

    private DocumentPath PathOf(Operand operand, string function) =>
        operand is Operand.Attribute attribute
            ? attribute.Path
            : throw FakeError.Validation($"Invalid {_member}: Operator or function requires a document path; operator or function: {function}");

    private Operand.Attribute PathOperand(Operand operand, string function) => new(PathOf(operand, function));

    // A constant of a type the function cannot take fails before any item is read; a path is only known on the item.
    private Operand RequireType(Operand operand, string function, Func<AttributeType, bool> allowed) =>
        operand.ConstantValue is { } value && !allowed(value.Type)
            ? throw FakeError.Validation(
                $"Invalid {_member}: Incorrect operand type for operator or function; operator or function: {function}, operand type: {AttributeRules.Descriptor(value.Type)}")
            : operand;

    // attribute_type(a, :t) with :t a string that names a type: S, N, B, BOOL, NULL, L, M, SS, NS or BS.
    private Operand TypeName(Operand operand)
    {
        RequireType(operand, "attribute_type", IsString);
        if (operand.ConstantValue is { } value && !TypeNames.Contains(value.AsString()))
        {
            throw FakeError.Validation($"Invalid {_member}: Invalid attribute type name found; type: {value.AsString()}, valid types: {{ B,NULL,SS,BOOL,L,BS,N,NS,S,M }}");
        }

        return operand;
    }

    private UpdateAction ParseAction(string section)
    {
        var path = ParsePath();
        switch (section)
        {
            case "SET":
                Expect(ExpressionTokenKind.Equal);
                return new UpdateAction.Set(path, ParseSum().Operand);
            case "REMOVE":
                return new UpdateAction.Remove(path);
            case "ADD":
                return new UpdateAction.Add(path, ParseActionValue(section, IsNumberOrSet));
            default:
                return new UpdateAction.Delete(path, ParseActionValue(section, IsSet));
        }
    }

    // ADD and DELETE take a value placeholder and nothing else: ADD a :v, not ADD a (:v) nor ADD a b.
    private AttributeValue ParseActionValue(string section, Func<AttributeType, bool> allowed)
    {
        var token = Current;
        if (token.Kind != ExpressionTokenKind.ValuePlaceholder)
        {
            throw Syntax(_index);
        }

        Advance();
        var value = _scope.ResolveValue(Text(token), _member);
        return allowed(value.Type)
            ? value
            : throw FakeError.Validation($"Invalid {_member}: Incorrect operand type for operator or function; operator: {section}, operand type: {LongTypeNames[value.Type]}");
    }

    // DynamoDB takes one operator: b + c, (b + c) and (b) + (c), but neither b + c + d nor (b + c) + d.
    private (Operand Operand, bool IsSum) ParseSum()
    {
        Operand left;
        if (Accept(ExpressionTokenKind.OpenParenthesis))
        {
            var inner = ParseSum();
            Expect(ExpressionTokenKind.CloseParenthesis);
            if (inner.IsSum)
            {
                return inner;
            }

            left = inner.Operand;
        }
        else
        {
            left = ParseOperandInParentheses();
        }

        if (!At(ExpressionTokenKind.Sign))
        {
            return (left, false);
        }

        var sign = Text(Current);
        Advance();
        var right = ParseOperandInParentheses();
        return (new Operand.Sum(RequireType(left, sign, IsNumber), sign == "-", RequireType(right, sign, IsNumber)), true);
    }

    private DocumentPath ParsePath()
    {
        var segments = new List<PathSegment> { PathSegment.Of(ParseName()) };
        while (true)
        {
            if (Accept(ExpressionTokenKind.Dot))
            {
                segments.Add(PathSegment.Of(ParseName()));
            }
            else if (Accept(ExpressionTokenKind.OpenBracket))
            {
                segments.Add(PathSegment.At(ParseIndex()));
                Expect(ExpressionTokenKind.CloseBracket);
            }
            else
            {
                return new DocumentPath(segments);
            }
        }
    }

    private string ParseName()
    {
        var token = Current;
        if (token.Kind == ExpressionTokenKind.NamePlaceholder)
        {
            Advance();
            return _scope.ResolveName(Text(token), _member);
        }

        if (token.Kind != ExpressionTokenKind.Word || IsKeyword(token))
        {
            throw Syntax(_index);
        }

        var word = Text(token);
        if (ReservedWords.Contains(word))
        {
            throw FakeError.Validation($"Invalid {_member}: Attribute name is a reserved keyword; reserved keyword: {word}");
        }

        Advance();
        return word;
    }

    private int ParseIndex()
    {
        if (Current.Kind != ExpressionTokenKind.Number || !int.TryParse(Text(Current), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            throw Syntax(_index);
        }

        Advance();
        return index;
    }

    private ConditionNode RequireCondition(Term term) => term.Condition ?? throw Syntax(_index);

    private void ThrowIfEmpty()
    {
        if (At(ExpressionTokenKind.End))
        {
            throw FakeError.Validation($"Invalid {_member}: The expression can not be empty;");
        }
    }

    private bool IsKeyword(ExpressionToken token)
    {
        var word = Text(token);
        return ExpressionTokenizer.ConditionKeywords.Contains(word, StringComparer.OrdinalIgnoreCase)
            || (_inUpdate && ExpressionTokenizer.UpdateSections.Contains(word, StringComparer.OrdinalIgnoreCase));
    }

    private bool At(ExpressionTokenKind kind) => Current.Kind == kind;

    private bool AtKeyword(string keyword) => At(ExpressionTokenKind.Word) && Text(Current).Equals(keyword, StringComparison.OrdinalIgnoreCase);

    // The end token stays current once reached, so a rule that looks past it sees it again.
    private void Advance() => _index = Math.Min(_index + 1, _tokens.Count - 1);

    private bool Accept(ExpressionTokenKind kind)
    {
        if (!At(kind))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void Expect(ExpressionTokenKind kind)
    {
        if (!Accept(kind))
        {
            throw Syntax(_index);
        }
    }

    private string Text(ExpressionToken token) => _text.Substring(token.Start, token.Length);

    private FakeError Syntax(int index)
    {
        var token = _tokens[index];
        var from = index > 0 ? _tokens[index - 1].Start : token.Start;
        string shown;
        int to;
        if (token.Kind == ExpressionTokenKind.End)
        {
            shown = "<EOF>";
            to = _tokens[index - 1].End;
        }
        else
        {
            shown = Text(token);
            var next = _tokens[index + 1];
            to = next.Kind == ExpressionTokenKind.End ? token.End : next.End;
        }

        return FakeError.Validation($"Invalid {_member}: Syntax error; token: \"{shown}\", near: \"{_text[from..to]}\"");
    }

    // What a rule parsed: a condition, an operand, or a function that is a condition but has not yet shown that no comparator follows it.
    private readonly record struct Term(ConditionNode? Condition, Operand? Operand, bool IsTest)
    {
        public static Term Of(ConditionNode condition) => new(condition, null, false);

        public static Term Of(Operand operand) => new(null, operand, false);

        public static Term Test(ConditionNode condition) => new(condition, null, true);
    }
}
