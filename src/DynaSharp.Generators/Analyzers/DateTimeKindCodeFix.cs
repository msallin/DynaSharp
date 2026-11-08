using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA237: <c>new DateTime(2026, 1, 15)</c> becomes <c>new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)</c>, and any other constructor without a kind takes
/// <c>DateTimeKind.Utc</c> as its last argument, where <c>DateTime</c> has that constructor. Not offered with named arguments, or with a <c>Calendar</c>, which has no such constructor for every
/// length. Only an IDE loads this class.
/// </summary>
/// <remarks>
/// The <c>DateOnly</c> of the message is no edit of the constructor: where the rule reports, a <c>DateTime</c> is wanted, so the property or the key would have to change its type, and with it what
/// is stored.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeKindCodeFix))]
[Shared]
internal sealed class DateTimeKindCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every constructor, so that a fix-all passes the kind to each.</summary>
    internal const string Key = "DYNA237.Utc";

    private const string Title = "Pass DateTimeKind.Utc";

    private const string KindType = "System.DateTimeKind";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.DateTimeWithoutKind.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false))!;
        foreach (var diagnostic in context.Diagnostics)
        {
            // The analyzer reports on the creation, which has arguments.
            var creation = (BaseObjectCreationExpressionSyntax)root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var arguments = creation.ArgumentList!;
            if (!arguments.Arguments.Any(static argument => argument.NameColon is not null) && Appended(model, creation) is { } appended)
            {
                var change = new TextChange(new TextSpan(arguments.Arguments.Last().Span.End, 0), appended);
                context.RegisterCodeFix(CodeAction.Create(Title, async token => document.WithText((await document.GetTextAsync(token).ConfigureAwait(false)).WithChanges(change)), Key), diagnostic);
            }
        }
    }

    // What follows the last argument: the kind, and for a date the time of midnight before it, as the constructor that takes a kind wants. Null where DateTime has no such constructor.
    private static string? Appended(SemanticModel model, BaseObjectCreationExpressionSyntax creation)
    {
        var constructor = (IMethodSymbol)model.GetSymbolInfo(creation).Symbol!;
        var kind = model.Compilation.GetTypeByMetadataName(KindType)!;
        var integer = model.Compilation.GetSpecialType(SpecialType.System_Int32);
        var date = constructor.Parameters.Length == 3 && constructor.Parameters.All(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, integer));
        ITypeSymbol[] midnight = date ? [integer, integer, integer] : [];
        ITypeSymbol[] wanted = [.. constructor.Parameters.Select(static parameter => parameter.Type), .. midnight, kind];
        if (!constructor.ContainingType.InstanceConstructors.Any(candidate => candidate.Parameters.Select(static parameter => parameter.Type).SequenceEqual(wanted, SymbolEqualityComparer.Default)))
        {
            return null;
        }

        // DateTimeKind where the file can name it so, else in full.
        var binds = model.GetSpeculativeSymbolInfo(creation.SpanStart, SyntaxFactory.ParseExpression("DateTimeKind.Utc"), SpeculativeBindingOption.BindAsExpression).Symbol is { } utc
            && SymbolEqualityComparer.Default.Equals(utc.ContainingType, kind);
        return (date ? ", 0, 0, 0, " : ", ") + (binds ? "DateTimeKind.Utc" : "global::System.DateTimeKind.Utc");
    }
}
