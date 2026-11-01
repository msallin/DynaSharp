namespace DynaSharp.Configuration;

/// <summary>
/// The value of an INI option: text, or a block of <c>name = value</c> lines (an option without text on its own line whose
/// value continues on the following indented lines).
/// </summary>
/// <param name="Text">The text; for a block, the raw lines starting with a line feed.</param>
/// <param name="Block">The lines of a block by name, or <see langword="null"/> for a plain value.</param>
internal readonly record struct IniValue(string Text, IReadOnlyDictionary<string, string>? Block);
