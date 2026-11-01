using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// Selects converters. First match wins: <c>[DynamoDbConverter]</c> on the property, on the type, <see cref="AttributeValueConverter"/> and the
/// built-in scalars, enums, nullable wrappers around any of these, documents, and collections recursing on the element.
/// </summary>
/// <param name="enums">Where enums register their generated converter.</param>
/// <param name="bag">Where problems go.</param>
internal sealed class ConverterResolver(EnumRegistry enums, DiagnosticBag bag)
{
    private const string Serialization = "global::DynaSharp.Serialization.";

    private const string AttributeValueConverter = Serialization + "AttributeValueConverter";

    private const string NumberConverter = Serialization + "NumberConverter";

    private const string DefaultHint = "Add a [DynamoDbConverter] or use a supported type";

    private const string ListHint = "Use IReadOnlyList<T>, or add a [DynamoDbConverter]";

    private const string SetArrayConverter = Serialization + "SetArrayConverter<";

    private const string EpochSecondsConverter = Serialization + "EpochSecondsConverter";

    private static readonly HashSet<string> Numbers =
    [
        "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal",
        "nint", "nuint", "global::System.Half", "global::System.Int128", "global::System.UInt128", "global::System.Numerics.BigInteger",
    ];

    // Everything with a key-compatible wire type, that is S, N or B. bool and AttributeValue are added to the full table below.
    private static readonly Dictionary<string, string> KeyScalars = new()
    {
        ["string"] = Serialization + "StringConverter",
        ["global::System.Guid"] = Serialization + "GuidConverter",
        ["global::System.DateTimeOffset"] = Serialization + "DateTimeOffsetConverter",
        ["global::System.DateTime"] = Serialization + "DateTimeConverter",
        ["global::System.DateOnly"] = Serialization + "DateOnlyConverter",
        ["global::System.TimeOnly"] = Serialization + "TimeOnlyConverter",
        ["global::System.TimeSpan"] = Serialization + "TimeSpanConverter",
        ["byte[]"] = Serialization + "BinaryConverter",
        ["global::System.ReadOnlyMemory<byte>"] = Serialization + "ReadOnlyMemoryConverter",
    };

    private static readonly Dictionary<string, string> OtherScalars = new()
    {
        ["bool"] = Serialization + "BoolConverter",
        ["global::DynaSharp.AttributeValue"] = AttributeValueConverter,
    };

    private static readonly Dictionary<string, string> Lists = new()
    {
        ["System.Collections.Generic.List<T>"] = "ListConverter",
        ["System.Collections.Generic.IReadOnlyList<T>"] = "ReadOnlyListConverter",
        ["System.Collections.Generic.IList<T>"] = "MutableListConverter",
    };

    private static readonly Dictionary<string, string> Sets = new()
    {
        ["System.Collections.Generic.HashSet<T>"] = "HashSetConverter",
        ["System.Collections.Generic.IReadOnlySet<T>"] = "ReadOnlySetConverter",
    };

    private static readonly Dictionary<string, string> Dictionaries = new()
    {
        ["System.Collections.Generic.Dictionary<TKey, TValue>"] = "DictionaryConverter",
        ["System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"] = "ReadOnlyDictionaryConverter",
    };

    private static readonly HashSet<string> WrongCollections =
    [
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
    ];

    private bool _isPublic;

    /// <summary>Whether a type without nullability has a wire type that DynamoDB accepts for a key.</summary>
    public static bool IsKeyType(ITypeSymbol type)
    {
        var code = Names.Of(type);
        return type.TypeKind == TypeKind.Enum
            || KeyScalars.ContainsKey(code)
            || Numbers.Contains(code)
            || AttributeReader.Find(type, WellKnownNames.ConverterAttribute) is not null;
    }

    /// <summary>
    /// Finds how a plain key is stored. A built-in says so by what it is. A custom converter can only say so by <c>IAddableConverter</c>, which marks a number or a set, and a set
    /// is no key. Nothing tells what any other custom converter writes.
    /// </summary>
    /// <param name="property">The property, with its converter resolved.</param>
    /// <returns>The type, or <see cref="KeyWireType.Unknown"/> for a custom converter that does not say.</returns>
    public static KeyWireType WireTypeOf(PropertyFacts property)
    {
        var (inner, _) = Types.Unwrap(property.Type);
        var attribute = property.Converter ?? AttributeReader.Find(inner, WellKnownNames.ConverterAttribute);
        if (attribute?.ConstructorArguments.FirstOrDefault().Value is not ITypeSymbol converter)
        {
            return WireTypeOf(inner);
        }

        var name = Names.Of(converter);
        if (name == EpochSecondsConverter || converter.AllInterfaces.Any(static candidate => candidate.ToDisplayString() == WellKnownNames.AddableInterface))
        {
            return KeyWireType.Number;
        }

        // A built-in named in the attribute writes what it writes without the attribute.
        return name.StartsWith(Serialization, StringComparison.Ordinal) ? WireTypeOf(inner) : KeyWireType.Unknown;
    }

