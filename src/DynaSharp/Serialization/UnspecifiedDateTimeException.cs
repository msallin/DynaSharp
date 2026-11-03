namespace DynaSharp.Serialization;

/// <summary>
/// A <see cref="DateTime"/> of <see cref="DateTimeKind.Unspecified"/> that <see cref="DateTimeConverter"/> refuses to write. It has a type of its own so that the code the source generator
/// emits can tell it from any other <see cref="ArgumentException"/> and name the property, see <see cref="ConverterChecks.IsRefusedValue"/>.
/// </summary>
/// <param name="paramName">The parameter that held the value.</param>
internal sealed class UnspecifiedDateTimeException(string paramName)
    : ArgumentException("The kind of the DateTime is Unspecified, so it names no instant. Use DateTimeKind.Utc or DateTimeKind.Local, or SdkDateTimeConverter, which takes it for local time as the AWS SDK does.", paramName);
