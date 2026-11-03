namespace DynaSharp.Serialization;

/// <summary>
/// A <see cref="DateTime"/> of <see cref="DateTimeKind.Unspecified"/> that <see cref="DateTimeConverter"/> refuses to write. It has a type of its own so that the code the source generator
/// emits can tell it from any other <see cref="ArgumentException"/> and name the property, see <see cref="ConverterChecks.IsRefusedValue"/>.
/// </summary>
/// <param name="paramName">The parameter that held the value.</param>
internal sealed class UnspecifiedDateTimeException(string paramName)
    : ArgumentException("The kind of the DateTime is Unspecified, so it names no instant. A DateTime that was never set, default(DateTime), has this kind: set it to a UTC time first. Only for times that the AWS SDK wrote, use SdkDateTimeConverter, which takes it for local time as the SDK does.", paramName);
