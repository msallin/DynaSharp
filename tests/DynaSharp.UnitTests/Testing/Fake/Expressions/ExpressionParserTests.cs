using DynaSharp.Testing.Fake;
using DynaSharp.UnitTests.Generators.Analyzers;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The verdicts of the corpus are those of the Developer Guide and of DynamoDB Local 3.3.2, see ExpressionCorpus. The messages are DynamoDB's, as the Developer Guide and
// DynamoDB Local quote them: the code is exact, the text best effort.
public sealed class ExpressionParserTests
{
    private const string ReservedKeyword = "Attribute name is a reserved keyword";

    // A value of each of these types makes every accepted update of the corpus valid, whatever its operators need: numbers for + and ADD, sets for ADD and DELETE, lists for list_append.
    private static readonly string[] UpdateValues = ["""{"N":"1"}""", """{"SS":["S"]}""", """{"L":[]}"""];

    [Test]
    public async Task ParseCondition_DocumentedConditions_Parse()
    {
        // Arrange
        var expressions = ExpressionCorpus.DocumentedConditions;

        // Act
        var refused = expressions.Where(static expression => ConditionError(expression) is not null);

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task ParseCondition_ConditionsDynamoDbLocalAccepts_ParseOrNameAReservedWord()
    {
        // Arrange (DynamoDB Local's grammar takes these; two name the reserved word size bare, which DynamoDB rejects)
        var expressions = ExpressionCorpus.AcceptedConditions;

        // Act
        var refused = expressions.Select(static expression => (expression, Error: ConditionError(expression))).Where(static result => result.Error is not null).ToList();

        // Assert
        await Assert.That(refused.All(static result => result.Error!.Contains(ReservedKeyword, StringComparison.Ordinal))).IsTrue();
        await Assert.That(refused.Select(static result => result.expression)).IsEquivalentTo(["a.size = :v", "size = :v"]);
    }

    [Test]
    public async Task ParseCondition_ConditionsDynamoDbLocalRejects_Throw()
    {
        // Arrange
        var expressions = ExpressionCorpus.RejectedConditions;

        // Act
        var accepted = expressions.Where(static expression => ConditionError(expression) is null);

        // Assert
        await Assert.That(accepted).IsEmpty();
    }

    [Test]
    public async Task ParseUpdate_DocumentedUpdates_Parse()
    {
        // Arrange
        var expressions = ExpressionCorpus.DocumentedUpdates;

        // Act
        var refused = expressions.Where(static expression => UpdateValues.All(value => UpdateError(expression, value) is not null));

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task ParseUpdate_UpdatesDynamoDbLocalAccepts_Parse()
    {
        // Arrange
        var expressions = ExpressionCorpus.AcceptedUpdates;

        // Act
        var refused = expressions.Where(static expression => UpdateValues.All(value => UpdateError(expression, value) is not null));

        // Assert
        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task ParseUpdate_UpdatesDynamoDbLocalRejects_ThrowWhateverTheValues()
    {
        // Arrange
        var expressions = ExpressionCorpus.RejectedUpdates;

        // Act
        var accepted = expressions.Where(static expression => UpdateValues.Any(value => UpdateError(expression, value) is null));

        // Assert
        await Assert.That(accepted).IsEmpty();
    }

    [Test]
    [Arguments("a = :v a", "Syntax error; token: \"a\", near: \":v a\"")]
    [Arguments("a = :v AND", "Syntax error; token: \"<EOF>\", near: \"AND\"")]
    [Arguments("AND a = :v", "Syntax error; token: \"AND\", near: \"AND a\"")]
    [Arguments("a = 5", "Syntax error; token: \"5\", near: \"= 5\"")]
    [Arguments("a $ :v", "Syntax error; token: \"$\", near: \"a $ :v\"")]
    [Arguments("a + :v = :v", "Syntax error; token: \"+\", near: \"a + :v\"")]
    [Arguments("attribute_exists(a) = :v", "Syntax error; token: \"=\", near: \") = :v\"")]
    [Arguments("a = attribute_exists(b)", "Syntax error; token: \"attribute_exists\", near: \"= attribute_exists(\"")]
    [Arguments("NOT (a)", "Syntax error; token: \"<EOF>\", near: \")\"")]
    [Arguments("a BETWEEN :v OR :v", "Syntax error; token: \"OR\", near: \":v OR :v\"")]
    [Arguments("_a = :v", "Syntax error; token: \"_\", near: \"_a\"")]
    [Arguments("a._b = :v", "Syntax error; token: \"_\", near: \"._b\"")]
    [Arguments("a[99999999999] = :v", "Syntax error; token: \"99999999999\", near: \"[99999999999]\"")]
    [Arguments("", "The expression can not be empty;")]
    [Arguments(" \t\r\n", "The expression can not be empty;")]
    [Arguments("status = :v", ReservedKeyword + "; reserved keyword: status")]
    [Arguments("a.Name = :v", ReservedKeyword + "; reserved keyword: Name")]
    [Arguments("#missing = :v", "An expression attribute name used in the document path is not defined; attribute name: #missing")]
    [Arguments("a = :missing", "An expression attribute value used in expression is not defined; attribute value: :missing")]
    [Arguments("unknown(a)", "Invalid function name; function: unknown")]
    [Arguments("SIZE(a) > :v", "Invalid function name; function: SIZE")]
    [Arguments("if_not_exists(a, :v) = :v", "The function is not allowed in a condition expression; function: if_not_exists")]
    [Arguments("list_append(a, :v) = :v", "The function is not allowed in a condition expression; function: list_append")]
    [Arguments("attribute_exists(a, b)", "Incorrect number of operands for operator or function; operator or function: attribute_exists, number of operands: 2")]
    [Arguments("size(a, b) > :v", "Incorrect number of operands for operator or function; operator or function: size, number of operands: 2")]
    [Arguments("contains(a)", "Incorrect number of operands for operator or function; operator or function: contains, number of operands: 1")]
    [Arguments("attribute_exists(:v)", "Operator or function requires a document path; operator or function: attribute_exists")]
    [Arguments("attribute_not_exists(size(a))", "Operator or function requires a document path; operator or function: attribute_not_exists")]
    [Arguments("attribute_type(:v, :v)", "Operator or function requires a document path; operator or function: attribute_type")]
    [Arguments("attribute_type(a, :n)", "Incorrect operand type for operator or function; operator or function: attribute_type, operand type: N")]
    [Arguments("attribute_type(a, :v)", "Invalid attribute type name found; type: abc, valid types: { B,NULL,SS,BOOL,L,BS,N,NS,S,M }")]
    [Arguments("begins_with(a, :n)", "Incorrect operand type for operator or function; operator or function: begins_with, operand type: N")]
    [Arguments("begins_with(:l, a)", "Incorrect operand type for operator or function; operator or function: begins_with, operand type: L")]
    [Arguments("a BETWEEN :n AND :v", "The BETWEEN operator requires same data type for lower and upper bounds; lower bound operand: AttributeValue: {N:5}, upper bound operand: AttributeValue: {S:abc}")]
    [Arguments("a BETWEEN :b AND :l", "The BETWEEN operator requires same data type for lower and upper bounds; lower bound operand: AttributeValue: {B:AQI=}, upper bound operand: AttributeValue: {L}")]
    [Arguments("a BETWEEN :n AND :one", "The BETWEEN operator requires upper bound to be greater than or equal to lower bound; lower bound operand: AttributeValue: {N:5}, upper bound operand: AttributeValue: {N:1}")]
    public async Task ParseCondition_Rejected_ThrowsValidationWithTheMessage(string expression, string message)
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"abc"},":n":{"N":"5"},":one":{"N":"1"},":b":{"B":"AQI="},":l":{"L":[]}}""");

        // Act
        void Act() => FakeExpressions.ParseCondition(expression, scope);

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Invalid ConditionExpression: " + message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("a BETWEEN :one AND :n")]
    [Arguments("a BETWEEN :n AND :n")]
    [Arguments("a BETWEEN :l AND :l")]
    [Arguments("a BETWEEN b AND :one")]
    [Arguments("a BETWEEN :n AND b")]
    [Arguments("attribute_type(a, b)")]
    [Arguments("begins_with(a, b)")]
    public async Task ParseCondition_BoundsOrOperandsKnownOnlyOnTheItem_Parses(string expression)
    {
        // Arrange
        var scope = Scope("""{":n":{"N":"5"},":one":{"N":"1"},":l":{"L":[]}}""");

        // Act
        void Act() => FakeExpressions.ParseCondition(expression, scope);

        // Assert
        await Assert.That(Act).ThrowsNothing();
    }

    [Test]
    public async Task ParseCondition_InWithOneHundredCandidates_Parses()
    {
        // Arrange
        var expression = "a IN (" + string.Join(", ", Enumerable.Repeat(":v", 100)) + ")";

        // Act
        var condition = FakeExpressions.ParseCondition(expression, Scope("""{":v":{"S":"x"}}"""));

        // Assert
        await Assert.That(((ConditionNode.In)condition.Root).Candidates.Count).IsEqualTo(100);
    }

    [Test]
    public async Task ParseCondition_InWithOneHundredAndOneCandidates_ThrowsValidation()
    {
        // Arrange
        var expression = "a IN (" + string.Join(", ", Enumerable.Repeat(":v", 101)) + ")";

        // Act
        void Act() => FakeExpressions.ParseCondition(expression, Scope("""{":v":{"S":"x"}}"""));

        // Assert
        await Assert.That(Act).Throws<FakeError>()
            .WithMessage("Invalid ConditionExpression: The IN operator is provided with too many operands; number of operands: 101", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseCondition_FilterExpression_NamesTheMemberInTheMessage()
    {
        // Arrange
        var scope = Scope();

        // Act
        void Act() => FakeExpressions.ParseCondition("a =", scope, FakeExpressions.FilterExpression);

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Invalid FilterExpression: Syntax error; token: \"<EOF>\", near: \"=\"", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseCondition_Precedence_BindsNotThenAndThenOr()
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"x"}}""");

