namespace DynaSharp.UnitTests.Properties;

/// <summary>Text that is not valid UTF-16: it holds a surrogate without its other half. A type of its own, so that a property asks for it by name.</summary>
/// <param name="Value">The text.</param>
public readonly record struct LoneSurrogateText(string Value);
