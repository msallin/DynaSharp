using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Reads the interpolated strings that bind to <c>ExpressionHandler</c> as the text they render, and reports names in value holes (DYNA210),
/// placeholders in the text (DYNA211) and what DynamoDB's grammar rejects (DYNA212). The rules are in <see cref="ExpressionParser"/>. The text functions on a handle of a number are valid
/// grammar that matches nothing (DYNA216), which only the type of the hole tells, so that is looked at here.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ExpressionAnalyzer : DiagnosticAnalyzer
{
    private const string SetMethod = "Set";

    private const string AttrType = "Attr";

    // What a number or a boolean cannot be an operand of: both functions want a string or a binary value, and contains also a set or a list.
    private static readonly string[] TextFunctions = ["begins_with", "contains"];

    // A nullable property says so by a wrapper around its converter.
    private static readonly HashSet<string> OptionalConverters = ["OptionalConverter", "OptionalReferenceConverter"];

    // The built-in converters that do not store text. Those of a consumer are not known.
    private static readonly HashSet<string> NoTextConverters = ["NumberConverter", "EpochSecondsConverter", "BoolConverter"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Descriptors.StringInNamePosition, Descriptors.PlaceholderInExpression, Descriptors.ExpressionSyntaxError, Descriptors.TextFunctionOnNumber];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is { } handler)
            {
                start.RegisterOperationAction(operation => Analyze(operation, handler), OperationKind.InterpolatedStringHandlerCreation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol handler)
    {
        var creation = (IInterpolatedStringHandlerCreationOperation)context.Operation;
        if (!SymbolEqualityComparer.Default.Equals(creation.Type, handler))
        {
            return;
        }

        var expression = new Expression();
        expression.Add(creation.Content);
        foreach (var problem in ExpressionParser.Check(KindOf(creation), expression.Parts))
        {
            var location = Location.Create(creation.Syntax.SyntaxTree, expression.SpanOf(problem, creation.Syntax.Span));
            context.ReportDiagnostic(Diagnostic.Create(DescriptorOf(problem.Kind), location, problem.Detail));
        }

        foreach (var (span, function) in expression.TextFunctionsOnNumbers())
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.TextFunctionOnNumber, Location.Create(creation.Syntax.SyntaxTree, span), function));
        }
    }

    // A wrapper of the consumer may forward the handler to either grammar, so only the methods of the library say which one it is.
    private static ExpressionKind KindOf(IOperation creation)
    {
        if (creation.Parent is not IArgumentOperation { Parameter: { } parameter } || !DynaSharpTypes.IsDeclaredByDynaSharp(parameter.ContainingType))
        {
            return ExpressionKind.Unknown;
        }

        return parameter.ContainingSymbol.Name == SetMethod ? ExpressionKind.SetAction : ExpressionKind.Condition;
    }

    private static DiagnosticDescriptor DescriptorOf(ExpressionProblemKind kind) => kind switch
    {
        ExpressionProblemKind.StringInNamePosition => Descriptors.StringInNamePosition,
        ExpressionProblemKind.Placeholder => Descriptors.PlaceholderInExpression,
        _ => Descriptors.ExpressionSyntaxError,
    };

    /// <summary>The parts of one interpolated string and where they stand in the source.</summary>
    private sealed class Expression
    {
        private readonly List<PartSource> _sources = [];

        public List<ExpressionPart> Parts { get; } = [];

        public void Add(IOperation content)
        {
            // $"a" + $"b" is one string: the handler sees the parts in a row.
            if (content is IInterpolatedStringAdditionOperation addition)
            {
                Add(addition.Left);
                Add(addition.Right);
                return;
            }

            foreach (var part in ((IInterpolatedStringOperation)content).Parts)
            {
                var append = (IInterpolatedStringAppendOperation)part;
                if (append.Kind == OperationKind.InterpolatedStringAppendLiteral)
                {
                    AddLiteral(append);
                }
                else
                {
                    AddHole(append);
                }
            }
        }

        // begins_with({handle}, ...), where the hole directly follows the name of the function: the first operand is the one that has to be text.
        public IEnumerable<(TextSpan Span, string Function)> TextFunctionsOnNumbers()
        {
            for (var part = 1; part < Parts.Count; part++)
            {
                var before = Parts[part - 1].Text.TrimEnd();
                foreach (var function in TextFunctions)
                {
                    if (_sources[part].HoldsNoText && before.EndsWith(function + "(", StringComparison.Ordinal))
                    {
                        yield return (_sources[part].Span, function);
                    }
                }
            }
        }

        // An escape or an indentation makes the text differ from the source, and then a position in it means nothing there.
        public TextSpan SpanOf(ExpressionProblem problem, TextSpan whole)
        {
            if (problem.Part == ExpressionProblem.WholeExpression)
            {
                return whole;
            }

            var source = _sources[problem.Part];
            return source.IsExact && problem.Length > 0 ? new TextSpan(source.Span.Start + problem.Start, problem.Length) : source.Span;
        }

        // A handle reaches the handler as it is, or through its conversion to Attr, so the operand is what has the converter.
        private static bool HoldsNoText(IOperation appendCall) =>
            appendCall is IInvocationOperation call
            && (call.Arguments[0].Value is IConversionOperation conversion ? conversion.Operand : call.Arguments[0].Value).Type is INamedTypeSymbol { Name: AttrType, TypeArguments.Length: 2 } handle
            && NoTextConverters.Contains(StoredBy(handle.TypeArguments[1]).Name);

        private static ITypeSymbol StoredBy(ITypeSymbol converter) =>
            converter is INamedTypeSymbol { TypeArguments.Length: 2 } optional && OptionalConverters.Contains(optional.Name) ? optional.TypeArguments[1] : converter;

        private static ExpressionPart HolePart(IOperation appendCall)
        {
            // A hole of a type the handler does not take is a compile error, and the call is invalid.
            if (appendCall is not IInvocationOperation call)
            {
                return new ExpressionPart(string.Empty, HoleKind.Value);
            }

            var argument = call.Arguments[0];
            if (argument.Parameter!.Type.Name == AttrType)
            {
                return new ExpressionPart(string.Empty, HoleKind.Name);
            }

            // A cast says that the string is meant as a value, so only the conversion the compiler adds is looked through.
            var value = argument.Value is IConversionOperation { IsImplicit: true } conversion ? conversion.Operand : argument.Value;
            return value.ConstantValue is { HasValue: true, Value: string text }
                ? new ExpressionPart(text, HoleKind.StringConstant)
                : new ExpressionPart(string.Empty, HoleKind.Value);
        }

        private void AddLiteral(IInterpolatedStringAppendOperation append)
        {
            var text = (string)((IInvocationOperation)append.AppendCall).Arguments[0].Value.ConstantValue.Value!;
            var span = append.Syntax.Span;
            var last = Parts.Count - 1;
            if (last >= 0 && Parts[last].Hole == HoleKind.None)
            {
                Parts[last] = ExpressionPart.Literal(Parts[last].Text + text);
                _sources[last] = new PartSource(TextSpan.FromBounds(_sources[last].Span.Start, span.End), false);
            }
            else
            {
                Parts.Add(ExpressionPart.Literal(text));
                _sources.Add(new PartSource(span, span.Length == text.Length));
            }
        }

        private void AddHole(IInterpolatedStringAppendOperation append)
        {
            Parts.Add(HolePart(append.AppendCall));
            _sources.Add(new PartSource(((InterpolationSyntax)append.Syntax).Expression.Span, false, HoldsNoText(append.AppendCall)));
        }

        private readonly record struct PartSource(TextSpan Span, bool IsExact, bool HoldsNoText = false);
    }
}
