namespace DynaSharp.Generators;

/// <summary>The converters of a property, as code. Names are fully qualified; generated enum converters are the exception, see <see cref="EnumModel.ConverterName"/>.</summary>
/// <param name="WriteConverter">For the value that is not null.</param>
/// <param name="ReadConverter">For the whole property, including the wrapper that accepts a <c>NULL</c> value.</param>
/// <param name="EmptySet">The expression that creates an empty set when the attribute is missing, or null for anything but a set.</param>
/// <param name="WriteForwarder">
/// A type with a public <c>Write</c> that reaches <paramref name="WriteConverter"/> through a type parameter, or null if it has such a <c>Write</c> itself. A
/// converter may implement the interface explicitly, and a member like that cannot be called on the type.
/// </param>
/// <param name="OmissionForwarder">
/// A type with a public <c>IsOmitted</c> that reaches <paramref name="WriteConverter"/> through a type parameter, for a converter that has an answer of its own. Null for every other one, whose default answer
/// is false, so that a write of such a property holds no call.
/// </param>
/// <param name="CountsAsMissing">
/// Whether a read of the property asks its converter if the value counts as missing, for a converter that has an answer of its own. It takes no forwarder, because the read goes through a type parameter, which reaches an
/// explicit member, and it takes none for any other converter, whose read holds no question.
/// </param>
/// <param name="AnswersMissing">Whether the converter says for itself what a missing attribute reads as, which is what <c>Select</c> asks.</param>
internal sealed record ConverterModel(string WriteConverter, string ReadConverter, string? EmptySet, string? WriteForwarder = null, string? OmissionForwarder = null, bool CountsAsMissing = false, bool AnswersMissing = false)
{
    /// <summary>Gets the call that writes <paramref name="value"/>, a value that is not null, to <c>writer</c>.</summary>
    /// <param name="value">The value as code.</param>
    /// <returns>The call as code, without the semicolon.</returns>
    public string WriteCall(string value) => $"{WriteForwarder ?? WriteConverter}.Write(writer, {value})";
}
