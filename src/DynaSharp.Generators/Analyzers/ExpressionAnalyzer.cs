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
/// grammar that matches nothing (DYNA216), which only the type of the hole tells, so that is looked at here, and so is the one <c>BeginsWith</c> by handle that no overload can refuse.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ExpressionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The built-in converters that do not store text, which are those <c>AttrExtensions.BeginsWith</c> has an overload for that does not compile. Those of a consumer are not known, and an enum has a converter of its own, which only the model knows about.</summary>
    internal static readonly HashSet<string> NoTextConverters = ["NumberConverter", "EpochSecondsConverter", "BoolConverter"];

    private const string SetMethod = "Set";

    private const string AttrType = "Attr";

    private const string BeginsWithMethod = "BeginsWith";

    private const string BeginsWithFunction = "begins_with";

    private const string HandleExtensions = "AttrExtensions";

    // What a number or a boolean cannot be an operand of: both functions want a string or a binary value, and contains also a set or a list.
    private static readonly string[] TextFunctions = [BeginsWithFunction, "contains"];

    // A nullable property says so by a wrapper around its converter.
    private static readonly HashSet<string> OptionalConverters = ["OptionalConverter", "OptionalReferenceConverter"];

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
                var models = new ModelCatalog(start.Compilation);
                start.RegisterOperationAction(operation => Analyze(operation, handler, models), OperationKind.InterpolatedStringHandlerCreation);
                start.RegisterOperationAction(operation => AnalyzeBeginsWith(operation, models), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol handler, ModelCatalog models)
    {
        var creation = (IInterpolatedStringHandlerCreationOperation)context.Operation;
        if (!SymbolEqualityComparer.Default.Equals(creation.Type, handler))
        {
            return;
        }

        var expression = new Expression(models);
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

    // A number, a date and a boolean have overloads that do not compile, CS0619. The forwarder of an enum is a nested type of the consumer, which no overload can name, so a [Flags] enum takes the general one.
    private static void AnalyzeBeginsWith(OperationAnalysisContext context, ModelCatalog models)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { Name: BeginsWithMethod, ContainingType: { Name: HandleExtensions } extensions } || !DynaSharpTypes.IsDeclaredByDynaSharp(extensions))
        {
            return;
        }

        var handle = call.Arguments[0].Value;
        if (StoredBy(handle.Type) is { } converter && StoresEnumAsNumber(converter, models))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.TextFunctionOnNumber, handle.Syntax.GetLocation(), BeginsWithFunction));
        }
    }

    // What stores the value of a handle: a converter, without the wrapper of a nullable property. Null if it is no handle.
    private static ITypeSymbol? StoredBy(ITypeSymbol? handle)
    {
        if (handle is not INamedTypeSymbol { Name: AttrType, TypeArguments.Length: 2 } attr)
        {
            return null;
        }

        var converter = attr.TypeArguments[1];
        return converter is INamedTypeSymbol { TypeArguments.Length: 2 } optional && OptionalConverters.Contains(optional.Name) ? optional.TypeArguments[1] : converter;
    }

    // A forwarder is nested in the Attrs of the type it serves. Its name is all the symbol says, and what the enum stores is known to the model of that type. A set of the enum has a converter of its own, so it does not come here.
    private static bool StoresEnumAsNumber(ITypeSymbol converter, ModelCatalog models) =>
        converter.ContainingType?.ContainingType is { } owner
        && models.ModelOf(owner) is { } model
        && model.Enums.Any(known => known.StoreAsNumber && AttrsEmitter.ForwarderPrefix + known.ConverterName == converter.Name);

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
    /// <param name="models">The models of the compilation.</param>
    private sealed class Expression(ModelCatalog models)
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

        // A handle reaches the handler as it is, or through its conversion to Attr, so the operand is what has the converter.
        private bool HoldsNoText(IOperation appendCall) =>
            appendCall is IInvocationOperation call
            && StoredBy((call.Arguments[0].Value is IConversionOperation conversion ? conversion.Operand : call.Arguments[0].Value).Type) is { } converter
            && (NoTextConverters.Contains(converter.Name) || StoresEnumAsNumber(converter, models));

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
