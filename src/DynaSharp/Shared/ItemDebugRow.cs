using System.Diagnostics;

namespace DynaSharp;

/// <summary>One attribute of an <see cref="Item"/> in the debugger.</summary>
/// <param name="name">The attribute name.</param>
/// <param name="type">The data type.</param>
/// <param name="value">The value, which the debugger shows as DynamoDB JSON.</param>
[DebuggerDisplay("{Value}", Name = "{Name,nq}", Type = "{Type}")]
internal sealed class ItemDebugRow(string name, AttributeType type, AttributeValue value)
{
    /// <summary>Gets the attribute name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the data type.</summary>
    public AttributeType Type { get; } = type;

    /// <summary>Gets the value.</summary>
    public AttributeValue Value { get; } = value;
}
