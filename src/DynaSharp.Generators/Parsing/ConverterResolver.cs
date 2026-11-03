using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// Selects converters. First match wins: <c>[DynamoDbConverter]</c> on the property, on the type, <c>[assembly: DynamoDbDefaultConverter]</c>, <see cref="AttributeValueConverter"/> and the
/// built-in scalars, enums, nullable wrappers around any of these, documents, and collections recursing on the element.
/// </summary>
/// <param name="enums">Where enums register their generated converter.</param>
/// <param name="bag">Where problems go.</param>
/// <param name="defaults">What the assembly of the type says.</param>
/// <param name="isEntity">Whether the properties belong to an entity, where an entity stored as a document is DYNA031.</param>
internal sealed class ConverterResolver(EnumRegistry enums, DiagnosticBag bag, ProjectDefaults defaults, bool isEntity)
{
    private const string Serialization = "global::DynaSharp.Serialization.";

    private const string AttributeValueConverter = Serialization + "AttributeValueConverter";

    private const string NumberConverter = Serialization + "NumberConverter";

    private const string DefaultHint = "Add a [DynamoDbConverter] or use a supported type";

    private const string ListHint = "Use IReadOnlyList<T>, or add a [DynamoDbConverter]";

    private const string SetArrayConverter = Serialization + "SetArrayConverter<";

    private const string EpochSecondsConverter = Serialization + "EpochSecondsConverter";

    private const string BoolAsNumberConverter = Serialization + "BoolAsNumberConverter";

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
    /// Whether the generated converter of an enum reads a resolved property and counts a name that is no member as missing, which
    /// <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> asks for, before a key takes that back.
    /// </summary>
    /// <param name="property">The property, nullable or not.</param>
    /// <returns>True if the attribute on the enum decides how the property reads an unknown name.</returns>
    public static bool CountsUnknownNameAsMissing(PropertyFacts property) => CountsUnknownNameAsMissing(property, Types.Unwrap(property.Type).Inner);

    /// <summary>
    /// Finds how a plain key is stored. A built-in says so by what it is. A custom converter can only say so by <c>IAddableConverter</c>, which marks a number or a set, and a set
    /// is no key. Nothing tells what any other custom converter writes.
    /// </summary>
    /// <param name="property">The property, with its converter resolved.</param>
    /// <returns>The type, or <see cref="KeyWireType.Unknown"/> for a custom converter that does not say.</returns>
    public static KeyWireType WireTypeOf(PropertyFacts property)
    {
        var (inner, _) = Types.Unwrap(property.Type);
        if (OwnConverterOf(property, inner) is not { } converter)
        {
            return WireTypeOf(inner);
        }

        // The opt-in built-ins that store a number where their type has none say so by name. An opt-in one that writes another type than its value would has to show here too.
        var name = Names.Of(converter);
        if (name is EpochSecondsConverter or BoolAsNumberConverter || IsAddable(converter))
        {
            return KeyWireType.Number;
        }

        // A built-in named in the attribute writes what it writes without the attribute.
        return name.StartsWith(Serialization, StringComparison.Ordinal) ? WireTypeOf(inner) : KeyWireType.Unknown;
    }

    /// <summary>Whether an argument of an attribute is a converter of a value: a closed, accessible type that implements <c>IDynamoDbConverter&lt;T&gt;</c> for it.</summary>
    /// <param name="candidate">What the attribute names.</param>
    /// <param name="value">The value, without nullability.</param>
    /// <returns>Whether the generated code can call it.</returns>
    public static bool IsUsable(object? candidate, ITypeSymbol value) =>
        candidate is INamedTypeSymbol { TypeKind: not (TypeKind.Error or TypeKind.Interface), IsUnboundGenericType: false } converter
        && Types.IsAccessible(converter)
        && converter.AllInterfaces.Any(contract => IsConverterOf(contract, value));

