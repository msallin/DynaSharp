namespace DynaSharp.Fluent;

/// <summary>
/// Says that a command is a read that ends in a terminal of its own and cannot be awaited or sent, which is what <see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA202</see> goes by: the analyzer runs in the compilation of a consumer, where the names of
/// the terminals differ between the reads, and this interface is the one thing they share. It has no members, so nothing is called through it and a ref struct is never boxed for it.
/// </summary>
internal interface IReadCommand;