        // Act
        var condition = FakeExpressions.ParseCondition("NOT a = :v OR b = :v AND c = :v", scope);

        // Assert
        await Assert.That(condition.Root).IsTypeOf<ConditionNode.Or>();
        var or = (ConditionNode.Or)condition.Root;
        await Assert.That((or.Left is ConditionNode.Not, or.Right is ConditionNode.And)).IsEqualTo((true, true));
    }

    [Test]
    public async Task ParseCondition_Expression_KeepsTheText()
    {
        // Arrange
        const string Text = "attribute_exists(a)";

        // Act
        var condition = FakeExpressions.ParseCondition(Text, Scope());

        // Assert
        await Assert.That(condition.Text).IsEqualTo(Text);
    }

    [Test]
    public async Task ParseCondition_NamePlaceholders_ResolveToTheNames()
    {
        // Arrange (#n stands for a name with a dot, which is one attribute, not a path)
        var scope = Scope("""{":v":{"S":"x"}}""", """{"#pr":"ProductReviews","#n":"a.b"}""");

        // Act
        var condition = FakeExpressions.ParseCondition("#pr.#n[1] = :v", scope);

        // Assert
        await Assert.That(((Operand.Attribute)((ConditionNode.Compare)condition.Root).Left).Path.ToString()).IsEqualTo("[ProductReviews, a.b, [1]]");
    }

    [Test]
    [Arguments("SET a = :v SET b = :v", "The \"SET\" section can only be used once in an update expression;")]
    [Arguments("set a = :v REMOVE b Set c = :v", "The \"SET\" section can only be used once in an update expression;")]
    [Arguments("REMOVE a remove b", "The \"REMOVE\" section can only be used once in an update expression;")]
    [Arguments("SET a = b + c + d", "Syntax error; token: \"+\", near: \"c + d\"")]
    [Arguments("a = :v", "Syntax error; token: \"a\", near: \"a =\"")]
    [Arguments("SET a = :v,", "Syntax error; token: \"<EOF>\", near: \",\"")]
    [Arguments("SET SET = :v", "Syntax error; token: \"SET\", near: \"SET SET =\"")]
    [Arguments("ADD a (:n)", "Syntax error; token: \"(\", near: \"a (:n\"")]
    [Arguments("", "The expression can not be empty;")]
    [Arguments("SET a = size(b)", "The function is not allowed in an update expression; function: size")]
    [Arguments("SET a = attribute_exists(b)", "The function is not allowed in an update expression; function: attribute_exists")]
    [Arguments("SET a = if_not_exists(:v, :v)", "Operator or function requires a document path; operator or function: if_not_exists")]
    [Arguments("SET a = list_append(a, b, c)", "Incorrect number of operands for operator or function; operator or function: list_append, number of operands: 3")]
    [Arguments("SET a = list_append(:v, a)", "Incorrect operand type for operator or function; operator or function: list_append, operand type: S")]
    [Arguments("SET a = list_append(a, :n)", "Incorrect operand type for operator or function; operator or function: list_append, operand type: N")]
    [Arguments("SET a = :v + :n", "Incorrect operand type for operator or function; operator or function: +, operand type: S")]
    [Arguments("SET a = b - :l", "Incorrect operand type for operator or function; operator or function: -, operand type: L")]
    [Arguments("ADD a :v", "Incorrect operand type for operator or function; operator: ADD, operand type: STRING")]
    [Arguments("ADD a :b", "Incorrect operand type for operator or function; operator: ADD, operand type: BINARY")]
    [Arguments("ADD a :t", "Incorrect operand type for operator or function; operator: ADD, operand type: BOOLEAN")]
    [Arguments("ADD a :z", "Incorrect operand type for operator or function; operator: ADD, operand type: NULL")]
    [Arguments("ADD a :l", "Incorrect operand type for operator or function; operator: ADD, operand type: LIST")]
    [Arguments("ADD a :m", "Incorrect operand type for operator or function; operator: ADD, operand type: MAP")]
    [Arguments("DELETE a :n", "Incorrect operand type for operator or function; operator: DELETE, operand type: NUMBER")]
    [Arguments("SET a = :v, a = :n", "Two document paths overlap with each other; must remove or rewrite one of these paths; path one: [a], path two: [a]")]
    [Arguments("SET a = :v REMOVE a.b", "Two document paths overlap with each other; must remove or rewrite one of these paths; path one: [a], path two: [a, b]")]
    [Arguments("ADD a.b[0].c :n DELETE a.b :ss", "Two document paths overlap with each other; must remove or rewrite one of these paths; path one: [a, b, [0], c], path two: [a, b]")]
    [Arguments("SET a.b = :v REMOVE a[0]", "Two document paths conflict with each other; must remove or rewrite one of these paths; path one: [a, b], path two: [a, [0]]")]
    public async Task ParseUpdate_Rejected_ThrowsValidationWithTheMessage(string expression, string message)
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"abc"},":n":{"N":"5"},":b":{"B":"AQI="},":t":{"BOOL":true},":z":{"NULL":true},":l":{"L":[]},":m":{"M":{}},":ss":{"SS":["a"]}}""");

        // Act
        void Act() => FakeExpressions.ParseUpdate(expression, scope);

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Invalid UpdateExpression: " + message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("SET a[0] = :v, a[1] = :v")]
    [Arguments("SET a.b = :v, a.c = :v")]
    [Arguments("SET a = :v, b = a")]
    [Arguments("REMOVE a[0], a[1]")]
    [Arguments("SET ab = :v, a = :v")]
    public async Task ParseUpdate_PathsApart_Parses(string expression)
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"abc"}}""");

        // Act
        var update = FakeExpressions.ParseUpdate(expression, scope);

        // Assert
        await Assert.That(update.Text).IsEqualTo(expression);
    }

    [Test]
    public async Task ParseUpdate_Sections_KeepTheOrderOfTheExpression()
    {
        // Arrange
        var scope = Scope("""{":n":{"N":"1"},":s":{"SS":["x"]}}""");

        // Act
        var update = FakeExpressions.ParseUpdate("DELETE d :s ADD c :n REMOVE b SET a = :n", scope);

        // Assert
        await Assert.That(string.Join(' ', update.Actions.Select(static action => action.GetType().Name + action.Path))).IsEqualTo("Delete[d] Add[c] Remove[b] Set[a]");
    }

    [Test]
    [Arguments("a, a.b", "Two document paths overlap with each other; must remove or rewrite one of these paths; path one: [a], path two: [a, b]")]
    [Arguments("a, b, a", "Two document paths overlap with each other; must remove or rewrite one of these paths; path one: [a], path two: [a]")]
    [Arguments("a.b, a[0]", "Two document paths conflict with each other; must remove or rewrite one of these paths; path one: [a, b], path two: [a, [0]]")]
    [Arguments("", "The expression can not be empty;")]
    [Arguments("a,, b", "Syntax error; token: \",\", near: \",, b\"")]
    [Arguments("a b", "Syntax error; token: \"b\", near: \"a b\"")]
    [Arguments("x(a)", "Syntax error; token: \"(\", near: \"x(a\"")]
    [Arguments("data", ReservedKeyword + "; reserved keyword: data")]
    [Arguments(":v", "Syntax error; token: \":v\", near: \":v\"")]
    public async Task ParseProjection_Rejected_ThrowsValidationWithTheMessage(string expression, string message)
    {
        // Arrange
        var scope = Scope();

        // Act
        void Act() => FakeExpressions.ParseProjection(expression, scope);

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Invalid ProjectionExpression: " + message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseProjection_Paths_KeepsThemInOrder()
    {
        // Arrange
        var scope = Scope(names: """{"#d":"data"}""");

        // Act
        var projection = FakeExpressions.ParseProjection("a.b[2], #d , c[0][1]", scope);

        // Assert
        await Assert.That((projection.Text, string.Join(' ', projection.Paths))).IsEqualTo(("a.b[2], #d , c[0][1]", "[a, b, [2]] [data] [c, [0], [1]]"));
    }

    private static string? ConditionError(string expression) => Error(() => FakeExpressions.ParseCondition(expression, ScopeFor(expression, """{"S":"S"}""")));

    private static string? UpdateError(string expression, string value) => Error(() => FakeExpressions.ParseUpdate(expression, ScopeFor(expression, value)));

    private static string? Error(Action parse)
    {
        try
        {
            parse();
            return null;
        }
        catch (FakeError error)
        {
            return error.Message;
        }
    }
}
