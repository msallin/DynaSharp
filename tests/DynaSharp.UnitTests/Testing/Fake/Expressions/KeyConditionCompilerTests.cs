using DynaSharp.Requests;
using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The forms are those of the Developer Guide, "Key condition expressions for Query": an equality on the partition key, and at most one of =, <, <=, >, >=, BETWEEN and
// begins_with on the sort key. The table has the partition key pk (S) and the sort key sk (N); the index has the partition key gpk (S) and the sort key gsk (S).
public sealed class KeyConditionCompilerTests
{
    private const string Values = """
        {":p":{"S":"user-1"},":n":{"N":"5"},":m":{"N":"9"},":s":{"S":"a"},":empty":{"S":""},":bin":{"B":""},":n2":{"N":"5.0"},":t":{"BOOL":true}}
        """;

    private static readonly KeySchema Table = new("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.Number);
    private static readonly KeySchema Index = new("gpk", ScalarAttributeType.String, "gsk", ScalarAttributeType.String);

    [Test]
    public async Task ParseKeyCondition_PartitionOnly_SelectsTheWholePartition()
    {
        // Arrange
        var scope = Scope(Values);

        // Act
        var range = FakeExpressions.ParseKeyCondition("pk = :p", scope, Table);

        // Assert
        await Assert.That(range).IsEqualTo(new KeyRange("user-1", null));
    }

    [Test]
    [Arguments("pk = :p AND sk = :n", "Equal")]
    [Arguments("pk = :p AND sk < :n", "LessThan")]
    [Arguments("pk = :p AND sk <= :n", "LessThanOrEqual")]
    [Arguments("pk = :p AND sk > :n", "GreaterThan")]
    [Arguments("pk = :p AND sk >= :n", "GreaterThanOrEqual")]
    [Arguments("sk = :n AND pk = :p", "Equal")]
    [Arguments(":p = pk AND :n = sk", "Equal")]
    [Arguments("pk = :p AND :n < sk", "GreaterThan")]
    [Arguments("pk = :p AND :n <= sk", "GreaterThanOrEqual")]
    [Arguments("pk = :p AND :n > sk", "LessThan")]
    [Arguments("pk = :p AND :n >= sk", "LessThanOrEqual")]
    [Arguments("(pk = :p) AND (sk > :n)", "GreaterThan")]
    [Arguments("#p = :p and #s > :n", "GreaterThan")]
    public async Task ParseKeyCondition_Comparison_SelectsBySortKey(string expression, string expected)
    {
        // Arrange
        var scope = Scope(Values, """{"#p":"pk","#s":"sk"}""");

        // Act
        var range = FakeExpressions.ParseKeyCondition(expression, scope, Table);

        // Assert
        await Assert.That(range).IsEqualTo(new KeyRange("user-1", new SortKeyRange(Enum.Parse<SortKeyOperator>(expected), AttributeValue.FromNumber("5"))));
    }

    [Test]
    public async Task ParseKeyCondition_Between_SelectsBothEnds()
    {
        // Arrange
        var scope = Scope(Values);

        // Act
        var range = FakeExpressions.ParseKeyCondition("sk BETWEEN :n AND :m AND pk = :p", scope, Table);

        // Assert
        await Assert.That(range).IsEqualTo(new KeyRange("user-1", new SortKeyRange(SortKeyOperator.Between, AttributeValue.FromNumber("5"), AttributeValue.FromNumber("9"))));
    }

    [Test]
    public async Task ParseKeyCondition_BeginsWith_SelectsThePrefix()
    {
        // Arrange
        var scope = Scope(Values);

        // Act
        var range = FakeExpressions.ParseKeyCondition("gpk = :p AND begins_with(gsk, :s)", scope, Index);

        // Assert
        await Assert.That(range).IsEqualTo(new KeyRange("user-1", new SortKeyRange(SortKeyOperator.BeginsWith, "a")));
    }

    [Test]
    [Arguments("pk = :p OR sk = :n", "Invalid operator used in KeyConditionExpression: OR")]
    [Arguments("NOT pk = :p", "Invalid operator used in KeyConditionExpression: NOT")]
    [Arguments("pk IN (:p)", "Invalid operator used in KeyConditionExpression: IN")]
    [Arguments("pk <> :p", "Invalid operator used in KeyConditionExpression: <>")]
    [Arguments("pk = :p AND attribute_exists(sk)", "Invalid operator used in KeyConditionExpression: attribute_exists")]
    [Arguments("pk = :p AND contains(sk, :n)", "Invalid operator used in KeyConditionExpression: contains")]
    [Arguments("pk = :p AND size(sk) = :n", "Invalid operator used in KeyConditionExpression: size")]
    [Arguments("pk = :p AND :n = size(sk)", "Invalid operator used in KeyConditionExpression: size")]
    [Arguments("pk = sk", "Invalid condition in KeyConditionExpression: Multiple attribute names used in one condition")]
    [Arguments("pk = :p AND sk BETWEEN :n AND extra", "Invalid condition in KeyConditionExpression: Multiple attribute names used in one condition")]
    [Arguments("pk = :p AND sk BETWEEN extra AND :n", "Invalid condition in KeyConditionExpression: Multiple attribute names used in one condition")]
    [Arguments(":p = :p", "Invalid condition in KeyConditionExpression: No key attribute specified")]
    [Arguments("pk = :p AND :n BETWEEN :n AND :m", "Invalid condition in KeyConditionExpression: No key attribute specified")]
    [Arguments("pk.a = :p", "Invalid KeyConditionExpression: KeyConditionExpressions cannot have conditions on nested attributes")]
    [Arguments("pk = :p AND pk = :p", "KeyConditionExpressions must only contain one condition per key")]
    [Arguments("pk = :p AND sk > :n AND sk < :m", "Conditions can be of length 1 or 2 only")]
    [Arguments("sk > :n AND sk < :m", "KeyConditionExpressions must only contain one condition per key")]
    [Arguments("pk > :p", "Query key condition not supported")]
    [Arguments("pk = :p AND begins_with(pk, :p)", "KeyConditionExpressions must only contain one condition per key")]
    [Arguments("pk BETWEEN :p AND :p", "Query key condition not supported")]
    [Arguments("extra = :p", "Query key condition not supported")]
    [Arguments("pk = :p AND extra = :n", "Query key condition not supported")]
    [Arguments("sk = :n", "Query condition missed key schema element: pk")]
    [Arguments("pk = :n", "One or more parameter values were invalid: Condition parameter type does not match schema type")]
    [Arguments("pk = :p AND sk = :s", "One or more parameter values were invalid: Condition parameter type does not match schema type")]
    [Arguments("pk = :bin", "One or more parameter values were invalid: Condition parameter type does not match schema type")]
    [Arguments("pk = :t", "One or more parameter values were invalid: Condition parameter type does not match schema type")]
    [Arguments("pk = :empty", "One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty string value. Key: pk")]
    [Arguments("pk = :p AND begins_with(sk, :n)", "Invalid KeyConditionExpression: Incorrect operand type for operator or function; operator or function: begins_with, operand type: N")]
    [Arguments("pk = :p AND sk BETWEEN :m AND :n", "Invalid KeyConditionExpression: The BETWEEN operator requires upper bound to be greater than or equal to lower bound; lower bound operand: AttributeValue: {N:9}, upper bound operand: AttributeValue: {N:5}")]
    [Arguments("pk = ", "Invalid KeyConditionExpression: Syntax error; token: \"<EOF>\", near: \"=\"")]
    public async Task ParseKeyCondition_Rejected_ThrowsValidation(string expression, string message)
    {
        // Arrange
        var scope = Scope(Values);

        // Act
        void Act() => FakeExpressions.ParseKeyCondition(expression, scope, Table);

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseKeyCondition_EmptyBinaryPartition_ThrowsValidation()
    {
        // Arrange
        var schema = new KeySchema("pk", ScalarAttributeType.Binary);

        // Act
        void Act() => FakeExpressions.ParseKeyCondition("pk = :bin", Scope(Values), schema);

        // Assert
        await Assert.That(Act).Throws<FakeError>()
            .WithMessage("One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty binary value. Key: pk", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseKeyCondition_SortKeyOnATableWithoutOne_ThrowsValidation()
    {
        // Arrange
        var schema = new KeySchema("pk", ScalarAttributeType.String);

        // Act
        void Act() => FakeExpressions.ParseKeyCondition("pk = :p AND sk = :n", Scope(Values), schema);

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Query key condition not supported", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseKeyCondition_NumberPartition_SelectsIt()
    {
        // Arrange
        var schema = new KeySchema("pk", ScalarAttributeType.Number);

        // Act
        var range = FakeExpressions.ParseKeyCondition("pk = :n", Scope(Values), schema);

        // Assert
        await Assert.That(range).IsEqualTo(new KeyRange(AttributeValue.FromNumber("5"), null));
    }

    [Test]
    public async Task ParseKeyCondition_NumberWithOtherDigits_KeepsTheStoredForm()
    {
        // Arrange (5.0 is the number 5, stored as DynamoDB stores it)
        var scope = Scope(Values);

        // Act
        var range = FakeExpressions.ParseKeyCondition("pk = :p AND sk = :n2", scope, Table);

        // Assert
        await Assert.That(range.Sort!.Value).IsEqualTo(AttributeValue.FromNumber("5"));
    }
}
