namespace DynaSharp;

/// <summary>
/// Which item attributes a write operation returns.
/// </summary>
/// <remarks>
/// <see cref="None"/> (the default) sends nothing, and DynamoDB returns nothing, which is also what its <c>NONE</c> means.
/// <c>PutItem</c> and <c>DeleteItem</c> accept only <see cref="None"/> and <see cref="AllOld"/>; <c>UpdateItem</c> accepts all.
/// DynamoDB rejects other combinations with a <see cref="ValidationException"/>.
/// </remarks>
public enum ReturnValue
{
    /// <summary>Return nothing. Sends no <c>ReturnValues</c>, which DynamoDB treats as <c>NONE</c>.</summary>
    None = 0,

    /// <summary>Return the whole item as it was before the write, DynamoDB name <c>ALL_OLD</c>.</summary>
    AllOld,

    /// <summary>Return only the updated attributes, as they were before the write, DynamoDB name <c>UPDATED_OLD</c>.</summary>
    UpdatedOld,

    /// <summary>Return the whole item as it is after the write, DynamoDB name <c>ALL_NEW</c>.</summary>
    AllNew,

    /// <summary>Return only the updated attributes, as they are after the write, DynamoDB name <c>UPDATED_NEW</c>.</summary>
    UpdatedNew,
}
