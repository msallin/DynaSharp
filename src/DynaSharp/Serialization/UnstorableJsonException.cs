namespace DynaSharp.Serialization;

/// <summary>
/// JSON that DynamoDB cannot store as it is: a number beyond its limits, nesting beyond 32 levels, an empty name, a name twice in an object, text that is not valid UTF-8 or UTF-16, or a default
/// <see cref="System.Text.Json.JsonElement"/>. It has a type of its own for the same reason as <see cref="UnspecifiedDateTimeException"/>.
/// </summary>
/// <param name="reason">What is wrong, with the path in the JSON and never a value.</param>
/// <param name="paramName">The parameter that held the JSON.</param>
/// <param name="innerException">What the JSON reader threw, if anything.</param>
internal sealed class UnstorableJsonException(string reason, string paramName, Exception? innerException = null)
    : ArgumentException(reason, paramName, innerException)
{
    /// <summary>Gets what is wrong, without the name of the parameter that <see cref="ArgumentException.Message"/> adds.</summary>
    public string Reason { get; } = reason;
}