    /// <summary>Resolves the converter of a property.</summary>
    /// <returns>The converter, or null when there is none, which has been reported.</returns>
    public ResolvedConverter? Resolve(PropertyFacts property)
    {
        var (inner, isNullable) = Types.Unwrap(property.Type);
        _isPublic = true;
        var converter = property.Converter is { } attribute ? Custom(attribute, inner, property.Spot) : Inner(inner, property);
        if (converter is null)
        {
            return null;
        }

        // A nullable one is left out when it is null, which is what it can only be.
        if (!isNullable && property.Converter is null && EnumRegistry.HasNoNames(inner))
        {
            bag.Report(Descriptors.EnumWithoutMembers, property.Spot, property.Name, inner.ToDisplayString());
        }

        var read = isNullable ? Wrapped(inner, converter) : converter;
        var write = isNullable ? WriteCondition.WhenNotNull : WriteCondition.Always;
        if (property.Converter is null && IsSetType(inner, out var element))
        {
            return new ResolvedConverter(new ConverterModel(converter, read, $"new global::System.Collections.Generic.HashSet<{Names.Of(element)}>()"), WriteCondition.WhenNotEmptyCount, _isPublic);
        }

        if (inner is IArrayTypeSymbol array && converter.StartsWith(SetArrayConverter, StringComparison.Ordinal))
        {
            return new ResolvedConverter(new ConverterModel(converter, read, $"global::System.Array.Empty<{Names.Of(array.ElementType)}>()"), WriteCondition.WhenNotEmptyLength, _isPublic);
        }

        var forwarder = WritesExplicitly(property, inner) ? Wrapped(inner, converter) : null;
        return new ResolvedConverter(new ConverterModel(converter, read, null, forwarder), write, _isPublic);
    }

    // What the built-in converter of a key type writes, a string unless it is listed here. A new entry in KeyScalars that writes anything else has to show here too.
    private static KeyWireType WireTypeOf(ITypeSymbol type)
    {
        var code = Names.Of(type);
        if (Numbers.Contains(code) || (type.TypeKind == TypeKind.Enum && EnumRegistry.IsStoredAsNumber(type)))
        {
            return KeyWireType.Number;
        }

        return code is "byte[]" or "global::System.ReadOnlyMemory<byte>" ? KeyWireType.Binary : KeyWireType.String;
    }

    private static string Wrapped(ITypeSymbol inner, string converter) =>
        $"{Serialization}{(inner.IsValueType ? "NullableConverter" : "NullableReferenceConverter")}<{Names.Of(inner)}, {converter}>";

    // Generated code calls Write on the type of the converter, and only a read goes through a type parameter. An explicit member cannot be called
    // that way, and the wrappers of null can: they take the value and pass it on through their own parameter.
    private static bool WritesExplicitly(PropertyFacts property, ITypeSymbol value)
    {
        var attribute = property.Converter ?? AttributeReader.Find(value, WellKnownNames.ConverterAttribute);
        if (attribute?.ConstructorArguments[0].Value is not ITypeSymbol converter)
        {
            return false;
        }

        var contract = converter.AllInterfaces.First(candidate => IsConverterOf(candidate, value));

        // By its accessibility: in metadata, every implementation of a static member looks like an explicit one.
        return converter.FindImplementationForInterfaceMember(contract.GetMembers("Write")[0]) is { DeclaredAccessibility: not Accessibility.Public };
    }

