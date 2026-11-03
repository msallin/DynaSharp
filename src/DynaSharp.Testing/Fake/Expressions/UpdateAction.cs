namespace DynaSharp.Testing.Fake;

/// <summary>An action of a parsed <c>UpdateExpression</c>, on the path it changes.</summary>
/// <param name="Path">The path the action changes.</param>
internal abstract record UpdateAction(DocumentPath Path)
{
    /// <summary><c>SET path = value</c>.</summary>
    /// <param name="Path">The path.</param>
    /// <param name="Value">The value, read from the item as it was before the update.</param>
    public sealed record Set(DocumentPath Path, Operand Value) : UpdateAction(Path);

    /// <summary><c>REMOVE path</c>.</summary>
    /// <param name="Path">The path.</param>
    public sealed record Remove(DocumentPath Path) : UpdateAction(Path);

    /// <summary><c>ADD path :value</c>, a number or a set.</summary>
    /// <param name="Path">The path.</param>
    /// <param name="Value">The number to add or the elements to add.</param>
    public sealed record Add(DocumentPath Path, AttributeValue Value) : UpdateAction(Path);

    /// <summary><c>DELETE path :value</c>, a set.</summary>
    /// <param name="Path">The path.</param>
    /// <param name="Value">The elements to take out.</param>
    public sealed record Delete(DocumentPath Path, AttributeValue Value) : UpdateAction(Path);
}
