namespace DynaSharp.Testing.Fake;

/// <summary>The forms of a condition on a sort key that DynamoDB accepts in a key condition.</summary>
internal enum SortKeyOperator
{
    /// <summary><c>sk = :v</c>.</summary>
    Equal,

    /// <summary><c>sk &lt; :v</c>.</summary>
    LessThan,

    /// <summary><c>sk &lt;= :v</c>.</summary>
    LessThanOrEqual,

    /// <summary><c>sk &gt; :v</c>.</summary>
    GreaterThan,

    /// <summary><c>sk &gt;= :v</c>.</summary>
    GreaterThanOrEqual,

    /// <summary><c>sk BETWEEN :a AND :b</c>, both ends included.</summary>
    Between,

    /// <summary><c>begins_with(sk, :v)</c>, on a string or a binary.</summary>
    BeginsWith,
}
