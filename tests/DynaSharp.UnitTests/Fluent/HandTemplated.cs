using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity written by hand with a key template that no index names, which the generator never emits.</summary>
internal sealed record HandTemplated(string Id) : IDynamoDbEntity<HandTemplated>
{
    public static readonly Attr<string, StringConverter> Tag = new("tag");

    private static readonly DynamoDbEntityInfo EntityInfo = new(
        "HAND",
        "pk",
        null,
        [],
        keyTemplates: [new DynamoDbKeyTemplate("derived", "T#{Tag}", false, [new DynamoDbKeyInput("Tag", "tag")], static values => "T#" + values[0])]);

    static DynamoDbEntityInfo IDynamoDbEntity<HandTemplated>.Info => EntityInfo;

    static void IDynamoDbEntity<HandTemplated>.WriteKey(DynamoDbWriter writer, HandTemplated entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Id);
    }

    static void IDynamoDbDocument<HandTemplated>.WriteAttributes(DynamoDbWriter writer, HandTemplated entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Id);
    }

    static HandTemplated IDynamoDbDocument<HandTemplated>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
