using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial class Bound
{
    public Bound(string name)
    {
        Name = name;
    }

    public required string Name { get; init; }
}
