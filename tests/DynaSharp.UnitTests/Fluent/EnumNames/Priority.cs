using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.EnumNames;

/// <summary>An attribute enum with stored names, whose <see cref="Minor"/> is an alias of <see cref="Low"/> that carries an older name, and which counts a name it does not have as missing.</summary>
[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
internal enum Priority
{
    [DynamoDbEnumName("LOW")]
    Low = 1,

    [DynamoDbEnumName("LO")]
    Minor = Low,

    [DynamoDbEnumName("HIGH")]
    High = 2,

    Normal = 3,
}
