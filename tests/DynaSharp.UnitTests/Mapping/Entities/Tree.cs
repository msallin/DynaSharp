using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial class Tree
{
    public string Name { get; set; } = string.Empty;

    public List<Tree> Children { get; set; } = [];

    public Tree? Parent { get; set; }
}
