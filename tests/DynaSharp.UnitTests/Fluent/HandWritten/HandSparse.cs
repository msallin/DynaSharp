using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity written by hand with a sparse index that has a key without a template, which the generator never emits.</summary>
internal sealed record HandSparse(string Id) : IDynamoDbEntity<HandSparse>
{
    public static readonly Attr<string, StringConverter> Tag = new("tag");

    private static readonly DynamoDbEntityInfo EntityInfo = new(
        "SPARSE",
        "_type",
        "pk",
        ScalarAttributeType.Unknown,
        null,
        ScalarAttributeType.Unknown,
        [new DynamoDbIndexInfo("ByTag", "derived", "other", true, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null)],
        null,
        keyTemplates: [new DynamoDbKeyTemplate("derived", "T#{Tag}", false, [new DynamoDbKeyInput("Tag", "tag")], static values => "T#" + values[0])]);

    static DynamoDbEntityInfo IDynamoDbEntity<HandSparse>.Info => EntityInfo;

    static void IDynamoDbEntity<HandSparse>.WriteKey(DynamoDbWriter writer, HandSparse entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<HandSparse>.WriteAttributes(DynamoDbWriter writer, HandSparse entity) => writer.WriteAttributeName("pk"u8);

    static HandSparse IDynamoDbDocument<HandSparse>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
