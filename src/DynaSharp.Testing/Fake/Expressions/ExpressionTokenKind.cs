namespace DynaSharp.Testing.Fake;

/// <summary>The tokens of DynamoDB's expression grammar, which conditions, updates, key conditions and projections share.</summary>
internal enum ExpressionTokenKind
{
    /// <summary>A name, a keyword or a function: ASCII letters, digits and underscores, starting with a letter.</summary>
    Word,

    /// <summary>Digits, which are only legal as a list index.</summary>
    Number,

    /// <summary><c>#name</c>.</summary>
    NamePlaceholder,

    /// <summary><c>:value</c>.</summary>
    ValuePlaceholder,

    /// <summary><c>=</c>, the comparator and the assignment of <c>SET</c>.</summary>
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

    /// <summary><c>+</c> or <c>-</c>, which only an update takes.</summary>
    Sign,

    /// <summary>A character that is in no token: a quote, <c>;</c>, a letter beyond ASCII, a lone <c>#</c> or <c>:</c>.</summary>
    Invalid,

    /// <summary>The end of the expression.</summary>
    End,
}
