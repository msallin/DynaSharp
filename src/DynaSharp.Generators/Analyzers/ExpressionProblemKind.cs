namespace DynaSharp.Generators;

/// <summary>What is wrong with an expression, one kind per diagnostic.</summary>
internal enum ExpressionProblemKind
{
    /// <summary>A constant string where the grammar wants a name, DYNA210.</summary>
    StringInNamePosition,

    /// <summary>A <c>#name</c> or <c>:value</c> in the literal text, DYNA211.</summary>
    Placeholder,

    /// <summary>A reserved word of DynamoDB as a bare name, DYNA220.</summary>
    ReservedWord,

    /// <summary>Text that DynamoDB would reject, DYNA212.</summary>
    Syntax,

    /// <summary>A bare word that a path starts with, which is DYNA213 if the type of the command has no such attribute. Only a caller that asks gets it.</summary>
    BareName,
}
