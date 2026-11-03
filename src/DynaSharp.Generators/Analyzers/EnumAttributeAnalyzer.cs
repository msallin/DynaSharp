using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA033: <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> on an enum that the generated converter does not read by name. A <c>[Flags]</c> enum is stored as a number, which
/// has no name to miss, and an enum with <c>[DynamoDbConverter]</c> of its own gets no generated converter at all. It looks at the enum, once, on the attribute, whether a type uses the enum or not,
/// because the generator sees an enum only through the types that use it, and would report it once for each of them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class EnumAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string StoredAsNumber = "it is [Flags], which is stored as a number and has no name to miss. Take the attribute off.";

    private const string HasConverter = "its [DynamoDbConverter] reads it in place of the generated converter. Take the attribute off, or answer CountsAsMissing in that converter.";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.IneffectiveEnumAttribute];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.EnumAttribute) is not null)
            {
                start.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
            }
        });
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        // The attribute only goes on an enum: on any other type it is CS0592, which says enough.
        var type = (INamedTypeSymbol)context.Symbol;
        if (!EnumRegistry.AsksToCountUnknownAsMissing(type) || WhyNoEffect(type) is not { } reason)
        {
            return;
        }

        var attribute = AttributeReader.Find(type, WellKnownNames.EnumAttribute)!;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IneffectiveEnumAttribute, attribute.ApplicationSyntaxReference!.GetSyntax().GetLocation(), type.Name, reason));
    }

    // The converter on the type wins over the generated one, as ConverterResolver has it, so that reason comes first.
    private static string? WhyNoEffect(INamedTypeSymbol type)
    {
        if (AttributeReader.Find(type, WellKnownNames.ConverterAttribute) is not null)
        {
            return HasConverter;
        }

        return EnumRegistry.IsStoredAsNumber(type) ? StoredAsNumber : null;
    }
}
