namespace DynaSharp.UnitTests.Properties;

/// <summary>An attribute value that plain JSON holds as well: strings, numbers, booleans, lists and maps, and null only in a list.</summary>
/// <param name="Value">The value.</param>
public readonly record struct JsonShapedValue(AttributeValue Value);
