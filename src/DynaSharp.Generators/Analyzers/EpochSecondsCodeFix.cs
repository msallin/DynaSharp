using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA036 on a <c>DateTimeOffset</c> without a converter of its own: <c>[DynamoDbConverter(typeof(EpochSecondsConverter))]</c> right after <c>[TimeToLive]</c>, so that the item holds the
/// number of seconds DynamoDB deletes by. A second <c>[TimeToLive]</c>, one on a property that is not stored, and one beside another converter are offered nothing.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EpochSecondsCodeFix))]
[Shared]
internal sealed class EpochSecondsCodeFix : TextEditCodeFix
{
    /// <summary>The equivalence key of the fix.</summary>
    internal const string Key = "DYNA036";

    private const string EpochSecondsConverter = "DynaSharp.Serialization.EpochSecondsConverter";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.InvalidTimeToLive.Id];

    /// <inheritdoc/>
    protected override async Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        if (FixOf(diagnostic) is null)
        {
            return [];
        }

        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var timeToLive = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<AttributeSyntax>()!;
        var converter = SourceEdits.AttributeName(model, timeToLive.SpanStart, WellKnownNames.ConverterAttribute)
            + "(typeof(" + SourceEdits.TypeName(model, timeToLive.SpanStart, EpochSecondsConverter) + "))";
        return [new FixOption("Add [DynamoDbConverter(typeof(EpochSecondsConverter))]", Key, [(document.Id, SourceEdits.InsertAfter(timeToLive, converter))])];
    }
}
