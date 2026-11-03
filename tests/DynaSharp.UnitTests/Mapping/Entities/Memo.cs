using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Mapping attributes on <see langword="virtual"/> properties, which an override that repeats none of them keeps, see <see cref="SealedMemo"/>.</summary>
[DynamoDbDocument]
internal partial class Memo
{
    [DynamoDbAttribute("t")]
    public virtual string Text { get; set; } = string.Empty;

    [DynamoDbIgnore]
    public virtual string Secret { get; set; } = string.Empty;
}
