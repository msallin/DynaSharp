using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>One way to fix a diagnostic: the title of the action, its equivalence key, which a fix-all picks it by, and its edits.</summary>
/// <param name="Title">What the action writes, such as <c>Add [KeyOnly]</c>.</param>
/// <param name="Key">The equivalence key, the same for the same choice on every diagnostic of the rule.</param>
/// <param name="Edits">The changes, each of the text of one document.</param>
internal sealed record FixOption(string Title, string Key, ImmutableArray<(DocumentId Document, TextChange Change)> Edits);
