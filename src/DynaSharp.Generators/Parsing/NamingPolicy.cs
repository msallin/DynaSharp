namespace DynaSharp.Generators;

/// <summary>How a stored attribute derives from a property. The values are those of <c>DynaSharp.Mapping.AttributeNaming</c>, which the generator can only read as a number.</summary>
internal enum NamingPolicy
{
    /// <summary>The name of the property, unchanged.</summary>
    AsDeclared,

    /// <summary>See <see cref="NameCasing.ToCamelCase"/>.</summary>
    CamelCase,

    /// <summary>See <see cref="NameCasing.ToSnakeCase"/>.</summary>
    SnakeCase,
}
