using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The grammar on its own: what real expressions need, and what DynamoDB refuses.</summary>
public sealed class ExpressionParserTests
{
    private static readonly ExpressionPart NameHole = new(string.Empty, HoleKind.Name);

    private static readonly ExpressionPart ValueHole = new(string.Empty, HoleKind.Value);

    private static readonly ExpressionPart StringHole = new("status", HoleKind.StringConstant);

    [Test]
    public async Task Check_ConditionsOfTheDocumentation_HaveNoSyntaxProblem()
    {
        // Arrange
        var expressions = ExpressionCorpus.DocumentedConditions;

        // Act
        var refused = expressions.Where(expression => HasSyntaxProblem(ExpressionKind.Condition, expression));

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task Check_UpdatesOfTheDocumentation_HaveNoSyntaxProblem()
    {
        // Arrange
        var expressions = ExpressionCorpus.DocumentedUpdates;

        // Act
        var refused = expressions.Where(expression => HasSyntaxProblem(ExpressionKind.Update, expression));

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task Check_ConditionsDynamoDbLocalAccepts_HaveNoSyntaxProblem()
    {
        // Arrange
        var expressions = ExpressionCorpus.AcceptedConditions;

        // Act
        var refused = expressions.Where(expression => HasSyntaxProblem(ExpressionKind.Condition, expression));

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task Check_UpdatesDynamoDbLocalAccepts_HaveNoSyntaxProblem()
    {
        // Arrange
        var expressions = ExpressionCorpus.AcceptedUpdates;

        // Act
        var refused = expressions.Where(expression => HasSyntaxProblem(ExpressionKind.Update, expression));

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task Check_ConditionsDynamoDbLocalRejects_HaveASyntaxProblem()
    {
        // Arrange
        var expressions = ExpressionCorpus.RejectedConditions;

        // Act
        var accepted = expressions.Where(expression => !HasSyntaxProblem(ExpressionKind.Condition, expression));

        // Assert
        await Assert.That(accepted).IsEmpty();
    }

    [Test]
    public async Task Check_UpdatesDynamoDbLocalRejects_HaveASyntaxProblem()
    {
        // Arrange
        var expressions = ExpressionCorpus.RejectedUpdates;

        // Act
        var accepted = expressions.Where(expression => !HasSyntaxProblem(ExpressionKind.Update, expression));

        // Assert
        await Assert.That(accepted).IsEmpty();
    }

    [Test]
    public async Task Check_ExpressionsOfTheOperationVectors_HaveNoSyntaxProblem()
    {
        // Arrange
        var expressions = VectorExpressions.All();

        // Act
        var refused = expressions.Where(static vector => HasSyntaxProblem(vector.Kind, vector.Text));

        // Assert
        await Assert.That(expressions.Count).IsGreaterThan(50);
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task Check_InWithOneHundredOperands_HasNoProblem()
    {
        // Arrange
        var expression = "a IN (" + string.Join(", ", Enumerable.Repeat(":v", 100)) + ")";

        // Act
        var problems = Problems(ExpressionKind.Condition, expression, withoutPlaceholders: true);

        // Assert
        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task Check_InWithOneHundredAndOneOperands_NamesTheCount()
    {
        // Arrange
        var expression = "a IN (" + string.Join(", ", Enumerable.Repeat(":v", 101)) + ")";

        // Act
        var problems = Problems(ExpressionKind.Condition, expression, withoutPlaceholders: true);

        // Assert
        await Assert.That(problems).IsEquivalentTo([new ExpressionProblem(ExpressionProblemKind.Syntax, 0, 2, 2, "IN takes 1 to 100 operands, and this one has 101")]);
    }

    [Test]
    [Arguments("attribute_exists(a", "a parenthesis is not closed")]
    [Arguments("(a = b", "a parenthesis is not closed")]
    [Arguments("a BETWEEN b", "BETWEEN needs AND between its bounds")]
    [Arguments("a IN b", "IN needs its operands in parentheses")]
    [Arguments("atribute_exists(a)", "unknown function 'atribute_exists'; function names are case sensitive")]
    [Arguments("SIZE(a) > b", "unknown function 'SIZE'; function names are case sensitive")]
    [Arguments("list_append(a, b) = c", "'list_append' is not allowed in a condition")]
    [Arguments("size(a, b) > c", "'size' takes 1 operands, not 2")]
    [Arguments("attribute_exists(a) = b", "a function that is a condition cannot be compared")]
    [Arguments("a = contains(b, c)", "'contains' is a condition, so it cannot be an operand")]
    [Arguments("a", "a condition needs a comparator or a function")]
    [Arguments("size(a)", "a condition needs a comparator or a function")]
    [Arguments("a = 5", "a number cannot be written in the text; put it in a hole")]
    [Arguments("a != b", "unexpected '!'")]
    [Arguments("a = b c", "unexpected 'c'")]
    [Arguments("a = ", "the expression ends too early")]
    [Arguments("a[b] = c", "a list index is a number")]
    [Arguments("a[0 = c", "a list index is not closed")]
    public async Task Check_InvalidCondition_SaysWhat(string expression, string reason)
    {
        // Arrange
        var kind = ExpressionKind.Condition;

        // Act
        var problems = Problems(kind, expression);

        // Assert
        await Assert.That(problems.Select(static problem => problem.Detail)).IsEquivalentTo([reason]);
    }

    [Test]
    [Arguments("SET a = size(b)", "'size' is not allowed in an update")]
    [Arguments("SET a b", "a SET action needs =")]
    [Arguments("ADD a b", "ADD and DELETE need a value after the path")]
    [Arguments("REMOVE a REMOVE b", "the REMOVE section can only be used once")]
    [Arguments("a = b", "unexpected 'a'")]
    [Arguments("(a)", "unexpected '('")]
    [Arguments("", "the expression ends too early")]
    public async Task Check_InvalidUpdate_SaysWhat(string expression, string reason)
    {
        // Arrange
        var kind = ExpressionKind.Update;

        // Act
        var problems = Problems(kind, expression);

        // Assert
        await Assert.That(problems.Select(static problem => problem.Detail)).IsEquivalentTo([reason]);
    }

    [Test]
    public async Task Check_SetActionWithItsKeyword_IsAnError()
    {
        // Arrange: Set($"SET a = 1") renders SET SET a = :v0.
        var kind = ExpressionKind.SetAction;

        // Act
        var problems = Problems(kind, "SET a = b");

        // Assert
        await Assert.That(problems).IsEquivalentTo([new ExpressionProblem(ExpressionProblemKind.Syntax, 0, 0, 3, "unexpected 'SET'")]);
    }

    [Test]
    [Arguments("a = b")]
    [Arguments("a = b, c = d")]
    [Arguments("a = list_append(a, b)")]
    [Arguments("a = a + b")]
    [Arguments("a = b REMOVE c")]
    [Arguments("a = b REMOVE c ADD d :v DELETE e :w")]
    public async Task Check_SetActionWithoutItsKeyword_HasNoSyntaxProblem(string expression)
    {
        // Arrange
        var kind = ExpressionKind.SetAction;

        // Act
        var refused = HasSyntaxProblem(kind, expression);

        // Assert
        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task Check_SetActionWithASecondSetSection_IsAnError()
    {
        // Arrange
        var kind = ExpressionKind.SetAction;

        // Act
        var problems = Problems(kind, "a = b SET c = d");

        // Assert
        await Assert.That(problems.Select(static problem => problem.Detail)).IsEquivalentTo(["the SET section can only be used once"]);
    }

    [Test]
    public async Task Check_UnknownKind_ReportsPlaceholdersAndNoGrammar()
    {
        // Arrange: a wrapper of the consumer may take a condition or an action, so neither grammar applies.
        var kind = ExpressionKind.Unknown;

        // Act
        var problems = Problems(kind, "this is ( no #grammar");

        // Assert
        await Assert.That(problems).IsEquivalentTo([new ExpressionProblem(ExpressionProblemKind.Placeholder, 0, 13, 8, "#grammar")]);
    }

    [Test]
    public async Task Check_Placeholders_AreAllReportedWithTheirPlace()
    {
        // Arrange
        ExpressionPart[] parts = [ExpressionPart.Literal("#n = "), ValueHole, ExpressionPart.Literal(" AND b = :v1")];

        // Act
        var problems = ExpressionParser.Check(ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems).IsEquivalentTo(
        [
            new ExpressionProblem(ExpressionProblemKind.Placeholder, 0, 0, 2, "#n"),
            new ExpressionProblem(ExpressionProblemKind.Placeholder, 2, 9, 3, ":v1"),
        ]);
    }

    [Test]
    public async Task Check_LonePlaceholderCharacters_AreSyntaxNotPlaceholders()
    {
        // Arrange
        var kind = ExpressionKind.Condition;

        // Act
        var problems = Problems(kind, "# = :");

        // Assert
        await Assert.That(problems).IsEquivalentTo([new ExpressionProblem(ExpressionProblemKind.Syntax, 0, 0, 1, "unexpected '#'")]);
    }

    [Test]
    [Arguments(false, "{0} = {1}")]
    [Arguments(false, "{0} <> {1}")]
    [Arguments(false, "{0} BETWEEN {1} AND {1}")]
    [Arguments(false, "{0} IN ({1})")]
    [Arguments(false, "({0}) = {1}")]
    [Arguments(false, "(({0})) = {1}")]
    [Arguments(false, "attribute_exists({0})")]
    [Arguments(false, "attribute_not_exists({0})")]
    [Arguments(false, "attribute_type({0}, {1})")]
    [Arguments(false, "begins_with({0}, {1})")]
    [Arguments(false, "contains({0}, {1})")]
    [Arguments(false, "size({0}) > {1}")]
    [Arguments(false, "{0}.b = {1}")]
    [Arguments(false, "{0}[1] = {1}")]
    [Arguments(false, "a.{0} = {1}")]
    [Arguments(false, "{1} = {1} AND {0} = {1}")]
    [Arguments(true, "{0} = {1}")]
    [Arguments(true, "a = {0} + {1}")]
    [Arguments(true, "a = ({0}) - {1}")]
    [Arguments(true, "a = if_not_exists({0}, {1})")]
    [Arguments(true, "a = {1} REMOVE {0}")]
    [Arguments(true, "a = {1} ADD {0} {1}")]
    public async Task Check_StringInANamePosition_IsFlaggedOnce(bool inSet, string template)
    {
        // Arrange
        var parts = Parts(template, StringHole);

        // Act
        var problems = ExpressionParser.Check(inSet ? ExpressionKind.SetAction : ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems.Select(problem => (problem.Kind, parts[problem.Part].Text))).IsEquivalentTo([(ExpressionProblemKind.StringInNamePosition, "status")]);
    }

    [Test]
    [Arguments(false, "a = {0}")]
    [Arguments(false, "a = ({0})")]
    [Arguments(false, "a BETWEEN {0} AND {0}")]
    [Arguments(false, "a IN ({0}, {0})")]
    [Arguments(false, "begins_with(a, {0})")]
    [Arguments(false, "contains(a, {0})")]
    [Arguments(false, "attribute_type(a, {0})")]
    [Arguments(true, "a = {0}")]
    [Arguments(true, "a = b + {0}")]
    [Arguments(true, "a = if_not_exists(a, {0})")]
    [Arguments(true, "a = list_append({0}, b)")]
    [Arguments(true, "a = b ADD c {0}")]
    public async Task Check_StringInAValuePosition_IsFine(bool inSet, string template)
    {
        // Arrange
        var parts = Parts(template, StringHole);

        // Act
        var problems = ExpressionParser.Check(inSet ? ExpressionKind.SetAction : ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems).IsEmpty();
    }

    [Test]
    [Arguments(false, "{0} = {0}")]
    [Arguments(false, "attribute_exists({0})")]
    [Arguments(false, "{0}.b = {0}")]
    [Arguments(false, "size({0}.b[1]) > {0}")]
    [Arguments(true, "{0} = {0} + {0}")]
    [Arguments(true, "{0}.b = {0}")]
    [Arguments(true, "{0} = {0} REMOVE {0}")]
    public async Task Check_HolesThatAreNotConstant_AreOperandsWherever(bool inSet, string template)
    {
        // Arrange: a name or a variable at run time, which the grammar cannot know.
        var parts = Parts(template, ValueHole);

        // Act
        var problems = ExpressionParser.Check(inSet ? ExpressionKind.SetAction : ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task Check_NameAsTheValueOfAdd_IsAnError()
    {
        // Arrange: ADD a #n0 is a syntax error, unlike SET a = #n0.
        var parts = Parts("a = b ADD c {0}", NameHole);

        // Act
        var problems = ExpressionParser.Check(ExpressionKind.SetAction, parts);

        // Assert
        await Assert.That(problems.Select(static problem => (problem.Kind, problem.Part))).IsEquivalentTo([(ExpressionProblemKind.Syntax, 1)]);
    }

    [Test]
    public async Task Check_HoleWhereNoOperandFits_PointsAtTheHole()
    {
        // Arrange
        var parts = Parts("{0} {0}", NameHole);

        // Act
        var problems = ExpressionParser.Check(ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems).IsEquivalentTo([new ExpressionProblem(ExpressionProblemKind.Syntax, 2, 0, 0, "a hole cannot stand here")]);
    }

    [Test]
    public async Task Check_ExpressionThatEndsEarly_HasNoPlace()
    {
        // Arrange
        var parts = Parts("{0} = ", NameHole);

        // Act
        var problems = ExpressionParser.Check(ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems.Select(static problem => problem.Part)).IsEquivalentTo([ExpressionProblem.WholeExpression]);
    }

    [Test]
    public async Task Check_WordAndHoleThatTouch_IsAnError()
    {
        // Arrange: a#n0 is two tokens for DynamoDB.
        var parts = Parts("a{0} = b", NameHole);

        // Act
        var problems = ExpressionParser.Check(ExpressionKind.Condition, parts);

        // Assert
        await Assert.That(problems.Select(static problem => problem.Kind)).IsEquivalentTo([ExpressionProblemKind.Syntax]);
    }

    private static bool HasSyntaxProblem(ExpressionKind kind, string expression) =>
        Problems(kind, expression).Any(static problem => problem.Kind == ExpressionProblemKind.Syntax);

    private static List<ExpressionProblem> Problems(ExpressionKind kind, string expression, bool withoutPlaceholders = false)
    {
        var problems = ExpressionParser.Check(kind, [ExpressionPart.Literal(expression)]);
        return withoutPlaceholders ? [.. problems.Where(static problem => problem.Kind != ExpressionProblemKind.Placeholder)] : problems;
    }

    // "{0} = {1}" with a hole for {0} and a variable for {1}.
    private static List<ExpressionPart> Parts(string template, ExpressionPart hole)
    {
        var parts = new List<ExpressionPart>();
        var rest = template;
        while (rest.Length > 0)
        {
            var open = rest.IndexOf('{', StringComparison.Ordinal);
            if (open < 0)
            {
                parts.Add(ExpressionPart.Literal(rest));
                break;
            }

            if (open > 0)
            {
                parts.Add(ExpressionPart.Literal(rest[..open]));
            }

            parts.Add(rest[open + 1] == '0' ? hole : ValueHole);
            rest = rest[(open + 3)..];
        }

        return parts;
    }
}
