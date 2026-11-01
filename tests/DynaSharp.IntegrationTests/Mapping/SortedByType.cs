using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A discriminator that is the sort key of the table, which the generator rejects and a type written by hand can have.</summary>
internal sealed record SortedByType(string Id) : IDynamoDbEntity<SortedByType>
{
    public const string Discriminator = "NOTE";

    private static readonly DynamoDbEntityInfo EntityInfo = new(Discriminator, "pk", "sk", [], discriminatorAttributeName: "sk");

    static DynamoDbEntityInfo IDynamoDbEntity<SortedByType>.Info => EntityInfo;

    static void IDynamoDbEntity<SortedByType>.WriteKey(DynamoDbWriter writer, SortedByType entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<SortedByType>.WriteAttributes(DynamoDbWriter writer, SortedByType entity) => writer.WriteAttributeName("pk"u8);

    static SortedByType IDynamoDbDocument<SortedByType>.ReadAttributes(ref DynamoDbReader reader) => new("a");
}
