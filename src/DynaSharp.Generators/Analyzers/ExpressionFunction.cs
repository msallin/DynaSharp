namespace DynaSharp.Generators;

/// <summary>A function of DynamoDB's grammar. DynamoDB matches the names case-sensitively and keeps the two groups apart.</summary>
/// <param name="Operands">The number of operands.</param>
/// <param name="InUpdate">Whether it belongs to update expressions, rather than to conditions.</param>
/// <param name="IsTest">Whether it is a condition by itself, and so cannot be an operand.</param>
/// <param name="HasPath">Whether its first operand is meant to be a name.</param>
internal sealed record ExpressionFunction(int Operands, bool InUpdate, bool IsTest, bool HasPath)
{
    /// <summary>
    /// All functions by name. The runtime repeats those of a condition in <c>ExpressionWords.ConditionFunctions</c>, because it cannot reference an analyzer.
    /// <c>ExpressionWordsTests</c> keeps the two in step: a change here needs a change there, or a reason in that test.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ExpressionFunction> All = new Dictionary<string, ExpressionFunction>(StringComparer.Ordinal)
    {
        ["attribute_exists"] = new(1, InUpdate: false, IsTest: true, HasPath: true),
        ["attribute_not_exists"] = new(1, InUpdate: false, IsTest: true, HasPath: true),
        ["attribute_type"] = new(2, InUpdate: false, IsTest: true, HasPath: true),
        ["begins_with"] = new(2, InUpdate: false, IsTest: true, HasPath: true),
        ["contains"] = new(2, InUpdate: false, IsTest: true, HasPath: true),
        ["size"] = new(1, InUpdate: false, IsTest: false, HasPath: true),
        ["if_not_exists"] = new(2, InUpdate: true, IsTest: false, HasPath: true),
        ["list_append"] = new(2, InUpdate: true, IsTest: false, HasPath: false),
    };
}
