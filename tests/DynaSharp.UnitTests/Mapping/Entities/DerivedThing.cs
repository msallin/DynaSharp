using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbEntity]
[Discriminator("DERIVED")]
internal sealed partial class DerivedThing : BaseThing
{
    public int Extra { get; set; }
}
