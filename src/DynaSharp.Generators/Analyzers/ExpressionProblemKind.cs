namespace DynaSharp.Generators;

/// <summary>What is wrong with an expression, one kind per diagnostic.</summary>
internal enum ExpressionProblemKind
{
    /// <summary>A constant string where the grammar wants a name, DYNA210.</summary>
    StringInNamePosition,

    /// <summary>A <c>#name</c> or <c>:value</c> in the literal text, DYNA211.</summary>
    Placeholder,

    /// <summary>Text that DynamoDB would reject, DYNA212.</summary>
    Syntax,
}
