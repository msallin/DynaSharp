using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA035: a nullable property that starts with a value, so a null that is written reads back as that value.</summary>
[DynamoDbDocument]
public partial class Memo
{
    public string? Note { get; set; } = "n/a";
}
