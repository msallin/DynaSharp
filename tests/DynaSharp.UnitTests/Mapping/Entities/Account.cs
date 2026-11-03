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

#pragma warning disable DYNA035 // The tests pin that a read keeps the initializer, which is what DYNA035 warns of.
    public string? Note { get; internal set; } = "kept";
#pragma warning restore DYNA035

    public IList<string> Aliases { get; set; } = ["first"];

    public int[] Scores { get; set; } = [1, 2];
}