    /// <summary>
    /// What a collection enumerates, which is all a test of its count needs to know about it.
    /// <code>
    /// List&lt;string&gt;            string
    /// string[]                string
    /// Dictionary&lt;string, int&gt;  KeyValuePair&lt;string, int&gt;
    /// </code>
    /// </summary>
    /// <param name="type">A type without nullability.</param>
    /// <returns>The element as code, or null for anything that is not a list, set, dictionary or array.</returns>
    public static string? ElementOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            return Names.Of(array.ElementType);
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return null;
        }

        var definition = named.OriginalDefinition.ToDisplayString();
        if (Dictionaries.ContainsKey(definition))
        {
            return $"global::System.Collections.Generic.KeyValuePair<{Names.Of(named.TypeArguments[0])}, {Names.Of(named.TypeArguments[1])}>";
        }

        return Lists.ContainsKey(definition) || Sets.ContainsKey(definition) ? Names.Of(named.TypeArguments[0]) : null;
    }

    /// <summary>Resolves the converter of a property.</summary>
    /// <returns>The converter, or null when there is none, which has been reported.</returns>
    public ResolvedConverter? Resolve(PropertyFacts property)
    {
        var (inner, isNullable) = Types.Unwrap(property.Type);
        _isPublic = true;
        property.DefaultConverter = property.Converter is null && AttributeReader.Find(inner, WellKnownNames.ConverterAttribute) is null ? DefaultOf(inner, property) : null;
        var converter = property.Converter is { } attribute ? Custom(attribute, inner, property.Spot) : Inner(inner, property);
        if (converter is null)
        {
            return null;
        }

        // A nullable one is left out when it is null, which is what it can only be.
        var hasOwnConverter = property.Converter is not null || property.DefaultConverter is not null;
        if (!isNullable && !hasOwnConverter && EnumRegistry.HasNoNames(inner))
        {
            bag.Report(Descriptors.EnumWithoutMembers, property.Spot, property.Name, inner.ToDisplayString());
        }

        var read = isNullable ? Wrapped(inner, converter) : converter;
        var write = isNullable ? WriteCondition.WhenNotNull : WriteCondition.Always;
        if (!hasOwnConverter && IsSetType(inner, out var element))
        {
            return new ResolvedConverter(new ConverterModel(converter, read, $"new global::System.Collections.Generic.HashSet<{Names.Of(element)}>()"), WriteCondition.WhenNotEmptyCount, _isPublic, IsAddable: true);
        }

        if (inner is IArrayTypeSymbol array && converter.StartsWith(SetArrayConverter, StringComparison.Ordinal))
        {
            return new ResolvedConverter(new ConverterModel(converter, read, $"global::System.Array.Empty<{Names.Of(array.ElementType)}>()"), WriteCondition.WhenNotEmptyLength, _isPublic, IsAddable: true);
        }

        var forwarder = WritesExplicitly(property, inner) ? Wrapped(inner, converter) : null;
        var omission = Answers(property, inner, "IsOmitted") ? NullableHandle(inner, converter) : null;
        var model = new ConverterModel(converter, read, null, forwarder, omission, CountsAsMissing(property, inner), Answers(property, inner, "TryReadMissing"));
        return new ResolvedConverter(model, write, _isPublic, IsAddable(property, inner));
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

    // What a handle wraps a converter in, which passes the question on through a type parameter: a member that a converter implements explicitly can only be reached that way.
    private static string NullableHandle(ITypeSymbol inner, string converter) =>
        $"{Serialization}{(inner.IsValueType ? "NullableHandleConverter" : "NullableReferenceHandleConverter")}<{Names.Of(inner)}, {converter}>";

    // Generated code calls Write on the type of the converter, and only a read goes through a type parameter. An explicit member cannot be called
    // that way, and the wrappers of null can: they take the value and pass it on through their own parameter.
    // By its accessibility: in metadata, every implementation of a static member looks like an explicit one.
    private static bool WritesExplicitly(PropertyFacts property, ITypeSymbol value) =>
        ImplementationOf(property, value, "Write") is { DeclaredAccessibility: not Accessibility.Public };

    // A converter that leaves the default of the interface alone has no answer of its own, so that its properties stay as they are.
    private static bool Answers(PropertyFacts property, ITypeSymbol value, string member) =>
        ImplementationOf(property, value, member) is { ContainingType: var owner } && owner.OriginalDefinition.ToDisplayString() != WellKnownNames.ConverterInterface;

    // The converter that the generator writes for an enum is no symbol that could be asked, so its answer is what the attribute on the enum says.
    private static bool CountsAsMissing(PropertyFacts property, ITypeSymbol value) => Answers(property, value, "CountsAsMissing") || CountsUnknownNameAsMissing(property, value);

    private static bool CountsUnknownNameAsMissing(PropertyFacts property, ITypeSymbol value) =>
        OwnConverterOf(property, value) is null && value.TypeKind == TypeKind.Enum && EnumRegistry.CountsUnknownAsMissing(value);

    private static ISymbol? ImplementationOf(PropertyFacts property, ITypeSymbol value, string member)
    {
        if (OwnConverterOf(property, value) is not { } converter)
        {
            return null;
        }

        var contract = converter.AllInterfaces.First(candidate => IsConverterOf(candidate, value));
        return converter.FindImplementationForInterfaceMember(contract.GetMembers(member)[0]);
    }

    private static bool IsAddable(ITypeSymbol converter) => converter.AllInterfaces.Any(static candidate => candidate.ToDisplayString() == WellKnownNames.AddableInterface);

    // A number is one as well, which no [OmitEmpty] asks about. A set of a built-in converter does not come here.
    private static bool IsAddable(PropertyFacts property, ITypeSymbol value) => OwnConverterOf(property, value) is { } converter && IsAddable(converter);

    // The converter that the property, the type of its value or the assembly names for all of the value, which leaves a built-in out.
    private static ITypeSymbol? OwnConverterOf(PropertyFacts property, ITypeSymbol value) =>
        (property.Converter ?? AttributeReader.Find(value, WellKnownNames.ConverterAttribute))?.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol ?? property.DefaultConverter;

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

    // A [Version] has to stay a number that an update can add to, so a default for its type passes over it. A string? compares equal to a string, and has to get the wrapper of null first.
    private ITypeSymbol? DefaultOf(ITypeSymbol type, PropertyFacts property) =>
        property.Version is null && type.NullableAnnotation != NullableAnnotation.Annotated ? defaults.ConverterOf(type) : null;

    private string? Inner(ITypeSymbol type, PropertyFacts property, bool isElement = false)
    {
        if (AttributeReader.Find(type, WellKnownNames.ConverterAttribute) is { } attribute)
        {
            return Custom(attribute, type, property.Spot);
        }

        if (DefaultOf(type, property) is { } fallback)
        {
            return Accepted(fallback);
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
            return Inner(inner, property, isElement) is { } wrapped ? Wrapped(inner, wrapped) : null;
        }

        if (!IsDocument(type))
        {
            return Collection(type, property, isElement);
        }

        // The copy is written with its keys, its index keys and its discriminator, and a read of the outer item takes none of them.
        if (isEntity && AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null)
        {
            bag.Report(Descriptors.EntityInsideEntity, property.Spot, property.Name, type.Name);
        }

        return $"{Serialization}DocumentConverter<{code}>";
    }

    // A set that is an element is refused empty before it is written, since only a set property can be left out; see NonEmptySetConverter.
    private string? Collection(ITypeSymbol type, PropertyFacts property, bool isElement)
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
            if (SetElement(named.TypeArguments[0]) is not { } element)
            {
                return Unsupported(property, DefaultHint);
            }

            var converter = $"{Serialization}{set}<{Names.Of(named.TypeArguments[0])}, {element}>";
            return isElement ? $"{Serialization}NonEmptySetConverter<{Names.Of(named)}, {Names.Of(named.TypeArguments[0])}, {converter}>" : converter;
        }

        if (Dictionaries.TryGetValue(definition, out var dictionary) && named.TypeArguments[0].SpecialType == SpecialType.System_String)
        {
            return Element(dictionary, named.TypeArguments[1], property);
        }

        return Unsupported(property, WrongCollections.Contains(definition) ? ListHint : DefaultHint);
    }

    private string? Element(string collection, ITypeSymbol element, PropertyFacts property) =>
        Inner(element, property, isElement: true) is { } converter ? $"{Serialization}{collection}<{Names.Of(element)}, {converter}>" : null;

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

        if (defaults.ConverterOf(element) is { } fallback)
        {
            return Implements(fallback, WellKnownNames.SetElementInterface, element) ? Accepted(fallback) : null;
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
        if (IsUsable(argument.Value, value))
        {
            return Accepted((ITypeSymbol)argument.Value!);
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
