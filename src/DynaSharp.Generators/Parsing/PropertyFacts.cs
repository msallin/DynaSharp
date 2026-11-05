using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>What the analysis of one type knows about one of its properties. It lives only inside the transform. An override maps like the property it overrides where it has no attribute of the kind itself.</summary>
/// <param name="symbol">The property.</param>
/// <param name="isSettable">Whether generated code inside the type can assign it.</param>
/// <param name="spot">Where to report problems.</param>
internal sealed class PropertyFacts(IPropertySymbol symbol, bool isSettable, Spot spot)
{
    public IPropertySymbol Symbol { get; } = symbol;

    public string Name => Symbol.Name;

    public ITypeSymbol Type => Symbol.Type;

    public Spot Spot { get; } = spot;

    public AttributeData? PartitionKey { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.PartitionKeyAttribute);

    public AttributeData? SortKey { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.SortKeyAttribute);

    public AttributeData? AttributeAttribute { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.AttributeAttribute);

    public AttributeData? KeyOnly { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.KeyOnlyAttribute);

    public AttributeData? Version { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.VersionAttribute);

    public AttributeData? TimeToLive { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.TimeToLiveAttribute);

    /// <summary>Gets a value indicating whether the property is a version that takes an item without one for version 0.</summary>
    public bool AllowsMissingVersion => Version is { } attribute && AttributeReader.Number(attribute, WellKnownNames.VersionMissingArgument) == WellKnownNames.VersionMissingAllow;

    public AttributeData? OmitEmpty { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.OmitEmptyAttribute);

    public AttributeData? Converter { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.ConverterAttribute);

    public AttributeData? ComputedFrom { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.ComputedFromAttribute);

    public bool IsIgnored { get; } = AttributeReader.FindOnProperty(symbol, WellKnownNames.IgnoreAttribute) is not null;

    /// <summary>Gets or sets the constructor parameter that supplies the value, or null.</summary>
    public IParameterSymbol? Parameter { get; set; }

    /// <summary>Gets or sets the builder parameter, unique within the type.</summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute name, set for stored properties.</summary>
    public string AttributeName { get; set; } = symbol.Name;

    /// <summary>Gets or sets the converter that an assembly default gives the property as a whole, which counts as one named on the property, set for stored properties.</summary>
    public ITypeSymbol? DefaultConverter { get; set; }

    /// <summary>Gets or sets the resolved converter, set for stored properties.</summary>
    public ResolvedConverter? Resolved { get; set; }

    /// <summary>Gets or sets the key attribute to parse a <c>[KeyOnly]</c> property from.</summary>
    public string? RestoredFromKey { get; set; }

    /// <summary>Gets or sets the key attribute that holds a stored property without loss, which a read falls back on when the attribute of the property is missing.</summary>
    public string? FallbackKey { get; set; }

    /// <summary>Gets or sets a value indicating whether an empty value counts as null, which the type or the property asks for and only a property outside the keys that a read can do without gets.</summary>
    public bool OmitsEmpty { get; set; }

    /// <summary>Gets a value indicating whether the property is a string or a collection, nullable or not, which is what <c>[OmitEmpty]</c> applies to.</summary>
    public bool CanBeEmpty => Types.Unwrap(Type).Inner is var inner && (inner.SpecialType == SpecialType.System_String || ConverterResolver.ElementOf(inner) is not null);

    public bool IsComputed => !isSettable && Parameter is null;

    /// <summary>Gets a value indicating whether the property has an attribute of its own in the item.</summary>
    public bool IsStored => !IsIgnored && KeyOnly is null && (!IsComputed || AttributeAttribute is not null || PartitionKey is not null || SortKey is not null);
}
