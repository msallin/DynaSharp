using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>A type written by hand that claims the interface and has no discriminator, which only a person can get wrong.</summary>
internal sealed record Undeclared(string Id) : IDynamoDbDiscriminated<Undeclared>
{
    static DynamoDbEntityInfo IDynamoDbEntity<Undeclared>.Info { get; } = new(null, "pk", null, []);

    static void IDynamoDbEntity<Undeclared>.WriteKey(DynamoDbWriter writer, Undeclared entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<Undeclared>.WriteAttributes(DynamoDbWriter writer, Undeclared entity) => writer.WriteAttributeName("pk"u8);

    static Undeclared IDynamoDbDocument<Undeclared>.ReadAttributes(ref DynamoDbReader reader) => new("a");
}
