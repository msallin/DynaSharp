using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An enum whose generated converter counts a name it does not have as missing. <see cref="Unknown"/> is no default, so that a property that holds it was left alone by the read.</summary>
[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
internal enum Wind
{
    Calm,
    Breeze,
    Gale,
    Unknown,
}
