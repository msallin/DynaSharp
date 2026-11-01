using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A plain class: settable, private setter, protected setter, initializers, no attribute on most of it.</summary>
[DynamoDbEntity]
[Discriminator("ACCOUNT", AttributeName = "kind")]
internal sealed partial class Account
{
    [PartitionKey]
    public string Id { get; set; } = "unset";

    public string Plan { get; set; } = "free";

    public int Logins { get; private set; } = 7;

    public string? Note { get; internal set; } = "kept";

    public IList<string> Aliases { get; set; } = ["first"];

    public int[] Scores { get; set; } = [1, 2];
}
