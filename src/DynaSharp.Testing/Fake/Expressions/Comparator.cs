namespace DynaSharp.Testing.Fake;

/// <summary>The comparators of a condition.</summary>
internal enum Comparator
{
    /// <summary><c>=</c>.</summary>
    Equal,

    /// <summary><c>&lt;&gt;</c>.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>.</summary>
    LessThan,

    /// <summary><c>&lt;=</c>.</summary>
    LessThanOrEqual,

    /// <summary><c>&gt;</c>.</summary>
    GreaterThan,

    /// <summary><c>&gt;=</c>.</summary>
    GreaterThanOrEqual,
}
