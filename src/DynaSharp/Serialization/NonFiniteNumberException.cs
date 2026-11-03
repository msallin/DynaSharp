namespace DynaSharp.Serialization;

/// <summary>NaN or an infinity, which DynamoDB cannot store. It has a type of its own for the same reason as <see cref="UnspecifiedDateTimeException"/>.</summary>
/// <param name="paramName">The parameter that held the value.</param>
/// <param name="value">The value, which says nothing about data: it is NaN or an infinity.</param>
internal sealed class NonFiniteNumberException(string? paramName, object value)
    : ArgumentOutOfRangeException(paramName, value, "DynamoDB cannot store NaN or infinity.");
