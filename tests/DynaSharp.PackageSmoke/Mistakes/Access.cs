using DynaSharp.Mapping;

namespace Mistakes;

// DYNA033: a [Flags] enum is stored as a number, which has no name to miss, so the attribute changes nothing.
[Flags]
[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
}
