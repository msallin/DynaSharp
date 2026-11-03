namespace DynaSharp.Fluent;

/// <summary>
/// The parts of the condition of a write, by where they come from, so that the message of a failed one can name them. DynamoDB does not say which part failed, so the message names all
/// that were there. It holds kinds and never a value, see <see cref="FailedCondition.Explain"/>.
/// </summary>
/// <remarks>When adding a part: set it where its condition is written, and name it in <see cref="FailedCondition.Explain"/>.</remarks>
[Flags]
internal enum ConditionParts : byte
{
    /// <summary>No condition.</summary>
    None = 0,

    /// <summary>The version attribute holds the version the write expects, or reads as 0 on an entity that allows an item without one.</summary>
    Version = 1,

    /// <summary>The version the write expects marks a new item, so its condition is that no item has the key.</summary>
    NewVersion = 2,

    /// <summary><c>IfNotExists</c>: no item has the key. It renders as one condition with <see cref="NewVersion"/>.</summary>
    NoItem = 4,

    /// <summary><c>IfExists</c>: an item has the key.</summary>
    ItemExists = 8,

    /// <summary>A condition of the caller, from <c>If</c>.</summary>
    Caller = 16,
}
