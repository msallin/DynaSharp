namespace DynaSharp.Configuration;

/// <summary>
/// The value of an INI option: text, or a block of <c>name = value</c> lines (an option without text on its own line whose
/// value continues on the following indented lines). No setting that DynaSharp reads is a block, so a block counts as not set.
/// </summary>
/// <param name="Text">The text; for a block, the raw lines starting with a line feed.</param>
/// <param name="IsBlock">Whether the value is a block.</param>
internal readonly record struct IniValue(string Text, bool IsBlock);
