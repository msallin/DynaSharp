namespace DynaSharp.Generators;

/// <summary>The tokens of DynamoDB's condition and update grammar.</summary>
internal enum TokenKind
{
    /// <summary>White space, which is never kept.</summary>
    Space,

    /// <summary>A name, keyword or function.</summary>
    Word,

    /// <summary>Digits, which are only legal as a list index.</summary>
    Number,

    /// <summary><c>#name</c>.</summary>
    NamePlaceholder,

    /// <summary><c>:value</c>.</summary>
    ValuePlaceholder,

    /// <summary>A hole of the interpolated string.</summary>
    Hole,

    /// <summary><c>=</c>, which is a comparator and the assignment of <c>SET</c>.</summary>
    Equal,

    /// <summary><c>&lt;&gt;</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c>.</summary>
    Comparator,

    /// <summary><c>(</c>.</summary>
    OpenParenthesis,

    /// <summary><c>)</c>.</summary>
    CloseParenthesis,

    /// <summary><c>[</c>.</summary>
    OpenBracket,

    /// <summary><c>]</c>.</summary>
    CloseBracket,

    /// <summary><c>,</c>.</summary>
    Comma,

    /// <summary><c>.</c>.</summary>
    Dot,

    /// <summary><c>+</c> or <c>-</c>.</summary>
    Sign,

    /// <summary>A character that is in no token: quotes, <c>!</c>, a letter beyond ASCII.</summary>
    Invalid,

    /// <summary>The end of the expression.</summary>
    End,
}
