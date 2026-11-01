using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>What the analysis of one type knows about one of its properties. It lives only inside the transform.</summary>
/// <param name="symbol">The property.</param>
/// <param name="isSettable">Whether generated code inside the type can assign it.</param>
/// <param name="spot">Where to report problems.</param>
internal sealed class PropertyFacts(IPropertySymbol symbol, bool isSettable, Spot spot)
{
    public IPropertySymbol Symbol { get; } = symbol;

    public string Name => Symbol.Name;

    public ITypeSymbol Type => Symbol.Type;

    public Spot Spot { get; } = spot;

    public AttributeData? PartitionKey { get; } = AttributeReader.Find(symbol, WellKnownNames.PartitionKeyAttribute);

    public AttributeData? SortKey { get; } = AttributeReader.Find(symbol, WellKnownNames.SortKeyAttribute);

    public AttributeData? AttributeAttribute { get; } = AttributeReader.Find(symbol, WellKnownNames.AttributeAttribute);

    public AttributeData? KeyOnly { get; } = AttributeReader.Find(symbol, WellKnownNames.KeyOnlyAttribute);

    public AttributeData? Version { get; } = AttributeReader.Find(symbol, WellKnownNames.VersionAttribute);

    public AttributeData? OmitEmpty { get; } = AttributeReader.Find(symbol, WellKnownNames.OmitEmptyAttribute);

    public AttributeData? Converter { get; } = AttributeReader.Find(symbol, WellKnownNames.ConverterAttribute);

    public bool IsIgnored { get; } = AttributeReader.Find(symbol, WellKnownNames.IgnoreAttribute) is not null;

    /// <summary>Gets or sets the constructor parameter that supplies the value, or null.</summary>
    public IParameterSymbol? Parameter { get; set; }

    /// <summary>Gets or sets the builder parameter, unique within the type.</summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute name, set for stored properties.</summary>
    public string AttributeName { get; set; } = symbol.Name;

    /// <summary>Gets or sets the resolved converter, set for stored properties.</summary>
    public ResolvedConverter? Resolved { get; set; }

    /// <summary>Gets or sets the key attribute to parse a <c>[KeyOnly]</c> property from.</summary>
    public string? RestoredFromKey { get; set; }

    /// <summary>Gets or sets the key attribute that holds a stored property without loss, which a read falls back on when the attribute of the property is missing.</summary>
    public string? FallbackKey { get; set; }

    /// <summary>Gets or sets a value indicating whether an empty string counts as null, which the type or the property asks for and only a property outside the keys gets.</summary>
    public bool OmitsEmpty { get; set; }

    /// <summary>Gets a value indicating whether the property is a <c>string?</c>, the one thing <c>[OmitEmpty]</c> applies to.</summary>
    public bool IsNullableString => Types.Unwrap(Type) is { Inner.SpecialType: SpecialType.System_String, IsNullable: true };

    public bool IsComputed => !isSettable && Parameter is null;

    /// <summary>Gets a value indicating whether the property has an attribute of its own in the item.</summary>
    public bool IsStored => !IsIgnored && KeyOnly is null && (!IsComputed || AttributeAttribute is not null || PartitionKey is not null || SortKey is not null);
}