    private static bool IsConverterOf(INamedTypeSymbol candidate, ITypeSymbol value) =>
        candidate.OriginalDefinition.ToDisplayString() == WellKnownNames.ConverterInterface && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], value);

    private static bool IsSetType(ITypeSymbol type, out ITypeSymbol element)
    {
        if (type is INamedTypeSymbol { TypeArguments.Length: 1 } named && Sets.ContainsKey(named.OriginalDefinition.ToDisplayString()))
        {
            element = named.TypeArguments[0];
            return true;
        }

        element = type;
        return false;
    }

    private static bool IsDocument(ITypeSymbol type) =>
        AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null
        || AttributeReader.Find(type, WellKnownNames.DocumentAttribute) is not null
        || type.AllInterfaces.Any(static candidate => candidate.OriginalDefinition.ToDisplayString() == WellKnownNames.DocumentInterface);

    private static bool Implements(ITypeSymbol converter, string interfaceName, ITypeSymbol value) =>
        converter.AllInterfaces.Any(candidate =>
            candidate.OriginalDefinition.ToDisplayString() == interfaceName
            && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], value));

    private string? Inner(ITypeSymbol type, PropertyFacts property)
    {
        if (AttributeReader.Find(type, WellKnownNames.ConverterAttribute) is { } attribute)
        {
            return Custom(attribute, type, property.Spot);
        }

        var code = Names.Of(type);
        if (KeyScalars.TryGetValue(code, out var scalar) || OtherScalars.TryGetValue(code, out scalar))
        {
            return scalar;
        }

        if (Numbers.Contains(code))
        {
            return $"{NumberConverter}<{code}>";
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return enums.ConverterOf(enumType);
        }

        var (inner, isNullable) = Types.Unwrap(type);
        if (isNullable)
        {
            return Inner(inner, property) is { } wrapped ? Wrapped(inner, wrapped) : null;
        }

        return IsDocument(type) ? $"{Serialization}DocumentConverter<{code}>" : Collection(type, property);
    }

    private string? Collection(ITypeSymbol type, PropertyFacts property)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            return Element("ArrayConverter", array.ElementType, property);
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return Unsupported(property, DefaultHint);
        }

        var definition = named.OriginalDefinition.ToDisplayString();
        if (Lists.TryGetValue(definition, out var list))
        {
            return Element(list, named.TypeArguments[0], property);
        }

        if (Sets.TryGetValue(definition, out var set))
        {
            return SetElement(named.TypeArguments[0]) is { } element
                ? $"{Serialization}{set}<{Names.Of(named.TypeArguments[0])}, {element}>"
                : Unsupported(property, DefaultHint);
        }

        if (Dictionaries.TryGetValue(definition, out var dictionary) && named.TypeArguments[0].SpecialType == SpecialType.System_String)
        {
            return Element(dictionary, named.TypeArguments[1], property);
        }

        return Unsupported(property, WrongCollections.Contains(definition) ? ListHint : DefaultHint);
    }

    private string? Element(string collection, ITypeSymbol element, PropertyFacts property) =>
        Inner(element, property) is { } converter ? $"{Serialization}{collection}<{Names.Of(element)}, {converter}>" : null;

    // A DynamoDB set holds strings, numbers or binary and never null, so a nullable element has no converter.
    private string? SetElement(ITypeSymbol element)
    {
        if (element.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return null;
        }

        var code = Names.Of(element.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        if (AttributeReader.Find(element, WellKnownNames.ConverterAttribute) is { } attribute)
        {
            return attribute.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol custom && Implements(custom, WellKnownNames.SetElementInterface, element) ? Accepted(custom) : null;
        }

        if (element is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return enums.ConverterOf(enumType);
        }

        return code switch
        {
            "string" => Serialization + "StringSetElement",
            "byte[]" => Serialization + "BinarySetElement",
            "global::System.Guid" => Serialization + "GuidSetElement",
            _ => Numbers.Contains(code) ? $"{Serialization}NumberSetElement<{code}>" : null,
        };
    }

    private string? Custom(AttributeData attribute, ITypeSymbol value, Spot fallback)
    {
        var argument = attribute.ConstructorArguments.FirstOrDefault();
        if (argument.Value is INamedTypeSymbol { TypeKind: not (TypeKind.Error or TypeKind.Interface), IsUnboundGenericType: false } converter
            && Types.IsAccessible(converter)
            && converter.AllInterfaces.Any(candidate => IsConverterOf(candidate, value)))
        {
            return Accepted(converter);
        }

        bag.Report(Descriptors.InvalidConverter, Spot.Of(attribute, fallback), argument.Value?.ToString() ?? "null", value.ToDisplayString());
        return null;
    }

    private string Accepted(ITypeSymbol converter)
    {
        _isPublic &= Types.Effective(converter) == Accessibility.Public;
        return Names.Of(converter);
    }

    private string? Unsupported(PropertyFacts property, string hint)
    {
        bag.Report(Descriptors.UnsupportedType, property.Spot, property.Name, property.Type.ToDisplayString(), hint);
        return null;
    }
}
