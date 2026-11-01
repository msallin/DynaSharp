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
internal sealed record DiagnosticInfo(string Id, EquatableArray<string> Arguments, string FilePath, TextSpan Span);
