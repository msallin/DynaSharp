using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DynaSharp.Operations;

/// <summary>
/// The wire names of the members of one enum, in one immutable table that serves both directions: the request writer looks a
/// member up by value, the response reader by name.
/// </summary>
/// <typeparam name="TEnum">The enum.</typeparam>
/// <remarks>
/// The zero member is not in the table. It stands for "not set" (<c>Unspecified</c>), "not known" (<c>Unknown</c>) or the default of the
/// service (<c>None</c>) and has no wire name, so writing it is an error for a required enum and means "omit" for an optional one, and
/// a name that is not in the table reads as the zero member. A dictionary and not a <see langword="switch"/> on strings: a switch with many cases compiles to
/// branches that tests cannot all reach.
/// </remarks>
internal sealed class EnumWireNames<TEnum>
    where TEnum : struct, Enum
{
    private readonly string _typeName;
    private readonly FrozenDictionary<string, TEnum> _valuesByName;
    private readonly FrozenDictionary<TEnum, string> _namesByValue;

    /// <summary>Initializes a new instance of the <see cref="EnumWireNames{TEnum}"/> class.</summary>
    /// <param name="typeName">The name of the enum, for the message about a value that has no name.</param>
    /// <param name="members">Every member that has a wire name, which is every member except the zero member.</param>
    public EnumWireNames(string typeName, params (TEnum Value, string Name)[] members)
    {
        _typeName = typeName;
        var valuesByName = new Dictionary<string, TEnum>(members.Length, StringComparer.Ordinal);
        var namesByValue = new Dictionary<TEnum, string>(members.Length);
        foreach (var (value, name) in members)
        {
            valuesByName.Add(name, value);
            namesByValue.Add(value, name);
        }

        _valuesByName = valuesByName.ToFrozenDictionary(StringComparer.Ordinal);
        _namesByValue = namesByValue.ToFrozenDictionary();
    }

    /// <summary>Looks a member up by its wire name.</summary>
    /// <param name="name">The wire name.</param>
    /// <param name="value">The member, or the zero member if the name is not in the table.</param>
    /// <returns><see langword="true"/> if the name is in the table.</returns>
    public bool TryGetValue(string name, out TEnum value) => _valuesByName.TryGetValue(name, out value);

    /// <summary>Looks the wire name of a member up.</summary>
    /// <param name="value">The member.</param>
    /// <param name="name">The wire name, or <see langword="null"/> if the value is not in the table.</param>
    /// <returns><see langword="true"/> if the value is in the table.</returns>
    public bool TryGetName(TEnum value, [NotNullWhen(true)] out string? name) => _namesByValue.TryGetValue(value, out name);

    /// <summary>Writes a property whose value is the wire name of a member.</summary>
    /// <param name="json">The writer.</param>
    /// <param name="propertyName">The UTF-8 name of the property.</param>
    /// <param name="value">The member.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not in the table, which includes the zero member.</exception>
    public void Write(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, TEnum value) =>
        json.WriteString(propertyName, _namesByValue.TryGetValue(value, out var name) ? name : throw NotKnown(value));

    /// <summary>Writes a property like <see cref="Write"/>, except for the zero member, which leaves the property out.</summary>
    /// <param name="json">The writer.</param>
    /// <param name="propertyName">The UTF-8 name of the property.</param>
    /// <param name="value">The member.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not in the table and is not the zero member.</exception>
    public void WriteIfSet(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, TEnum value)
    {
        if (GetNameIfSet(value) is { } name)
        {
            json.WriteString(propertyName, name);
        }
    }

    /// <summary>Gets the wire name of a member for the request, except for the zero member, which is left out.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The wire name, or <see langword="null"/> for the zero member.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not in the table and is not the zero member.</exception>
    public string? GetNameIfSet(TEnum value)
    {
        if (EqualityComparer<TEnum>.Default.Equals(value, default))
        {
            return null;
        }

        return _namesByValue.TryGetValue(value, out var name) ? name : throw NotKnown(value);
    }

    private ArgumentOutOfRangeException NotKnown(TEnum value) => new(nameof(value), value, $"Not a known {_typeName}.");
}
