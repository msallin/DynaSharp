using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record Pair
{
    public Pair(string x, string y)
    {
        X = x;
        Y = y;
    }

    [DynamoDbAttribute("x")]
    public string X { get; }

    [DynamoDbAttribute("y")]
    public string Y { get; }
}
