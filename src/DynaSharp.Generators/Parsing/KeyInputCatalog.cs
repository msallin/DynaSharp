using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Sorts a type into its row of the format table.</summary>
internal static class KeyInputCatalog
{
    private const string FormattableInterface = "System.IUtf8SpanFormattable";

    private const string ParsableInterface = "System.IUtf8SpanParsable<TSelf>";

    private static readonly Dictionary<SpecialType, IntegerType> Integers = new()
    {
        [SpecialType.System_SByte] = new IntegerType("sbyte", IsUnsigned: false, IsFullWidth: false),
        [SpecialType.System_Int16] = new IntegerType("short", IsUnsigned: false, IsFullWidth: false),
        [SpecialType.System_Int32] = new IntegerType("int", IsUnsigned: false, IsFullWidth: false),
        [SpecialType.System_Int64] = new IntegerType("long", IsUnsigned: false, IsFullWidth: true),
        [SpecialType.System_Byte] = new IntegerType("byte", IsUnsigned: true, IsFullWidth: false),
        [SpecialType.System_UInt16] = new IntegerType("ushort", IsUnsigned: true, IsFullWidth: false),
        [SpecialType.System_UInt32] = new IntegerType("uint", IsUnsigned: true, IsFullWidth: false),
        [SpecialType.System_UInt64] = new IntegerType("ulong", IsUnsigned: true, IsFullWidth: true),
    };

    // These implement the formattable interfaces, so they must be turned away by name.
    private static readonly HashSet<string> Rejected =
    [
        "float", "double", "global::System.Half", "global::System.TimeSpan", "global::System.Int128", "global::System.UInt128",
        "global::System.Numerics.BigInteger", "nint", "nuint",
    ];

    public static bool IsInteger(ITypeSymbol type) => Integers.ContainsKey(type.SpecialType);

    /// <summary>Classifies a type without nullability.</summary>
    /// <param name="type">The type.</param>
    /// <param name="enums">Where an enum in a name format registers its converter.</param>
    /// <returns>The row of the table.</returns>
    public static KeyInputType Classify(ITypeSymbol type, EnumRegistry enums)
    {
        var typeName = type.ToDisplayString();
        var code = Names.Of(type);
        return code switch
        {
            "string" => new StringInput(typeName),
            "decimal" => new DecimalInput(typeName),
            "global::System.Guid" => new FixedInput(typeName, new GuidEncoding()),
            "global::System.DateOnly" => new FixedInput(typeName, new DateOnlyEncoding(), "no format, because it is written as yyyy-MM-dd already; a DateTime or DateTimeOffset takes the shorter ones, such as yyyy-MM"),
            "global::System.DateTimeOffset" => new TimestampInput(typeName, isOffset: true),
            "global::System.DateTime" => new TimestampInput(typeName, isOffset: false),
            _ => Others(type, typeName, code, enums),
        };
    }

    private static KeyInputType Others(ITypeSymbol type, string typeName, string code, EnumRegistry enums)
    {
        if (Integers.TryGetValue(type.SpecialType, out var integer))
        {
            return new IntegerInput(typeName, integer);
        }

        if (type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } enumType)
        {
            return new EnumInput(typeName, code, Integers[underlying.SpecialType], EnumRegistry.NamesAreUnique(enumType), () => enums.ConverterOf(enumType));
        }

        return !Rejected.Contains(code) && IsFormattable(type) ? new FixedInput(typeName, new FormattableEncoding(code)) : new RejectedInput(typeName);
    }

    private static bool IsFormattable(ITypeSymbol type) =>
        type.AllInterfaces.Any(static candidate => candidate.ToDisplayString() == FormattableInterface)
        && type.AllInterfaces.Any(static candidate => candidate.OriginalDefinition.ToDisplayString() == ParsableInterface);
}
