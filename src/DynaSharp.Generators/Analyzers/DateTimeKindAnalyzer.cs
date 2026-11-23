using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA237: <c>new DateTime(2026, 1, 15)</c>, a time of kind <c>Unspecified</c>, where DynaSharp writes it as an instant: in the initializer of a mapped type, to a property that
/// <c>DateTimeConverter</c> stores or a key template holds as a timestamp, and as an argument of a generated key builder. <c>DateTimeConverter.Write</c> and <c>KeyWriter.WriteTimestamp</c>
/// refuse that kind, because it names no instant, so the write or the call throws when it runs.
/// </summary>
/// <remarks>
/// Only a constructor without a <c>DateTimeKind</c> is reported, and only where it stands right there: a value in a variable, <c>default</c>, <c>DateTime.Parse</c>, a property with a converter of its
/// own and a value given to a handle in <c>Set</c> or a condition are not followed. A <c>TryOf</c> of a key builder gives null for such a value instead of throwing, and a <c>DateTimeOffset</c> takes
/// it for local time, so neither is reported.
/// <para>
/// The fix passes <c>DateTimeKind.Utc</c>: <c>new DateTime(2026, 1, 15)</c> becomes <c>new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)</c>, and any other constructor takes the kind as its last
/// argument, where <c>DateTime</c> has that constructor. It is not offered with named arguments, or with a <c>Calendar</c>, which has no such constructor for every length. The <c>DateOnly</c> of the
/// message is no edit of the constructor: where the rule reports, a <c>DateTime</c> is wanted, so the property or the key would have to change its type, and with it what is stored.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class DateTimeKindAnalyzer : DiagnosticAnalyzer
{
    private const string Converter = CodeText.Serialization + "DateTimeConverter";

    private const string TryPrefix = "Try";

    private const string KindType = "System.DateTimeKind";

    private const string Utc = "DateTimeKind.Utc";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.DateTimeWithoutKind];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                var models = new ModelCatalog(start.Compilation);
                var kind = start.Compilation.GetSpecialType(SpecialType.System_DateTime).ContainingAssembly.GetTypeByMetadataName(KindType);
                start.RegisterOperationAction(operation => Analyze(operation, models, kind), OperationKind.ObjectCreation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models, INamedTypeSymbol? kind)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is not { SpecialType: SpecialType.System_DateTime } || creation.Constructor is not { Parameters.Length: > 0 } constructor
            || constructor.Parameters.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, kind)))
        {
            return;
        }

        // A conversion to DateTime? keeps the kind. One to DateTimeOffset does not: it takes the time for local time.
        IOperation value = creation;
        while (value.Parent is IConversionOperation conversion && IsDateTime(conversion.Type!))
        {
            value = conversion;
        }

        var consequence = value.Parent switch
        {
            ISimpleAssignmentOperation { Parent: IObjectOrCollectionInitializerOperation { Parent: { Type: INamedTypeSymbol mapped } }, Target: IMemberReferenceOperation target }
                when models.ModelOf(mapped) is { } model && WritesAnInstant(model, target.Member.Name) => $"a write of '{mapped.Name}' throws",
            IArgumentOperation { Parent: IInvocationOperation call, Parameter.Type: var type } when IsDateTime(type) && IsKeyBuilder(call.TargetMethod, models) => "this call throws KeyFormatException",
            _ => null,
        };
        if (consequence is not null)
        {
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.DateTimeWithoutKind, creation.Syntax.GetLocation(), PassUtc(creation, constructor, kind), creation.Syntax.ToString(), consequence));
        }
    }

    // The reported constructor has arguments. What follows the last one: the kind, and for a date the time of midnight before it, as the constructor that takes a kind wants.
    private static List<SuggestedEdit> PassUtc(IObjectCreationOperation creation, IMethodSymbol constructor, INamedTypeSymbol? kind)
    {
        var arguments = ((BaseObjectCreationExpressionSyntax)creation.Syntax).ArgumentList!.Arguments;
        var integer = creation.SemanticModel!.Compilation.GetSpecialType(SpecialType.System_Int32);
        var date = constructor.Parameters.Length == 3 && constructor.Parameters.All(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, integer));
        ITypeSymbol[] midnight = date ? [integer, integer, integer] : [];
        ITypeSymbol[] wanted = [.. constructor.Parameters.Select(static parameter => parameter.Type), .. midnight, kind!];
        if (arguments.Any(static argument => argument.NameColon is not null)
            || !constructor.ContainingType.InstanceConstructors.Any(candidate => candidate.Parameters.Select(static parameter => parameter.Type).SequenceEqual(wanted, SymbolEqualityComparer.Default)))
        {
            return [];
        }

        // DateTimeKind where the file can name it so, else in full.
        var binds = creation.SemanticModel.GetSpeculativeSymbolInfo(creation.Syntax.SpanStart, SyntaxFactory.ParseExpression(Utc), SpeculativeBindingOption.BindAsExpression).Symbol is { } utc
            && SymbolEqualityComparer.Default.Equals(utc.ContainingType, kind);
        var text = (date ? ", 0, 0, 0, " : ", ") + (binds ? Utc : "global::System." + Utc);
        return [new SuggestedEdit("Pass " + Utc, new TextSpan(arguments.Last().Span.End, 0), text)];
    }

    // Stored by DateTimeConverter, or held by a key template, where a property that takes a DateTime is a timestamp. A converter of one's own, or the default of an assembly, decides for itself.
    private static bool WritesAnInstant(TypeModel model, string property) =>
        model.Properties.Any(candidate => candidate.Name == property && candidate.Converter?.WriteConverter == Converter)
        || (model.Entity is { } entity && entity.Keys().Any(key => key.Holds(property)));

    // A public method of the generated Keys of an entity, or of a builder nested in it: Of, Format, Between, Range, Eq and the others write the time with KeyWriter, which throws, and TryOf does not.
    private static bool IsKeyBuilder(IMethodSymbol method, ModelCatalog models)
    {
        if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.Name.StartsWith(TryPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Keys is nested in the entity, and a builder in Keys.
        for (var type = method.ContainingType; type.ContainingType is { } outer; type = outer)
        {
            if (type.Name == Names.KeysClass)
            {
                return models.ModelOf(outer)?.Entity is not null;
            }
        }

        return false;
    }

    private static bool IsDateTime(ITypeSymbol type) =>
        (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? ((INamedTypeSymbol)type).TypeArguments[0] : type).SpecialType == SpecialType.System_DateTime;
}
