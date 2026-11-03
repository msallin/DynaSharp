using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Overrides of the properties of <see cref="Memo"/> without their attributes.</summary>
[DynamoDbDocument]
internal sealed partial class SealedMemo : Memo
{
    public override string Text { get; set; } = string.Empty;

    public override string Secret { get; set; } = string.Empty;
}
