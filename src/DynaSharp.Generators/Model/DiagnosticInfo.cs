using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// A diagnostic as a value, so it can cross the pipeline. The location is rebuilt from the compilation in a separate output node,
/// because only a location in a real tree reaches <c>#pragma</c> and <c>.editorconfig</c>.
/// </summary>
/// <param name="Id">The id, a key of <see cref="Descriptors.ById"/>.</param>
/// <param name="Arguments">The message arguments.</param>
/// <param name="FilePath">The path of the tree.</param>
/// <param name="Span">The span in that tree.</param>
/// <param name="Type">
/// The metadata name of a type of the compilation whose <c>[DynamoDbEntity]</c> is the place, for a value that cannot know a place: one of all the entities, which would change with every edit that moves one. Null otherwise.
/// </param>
/// <param name="Fix">What a code fix writes, which the diagnostic carries in its property <see cref="FixProperty"/>; null for none.</param>
internal sealed record DiagnosticInfo(string Id, EquatableArray<string> Arguments, string FilePath, TextSpan Span, string? Type = null, string? Fix = null)
{
    /// <summary>The property of a diagnostic that holds <see cref="Fix"/>.</summary>
    public const string FixProperty = "Fix";
}
