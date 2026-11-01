using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>A call into a key writer that a test supplies; a delegate type because the writer is a ref struct.</summary>
/// <param name="writer">The writer.</param>
public delegate void KeyWriterAction(ref KeyWriter writer);
