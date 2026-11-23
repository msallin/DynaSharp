using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// An entity written by hand with a key that has no inputs and nothing that formats it, which the generator never emits. An index with such a key is no index of literals, and the
/// other index has one, on a sort key the table does not have: only an index whose keys are all literals is written.
/// </summary>
internal sealed record HandLiteral(string Id) : IDynamoDbEntity<HandLiteral>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new(
        null,
        "_type",
        "pk",
        ScalarAttributeType.Unknown,
        null,
        ScalarAttributeType.Unknown,
        [new DynamoDbIndexInfo("Unformatted", "plain", null, false, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null), new DynamoDbIndexInfo("Halfway", "written", "plain", false, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null), new DynamoDbIndexInfo("Written", "written", null, false, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null)],
        null,
        keyTemplates:
        [
            new DynamoDbKeyTemplate("plain", "P", false, []),
            new DynamoDbKeyTemplate("written", "FIXED", false, [], static _ => "FIXED"),
        ]);

    static DynamoDbEntityInfo IDynamoDbEntity<HandLiteral>.Info => EntityInfo;

    static void IDynamoDbEntity<HandLiteral>.WriteKey(DynamoDbWriter writer, HandLiteral entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<HandLiteral>.WriteAttributes(DynamoDbWriter writer, HandLiteral entity) => writer.WriteAttributeName("pk"u8);

    static HandLiteral IDynamoDbDocument<HandLiteral>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
