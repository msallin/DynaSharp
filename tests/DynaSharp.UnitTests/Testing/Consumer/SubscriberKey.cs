using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Testing.Consumer;

/// <summary>The key of a <see cref="Subscriber"/>, written by hand until the generator emits keys.</summary>
/// <param name="Id">The subscriber.</param>
internal readonly record struct SubscriberKey(string Id) : IKey<Subscriber>
{
    public void WriteKey(DynamoDbWriter writer)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(Id);
    }
}
