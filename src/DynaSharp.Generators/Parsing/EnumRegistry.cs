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
            Members(type));
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
