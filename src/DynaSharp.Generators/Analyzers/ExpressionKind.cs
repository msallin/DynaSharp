namespace DynaSharp.Generators;

/// <summary>What an expression is, which decides the grammar it is read with.</summary>
internal enum ExpressionKind
{
    /// <summary>Not known, because a method of the consumer took it. Only its text is read, since it may be either grammar.</summary>
    Unknown,

    /// <summary>A condition, filter or key condition.</summary>
    Condition,

    /// <summary>A whole update expression, with its <c>SET</c>, <c>REMOVE</c>, <c>ADD</c> and <c>DELETE</c> keywords.</summary>
    Update,

    /// <summary>What <c>Set($"...")</c> takes: an update expression that is already inside its <c>SET</c> clause.</summary>
    SetAction,
}
