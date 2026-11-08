using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes that rewrite one placeholder of a key template, each with what the generator computed: DYNA102 a property of a close spelling, DYNA104 each format of the type that holds every
/// value, DYNA108 <c>d&lt;w&gt;</c> and <c>n&lt;i&gt;</c> with the digits of the type's maximum or <c>plain</c>, DYNA113 <c>d&lt;w&gt;</c> with the digits of the largest member, and DYNA116
/// the placeholder without its format. A template that is no string literal, such as a constant, is not offered one.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(KeyPlaceholderCodeFix))]
[Shared]
internal sealed class KeyPlaceholderCodeFix : TextEditCodeFix
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [Descriptors.UnknownProperty.Id, Descriptors.FormatDoesNotFit.Id, Descriptors.NumberWithoutFixedFormat.Id, Descriptors.EnumNamesNotUnique.Id, Descriptors.TruncatedTimestampEndsTableKey.Id];

    /// <summary>The equivalence key of a replacement: by its rank for a spelling, closest first, and by the kind of its format for the others, <c>DYNA108.d</c>, <c>DYNA104.yyyy-MM</c>, <c>DYNA116.none</c>.</summary>
    /// <param name="id">The rule.</param>
    /// <param name="placeholder">The replacement, such as <c>{Qty:d10}</c>.</param>
    /// <param name="rank">Its place among the replacements.</param>
    /// <returns>The key.</returns>
    internal static string Key(string id, string placeholder, int rank)
    {
        if (id == Descriptors.UnknownProperty.Id)
        {
            return id + "." + rank;
        }

        var colon = placeholder.IndexOf(':');
        var format = colon < 0 ? "none" : placeholder.Substring(colon + 1, placeholder.Length - colon - 2);
        return id + "." + (format.Length > 1 && format[0] is 'd' or 'n' && char.IsDigit(format[1]) ? format.Substring(0, 1) : format);
    }

    /// <inheritdoc/>
    protected override async Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        if (FixOf(diagnostic) is not { } fix
            || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not LiteralExpressionSyntax literal
            || KeyTemplateParser.FindPlaceholder(literal.Token.Text, fix[0]) is not { } found)
        {
            return [];
        }

        var span = new TextSpan(literal.Token.SpanStart + found.Start, found.Length);
        return fix.Skip(1).Select((placeholder, rank) => new FixOption("Change to " + placeholder, Key(diagnostic.Id, placeholder, rank), [(document.Id, new TextChange(span, placeholder))]));
    }
}
