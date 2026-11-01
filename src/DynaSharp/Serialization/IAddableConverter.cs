using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Serialization;

/// <summary>
/// Marks the converter of a number or a set, the only types that the <c>ADD</c> action of an update takes. A typed <c>Add</c> requires it, so
/// adding to a handle of a string is a compile error rather than a <see cref="ValidationException"/>. Implement it on a custom converter that writes
/// a number (<c>N</c>) or a set.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "It exists for a generic constraint, which an attribute cannot express.")]
public interface IAddableConverter;
