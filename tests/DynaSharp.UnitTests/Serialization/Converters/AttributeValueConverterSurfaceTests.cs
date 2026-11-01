using System.Reflection;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>What a consumer can do with <see cref="AttributeValueConverter"/> now that it is public: use it like any other converter.</summary>
public sealed class AttributeValueConverterSurfaceTests
{
    public static IEnumerable<(string Json, AttributeType Found)> AllTypes() => AttributeSamples.Except(AttributeType.None);

    [Test]
    public async Task Type_IsAPublicStruct()
    {
        // Arrange
        var type = typeof(AttributeValueConverter);

        // Act
        var shape = (type.IsPublic, type.IsValueType, type.IsSealed);

        // Assert
        await Assert.That(shape).IsEqualTo((true, true, true));
    }

    [Test]
    [Arguments("WriteItem")]
    [Arguments("ReadItem")]
    [Arguments("ReadAttributes")]
    public async Task Type_ItemMethods_AreNotPublic(string name)
    {
        // Arrange
        // Act
        var publicMethods = typeof(AttributeValueConverter)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Count(method => method.Name == name);

        // Assert
        await Assert.That(publicMethods).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(nameof(AllTypes))]
    public async Task ReadAttribute_EveryType_WritesBackTheSameJson(string json, AttributeType found)
    {
        // Arrange
        var value = ConverterRun.Read<AttributeValue, AttributeValueConverter>(json);

        // Act
        var written = ConverterRun.Write<AttributeValue, AttributeValueConverter>(value);

        // Assert
        await Assert.That((value.Type, written)).IsEqualTo((found, json));
    }

    [Test]
    public async Task Write_DefaultValue_ThrowsArgumentException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<AttributeValue, AttributeValueConverter>(default);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Read_TypeNone_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        // Act
        var act = () => TestJson.Read("""{"S":"a"}""", static (ref reader) => AttributeValueConverter.Read(ref reader, AttributeType.None));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Read_EmptyStringSet_ThrowsJsonException()
    {
        // Arrange (DynamoDB never sends one)
        // Act
        var act = () => ConverterRun.Read<AttributeValue, AttributeValueConverter>("""{"SS":[]}""");

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("A DynamoDB set must contain at least one element.", StringComparison.Ordinal);
    }
}
