using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA243: <c>Set(handle, value)</c> where the compiler's nullable analysis says the value may be null at that point: <c>Set(Customer.Attrs.ShipTo, dto.ShipTo)</c>. <c>Set</c> of null removes the
/// attribute, which a patch from a request whose field was left out does not mean; <c>SetIfNotNull</c> leaves it as it is.
/// </summary>
/// <remarks>
/// The flow state is the compiler's, so a value that a check before proved not null, <c>if (dto.ShipTo is { } shipTo)</c> or a guard clause, is not reported, nor is a literal <c>null</c> or <c>default</c>,
/// which says the removal, nor any value where the warnings of the nullable context are off, <c>#nullable disable</c>. Info, because a value that may be null is right where null means removal.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class NullableSetAnalyzer : DiagnosticAnalyzer
{
    private const string SetVerb = "Set";

    private const string ValueParameter = "value";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.SetOfMaybeNull];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != SetVerb
            || !DynaSharpTypes.IsDeclaredByDynaSharp(method.ContainingType)
            || method.Parameters[0].Name != EntityCommandAnalyzer.HandleParameter)
        {
            return;
        }

        var handle = invocation.Arguments.First(static argument => argument.Parameter!.Name == EntityCommandAnalyzer.HandleParameter);
        var value = invocation.Arguments.First(static argument => argument.Parameter!.Name == ValueParameter).Value;
        var written = (value is IConversionOperation { IsImplicit: true } conversion ? conversion.Operand : value).Syntax;
        var model = invocation.SemanticModel!;
        if (IsNullLiteral(written)
            || !model.GetNullableContext(written.SpanStart).WarningsEnabled()
            || model.GetTypeInfo(written, context.CancellationToken).Nullability.FlowState != NullableFlowState.MaybeNull)
        {
            return;
        }

        // The name of the method, however the call is written; SetIfNotNull takes the same arguments.
        var verb = ((InvocationExpressionSyntax)invocation.Syntax).Expression.DescendantTokens().Last(static token => token.ValueText == SetVerb);
        var edits = new List<SuggestedEdit> { new("Use SetIfNotNull", verb.Span, "SetIfNotNull") };
        context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.SetOfMaybeNull, written.GetLocation(), edits, handle.Value.Syntax.ToString()));
    }

    // null, default, and either in parentheses or a cast, such as (string?)null: the removal written out.
    private static bool IsNullLiteral(SyntaxNode syntax) => syntax switch
    {
        ParenthesizedExpressionSyntax parenthesized => IsNullLiteral(parenthesized.Expression),
        CastExpressionSyntax cast => IsNullLiteral(cast.Expression),
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.NullLiteralExpression) || literal.IsKind(SyntaxKind.DefaultLiteralExpression),
        _ => syntax is DefaultExpressionSyntax,
    };
}
