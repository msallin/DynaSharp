namespace DynaSharp;

/// <summary>
/// Whether a failed condition check returns the item it was evaluated against, in
/// <see cref="ConditionalCheckFailedException.Item"/>, or for an action of a transaction in <see cref="CancellationReason.Item"/>.
/// </summary>
/// <remarks>
/// <see cref="None"/> (the default) sends nothing, which DynamoDB treats as its own <c>NONE</c>.
/// </remarks>
public enum ReturnValuesOnConditionCheckFailure
{
    /// <summary>Return nothing. Sends no <c>ReturnValuesOnConditionCheckFailure</c>, which DynamoDB treats as <c>NONE</c>.</summary>
    None = 0,

    /// <summary>Return the whole existing item, DynamoDB name <c>ALL_OLD</c>. Costs no extra capacity.</summary>
    AllOld,
}
