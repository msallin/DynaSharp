using System.Reflection;
using DynaSharp.Expressions;
using DynaSharp.Serialization;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Expressions;

/// <summary>What only reflection reaches: the overloads of <c>BeginsWith</c> that exist so that a call does not compile, which <c>CompileGuardTests</c> pins.</summary>
public sealed class AttrExtensionsTests
{
    [Test]
    public async Task BeginsWith_EveryRejectedOverload_IsAnErrorAndKeepsTheCondition()
    {
        // Arrange
        var rejected = typeof(AttrExtensions).GetMethods().Where(static method => method.GetCustomAttribute<ObsoleteAttribute>() is not null).ToList();

        // Act
        var conditions = rejected.Select(static method => (method.GetCustomAttribute<ObsoleteAttribute>()!.IsError, Invoke(method))).ToList();

        // Assert
        await Assert.That((rejected.Count, conditions.All(static condition => condition.IsError && condition.Item2 is BeginsWith))).IsEqualTo((12, true));
    }

    [Test]
    public async Task BeginsWith_HandleOfBytes_SendsABinaryPrefix()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(new Attr<byte[], BinaryConverter>("blob").BeginsWith(new byte[] { 1, 2 })).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"ConditionExpression":"begins_with(#n0, :v0)","ExpressionAttributeNames":{"#n0":"blob"},"ExpressionAttributeValues":{":v0":{"B":"AQI="}}}""");
    }

    [Test]
    public async Task BeginsWith_HandleOfBytesWithOmitEmpty_SendsABinaryPrefix()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(new Attr<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>>("blob").BeginsWith(new byte[] { 1, 2 })).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"B":"AQI="}
            """);
    }

    [Test]
    public async Task BeginsWith_HandleOfNullableBytesWithOmitEmpty_SendsABinaryPrefix()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(new Attr<byte[], NullableReferenceHandleConverter<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>>>("blob").BeginsWith(new byte[] { 1, 2 })).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"B":"AQI="}
            """);
    }

    [Test]
    public async Task BeginsWith_HandleOfMemory_SendsABinaryPrefix()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        ReadOnlyMemory<byte> prefix = new byte[] { 1, 2 };

        // Act
        var json = client.Table("Shop", "pk").Scan().Filter(new Attr<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>("blob").BeginsWith(prefix)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"B":"AQI="}
            """);
    }

    [Test]
    public async Task BeginsWith_HandleOfNullableBytes_SendsABinaryPrefix()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Scan().Filter(new Attr<byte[], NullableReferenceHandleConverter<byte[], BinaryConverter>>("blob").BeginsWith(new byte[] { 1, 2 })).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"B":"AQI="}
            """);
    }

    [Test]
    public async Task BeginsWith_HandleOfNullableMemory_SendsABinaryPrefix()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        ReadOnlyMemory<byte> prefix = new byte[] { 1, 2 };

        // Act
        var json = client.Table("Shop", "pk").Scan().Filter(new Attr<ReadOnlyMemory<byte>, NullableHandleConverter<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>>("blob").BeginsWith(prefix)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"B":"AQI="}
            """);
    }

    private static object? Invoke(MethodInfo method)
    {
        var attribute = method.GetParameters()[0].ParameterType;
        var closed = method.IsGenericMethodDefinition ? method.MakeGenericMethod(typeof(int)) : method;
        var handle = closed.GetParameters()[0].ParameterType;
        return closed.Invoke(null, [Activator.CreateInstance(attribute.IsGenericTypeDefinition || attribute.ContainsGenericParameters ? handle : attribute, "n"), "1"]);
    }
}
