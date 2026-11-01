namespace DynaSharp.Generators;

/// <summary>What a part of an interpolated expression is, which decides what the handler renders for it.</summary>
internal enum HoleKind
{
    /// <summary>Literal text, not a hole.</summary>
    None,

    /// <summary>An attribute, which renders a <c>#n</c> placeholder.</summary>
    Name,

    /// <summary>A value that is not known at compile time or is not a string, which renders a <c>:v</c> placeholder.</summary>
    Value,

    /// <summary>A constant string. It renders a <c>:v</c> placeholder too, but it may be a name that lacks <c>Attr(...)</c>.</summary>
    StringConstant,
}
