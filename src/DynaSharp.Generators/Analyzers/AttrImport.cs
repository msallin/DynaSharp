using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DynaSharp.Generators;

/// <summary>The <c>using static DynaSharp.Expr;</c> that a code fix writing <c>Attr(...)</c> adds where the name does not bind. Only the code fixes use this class.</summary>
internal static class AttrImport
{
    private const string StaticImport = "DynaSharp.Expr";

    /// <summary>Whether <paramref name="call"/>, an <c>Attr(...)</c>, binds at <paramref name="position"/> as the file stands.</summary>
    public static bool Binds(SemanticModel model, int position, ExpressionSyntax call) =>
        model.GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression).Symbol is not null;

    public static CompilationUnitSyntax Add(CompilationUnitSyntax root)
    {
        var import = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(StaticImport))
            .WithStaticKeyword(SyntaxFactory.Token(SyntaxKind.StaticKeyword))
            .WithAdditionalAnnotations(Formatter.Annotation);
        return root.AddUsings(import);
    }
}
