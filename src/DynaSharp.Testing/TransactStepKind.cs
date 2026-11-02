namespace DynaSharp.Testing;

/// <summary>What a <see cref="RecordedTransactStep"/> does: the member its action sets on the wire.</summary>
public enum TransactStepKind
{
    /// <summary>The action sets no member, or one this library does not know. DynamoDB rejects a request with an action that sets none, which the request API can send.</summary>
    Unknown = 0,

    /// <summary>A put of <c>TransactWriteItems</c>.</summary>
    Put,

    /// <summary>An update of <c>TransactWriteItems</c>.</summary>
    Update,

    /// <summary>A delete of <c>TransactWriteItems</c>.</summary>
    Delete,

    /// <summary>A condition check of <c>TransactWriteItems</c>.</summary>
    ConditionCheck,

    /// <summary>A get of <c>TransactGetItems</c>.</summary>
    Get,
}
