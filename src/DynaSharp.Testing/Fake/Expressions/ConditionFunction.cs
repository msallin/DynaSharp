namespace DynaSharp.Testing.Fake;

/// <summary>The functions that are a condition by themselves. <c>size</c> is an operand, see <see cref="Operand.Size"/>.</summary>
internal enum ConditionFunction
{
    /// <summary><c>attribute_exists(path)</c>.</summary>
    AttributeExists,

    /// <summary><c>attribute_not_exists(path)</c>.</summary>
    AttributeNotExists,

    /// <summary><c>attribute_type(path, :type)</c>.</summary>
    AttributeType,

    /// <summary><c>begins_with(path, :prefix)</c>.</summary>
    BeginsWith,

    /// <summary><c>contains(path, operand)</c>.</summary>
    Contains,
}
