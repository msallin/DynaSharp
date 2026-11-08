using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>The enums of one type that need a generated converter, each once, with a converter name unique within the file.</summary>
internal sealed class EnumRegistry
{
    private const string FlagsAttribute = "System.FlagsAttribute";

    private static readonly SymbolDisplayFormat QualifiedName = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    private readonly Dictionary<string, EnumModel> _enums = [];

    public static bool NamesAreUnique(INamedTypeSymbol type)
    {
        var values = new HashSet<object>();
        return AttributeReader.Find(type, FlagsAttribute) is null && Fields(type).All(field => values.Add(field.ConstantValue!));
    }

    /// <summary>
    /// The width of a <c>d&lt;w&gt;</c> that holds every value of an enum: the digits of its largest member, or of all its members combined for <c>[Flags]</c>. Null when a member is negative,
    /// which <c>d&lt;w&gt;</c> cannot write.
    /// </summary>
    /// <param name="type">The enum.</param>
    /// <returns>The width, at least 1, or null.</returns>
    public static int? DigitsWidth(INamedTypeSymbol type)
    {
        var values = Fields(type).Select(static field => Convert.ToDecimal(field.ConstantValue, CultureInfo.InvariantCulture)).ToList();
        if (values.Exists(static value => value < 0))
        {
            return null;
        }

        var largest = AttributeReader.Find(type, FlagsAttribute) is null
            ? values.DefaultIfEmpty(0).Max()
            : values.Aggregate(0UL, static (all, value) => all | (ulong)value);
        return largest.ToString(CultureInfo.InvariantCulture).Length;
    }

    /// <summary>Whether a generated converter can write no value of a type: an enum goes by the name of a member, and this one has none. <c>[Flags]</c> goes by value, and a converter on the enum has its own way.</summary>
    /// <param name="type">The type, without nullability.</param>
    /// <returns>Whether every write of the type throws.</returns>
    public static bool HasNoNames(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum } named
        && !Fields(named).Any()
        && AttributeReader.Find(named, FlagsAttribute) is null
        && AttributeReader.Find(named, WellKnownNames.ConverterAttribute) is null;

    /// <summary>Whether a generated converter stores an enum as a number, as it does for <c>[Flags]</c>, whose combinations have no name.</summary>
    /// <param name="type">The enum.</param>
    /// <returns>True for a number, false for a name.</returns>
    public static bool IsStoredAsNumber(ITypeSymbol type) => AttributeReader.Find(type, FlagsAttribute) is not null;

    /// <summary>Whether an enum says <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c>, whatever stores it. A value that is no member of <c>UnknownEnumName</c> throws, as the default does.</summary>
    /// <param name="type">The enum.</param>
    /// <returns>True if it asks for a stored name it does not have to count as missing.</returns>
    public static bool AsksToCountUnknownAsMissing(ITypeSymbol type) =>
        AttributeReader.Find(type, WellKnownNames.EnumAttribute) is { } attribute
        && AttributeReader.Number(attribute, WellKnownNames.UnknownNameArgument) == WellKnownNames.UnknownNameCountsAsMissing;

    /// <summary>Whether the generated converter of an enum counts a stored name it does not have as missing. A number has no name, so a <c>[Flags]</c> enum never does.</summary>
    /// <param name="type">The enum.</param>
    /// <returns>True if the converter answers <c>CountsAsMissing</c>.</returns>
    public static bool CountsUnknownAsMissing(ITypeSymbol type) => !IsStoredAsNumber(type) && AsksToCountUnknownAsMissing(type);

    public EquatableArray<EnumModel> ToEquatableArray() => new([.. _enums.Values]);

    public string ConverterOf(INamedTypeSymbol type)
    {
        var fullName = Names.Of(type);
        if (_enums.TryGetValue(fullName, out var known))
        {
            return known.ConverterName;
        }

        var model = new EnumModel(
            fullName,
            UniqueConverterName(type),
            IsStoredAsNumber(type),
            Names.Of(type.EnumUnderlyingType!),
            Members(type),
            CountsUnknownAsMissing(type));
        _enums.Add(fullName, model);
        return model.ConverterName;
    }

    private static IEnumerable<IFieldSymbol> Fields(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(static field => field.HasConstantValue);

    private static EquatableArray<EnumMember> Members(INamedTypeSymbol type)
    {
        var values = new HashSet<object>();
        var members = Fields(type)
            .Select(field => new EnumMember(field.Name, Names.Identifier(field.Name), Encoding.UTF8.GetByteCount(field.Name), values.Add(field.ConstantValue!)));
        return new EquatableArray<EnumMember>([.. members]);
    }

    // Shop.Inner.Status gives Shop_Inner_StatusConverter. Shop_Inner.Status gives the same text, so the second one gets a counter.
    private string UniqueConverterName(INamedTypeSymbol type)
    {
        var name = new StringBuilder(type.ToDisplayString(QualifiedName)).Replace('.', '_').Append("Converter").ToString();
        var unique = name;
        for (var counter = 2; _enums.Values.Any(known => known.ConverterName == unique); counter++)
        {
            unique = name + counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return unique;
    }
}
