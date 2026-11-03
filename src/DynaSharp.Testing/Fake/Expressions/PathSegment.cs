namespace DynaSharp.Testing.Fake;

/// <summary>A step of a document path: the name of an attribute or a map entry, or the index of a list element.</summary>
/// <param name="Name">The name, or <see langword="null"/> for a list index.</param>
/// <param name="Index">The list index, 0 for a name.</param>
internal readonly record struct PathSegment(string? Name, int Index)
{
    /// <summary>Gets a value indicating whether this is a list index.</summary>
    public bool IsIndex => Name is null;

    /// <summary>Makes the step of a name.</summary>
    /// <param name="name">The name, as resolved from a placeholder where the expression has one.</param>
    /// <returns>The step.</returns>
    public static PathSegment Of(string name) => new(name, 0);

    /// <summary>Makes the step of a list index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The step.</returns>
    public static PathSegment At(int index) => new(null, index);

    /// <summary>Gets the step as DynamoDB prints it in a message: <c>a</c> for a name, <c>[0]</c> for an index.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Name ?? $"[{Index}]";
}
