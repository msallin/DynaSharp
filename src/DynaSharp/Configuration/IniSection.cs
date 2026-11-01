namespace DynaSharp.Configuration;

/// <summary>
/// A section of an INI file: its name as written between the brackets, and its options.
/// </summary>
/// <param name="Name">The text between the brackets, not trimmed, for example <c>profile dev</c>.</param>
/// <param name="Values">The options by lower-cased name, including the options inherited from the <c>[DEFAULT]</c> section.</param>
internal sealed record IniSection(string Name, IReadOnlyDictionary<string, IniValue> Values);
