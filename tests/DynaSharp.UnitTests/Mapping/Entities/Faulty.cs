using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial class Faulty
{
    [DynamoDbConverter(typeof(BuggyConverter))]
    public string Buggy { get; set; } = string.Empty;
}
