using DynaSharp.Generators;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Generators;

// The generator has no reference to the library, so WriteEmitter knows the built-in converters that refuse a value on write (the ones
// ConverterChecks.IsRefusedValue catches) by name. Every built-in converter is placed here as refusing or not: a new one fails the first test
// until it is, and the second holds the list of the emitter against the refusing ones.
public sealed class WriteEmitterTests
{
    private const string Prefix = "global::DynaSharp.Serialization.";

    private static readonly Dictionary<string, string[]> Refusing = new(StringComparer.Ordinal)
    {
        // A DateTime of DateTimeKind.Unspecified.
        ["DateTimeConverter"] = [Prefix + "DateTimeConverter"],

        // NaN and the infinities, of the number types that have them.
        ["NumberConverter"] = [Prefix + "NumberConverter<double>", Prefix + "NumberConverter<float>", Prefix + "NumberConverter<global::System.Half>"],
        ["NumberSetElement"] = [Prefix + "NumberSetElement<double>", Prefix + "NumberSetElement<float>", Prefix + "NumberSetElement<global::System.Half>"],

        // An empty set in a list or a map.
        ["NonEmptySetConverter"] = [Prefix + "NonEmptySetConverter<"],

        // An empty key, which DynamoDB does not take as a name in a map.
        ["DictionaryConverter"] = [Prefix + "DictionaryConverter<"],
        ["ReadOnlyDictionaryConverter"] = [Prefix + "ReadOnlyDictionaryConverter<"],

        // JSON that DynamoDB cannot store.
        ["JsonElementConverter"] = [Prefix + "JsonElementConverter"],
    };

    private static readonly string[] Accepting =
    [
        "ArrayConverter", "AttributeValueConverter", "BinaryConverter", "BinarySetElement", "BoolAsNumberConverter", "BoolConverter", "DateOnlyConverter",
        "DateTimeOffsetConverter", "DocumentConverter", "EmptyStringAsMissingConverter", "EpochSecondsConverter", "GuidConverter",
        "GuidSetElement", "HashSetConverter", "ListConverter", "MutableListConverter", "NullableConverter", "NullableHandleConverter",
        "NullableReferenceConverter", "NullableReferenceHandleConverter", "OffsetTimestampConverter", "OmitEmptyCollectionConverter", "OmitEmptyConverter",
        "OmitEmptySetConverter", "ReadOnlyListConverter", "ReadOnlyMemoryConverter", "ReadOnlySetConverter",
        "SdkDateTimeConverter", "SetArrayConverter", "SetElementValueConverter", "StringConverter", "StringSetElement", "TimeOnlyConverter",
        "TimeSpanConverter",
    ];

    [Test]
    public async Task BuiltInConverters_AreEachPlacedAsRefusingOrAccepting()
    {
        // Arrange
        var placed = Refusing.Keys.Concat(Accepting).Order(StringComparer.Ordinal);

        // Act
        var builtIn = typeof(StringConverter).Assembly.GetExportedTypes()
            .Where(static type => type.IsValueType && type.Namespace == typeof(StringConverter).Namespace && type.GetInterfaces().Any(IsConverterInterface))
            .Select(static type => type.Name.Split('`')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', builtIn)).IsEqualTo(string.Join(' ', placed));
    }

    [Test]
    public async Task RefusingConverters_OfWriteEmitter_AreTheBuiltInsThatRefuseAValue()
    {
        // Arrange
        var refusing = Refusing.Values.SelectMany(static names => names).Order(StringComparer.Ordinal);

        // Act
        var list = WriteEmitter.RefusingConverters.Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', refusing));
    }

    private static bool IsConverterInterface(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IDynamoDbConverter<>) || type.GetGenericTypeDefinition() == typeof(IDynamoDbSetElementConverter<>));
}
