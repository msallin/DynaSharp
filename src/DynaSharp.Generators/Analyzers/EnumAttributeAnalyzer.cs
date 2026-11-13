using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// The attributes of an enum that the generated converter reads. DYNA033: <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> on an enum that the generated converter does not read by name.
/// A <c>[Flags]</c> enum is stored as a number, which has no name to miss, and an enum with <c>[DynamoDbConverter]</c> of its own gets no generated converter at all. DYNA046 is the same for
/// <c>[DynamoDbEnumName]</c> on a member. DYNA045: a stored name that is null, empty, white space alone or holds a control character. DYNA044: two members of different values that one stored name
/// would read as both, because they share it, or one stores the name of the other. It looks at the enum, once, whether a type uses the enum or not, because the generator sees an enum only
/// through the types that use it, and would report it once for each of them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class EnumAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string StoredAsNumber = "it is [Flags], which is stored as a number and has no name to miss. Take the attribute off.";

    private const string HasConverter = "its [DynamoDbConverter] reads it in place of the generated converter. Take the attribute off, or answer CountsAsMissing in that converter.";

    private const string NameStoredAsNumber = "the enum is [Flags], which is stored as a number. Take the attribute off.";

    private const string NameHasConverter = "the [DynamoDbConverter] of the enum reads and writes it in place of the generated converter. Take the attribute off, or store the name in that converter.";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Descriptors.IneffectiveEnumAttribute, Descriptors.EnumStoredNameTaken, Descriptors.InvalidEnumStoredName, Descriptors.IneffectiveEnumName];

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
        if (EnumRegistry.AsksToCountUnknownAsMissing(type) && WhyNoEffect(type) is { } reason)
        {
            var attribute = AttributeReader.Find(type, WellKnownNames.EnumAttribute)!;
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IneffectiveEnumAttribute, attribute.ApplicationSyntaxReference!.GetSyntax().GetLocation(), type.Name, reason));
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            AnalyzeNames(context, type);
        }
    }

    // A name that has no effect is not checked further: the attribute is to go. Only a name the converter stores can be taken twice.
    private static void AnalyzeNames(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        var noEffect = WhyNameHasNoEffect(type);
        foreach (var field in EnumRegistry.Fields(type))
        {
            if (AttributeReader.Find(field, WellKnownNames.EnumNameAttribute) is not { } attribute)
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference!.GetSyntax().GetLocation();
            if (noEffect is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IneffectiveEnumName, location, field.Name, noEffect));
            }
            else if (WhyInvalid(AttributeReader.Text(attribute, 0)) is { } invalid)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidEnumStoredName, location, field.Name, invalid));
            }
        }

        if (noEffect is null)
        {
            ReportTakenNames(context, type);
        }
    }

    // Each pair once, the earlier member first. Members of one value are aliases: they write one name and may read each other's.
    private static void ReportTakenNames(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        var fields = EnumRegistry.Fields(type).ToList();
        for (var first = 0; first < fields.Count; first++)
        {
            for (var second = first + 1; second < fields.Count; second++)
            {
                var (one, other) = (fields[first], fields[second]);
                if (Equals(one.ConstantValue, other.ConstantValue) || WhyTaken(one, other) is not { } taken)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(Descriptors.EnumStoredNameTaken, type.Locations[0], type.Name, taken.Member, taken.Name, taken.Reason));
            }
        }
    }

    private static (string Member, string Name, string Reason)? WhyTaken(IFieldSymbol one, IFieldSymbol other)
    {
        var (oneName, otherName) = (EnumRegistry.StoredName(one), EnumRegistry.StoredName(other));
        if (oneName == otherName)
        {
            return (one.Name, oneName, $"the stored name of '{other.Name}' as well");
        }

        if (oneName == other.Name)
        {
            return (one.Name, oneName, $"the name of the member '{other.Name}'");
        }

        return otherName == one.Name ? (other.Name, otherName, $"the name of the member '{one.Name}'") : null;
    }

    private static string? WhyInvalid(string? name)
    {
        if (name is null)
        {
            return "is null";
        }

        if (name.Length == 0)
        {
            return "is empty";
        }

        if (name.All(char.IsWhiteSpace))
        {
            return "is white space alone";
        }

        return name.Any(char.IsControl) ? "holds a control character" : null;
    }

    private static string? WhyNameHasNoEffect(INamedTypeSymbol type)
    {
        if (AttributeReader.Find(type, WellKnownNames.ConverterAttribute) is not null)
        {
            return NameHasConverter;
        }

        return EnumRegistry.IsStoredAsNumber(type) ? NameStoredAsNumber : null;
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
