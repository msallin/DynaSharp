using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>Converter selection, through the real generator: a property type in, converters out.</summary>
public sealed class ConverterResolverTests
{
    private const string Ser = "global::DynaSharp.Serialization.";

    private const string Types = """
        public enum Color { Red }
        [DynamoDbDocument] public partial record Doc(string Name);
        [DynamoDbEntity, Discriminator("O"), PartitionKey("O#{Id}")] public partial record Other(string Id);
        [DynamoDbConverter(typeof(MoneyConverter))] public readonly struct Money { }
        public readonly struct MoneyConverter : IDynamoDbConverter<Money>
        {
            public static void Write(DynamoDbWriter writer, Money value) { }
            public static Money Read(ref DynamoDbReader reader, AttributeType type) => default;
        }
        public sealed class Ready : DynaSharp.Mapping.IDynamoDbDocument<Ready> { }
        """;

    [Test]
    [Arguments("string", "StringConverter")]
    [Arguments("bool", "BoolConverter")]
    [Arguments("Guid", "GuidConverter")]
    [Arguments("DateTimeOffset", "DateTimeOffsetConverter")]
    [Arguments("DateTime", "DateTimeConverter")]
    [Arguments("DateOnly", "DateOnlyConverter")]
    [Arguments("TimeOnly", "TimeOnlyConverter")]
    [Arguments("TimeSpan", "TimeSpanConverter")]
    [Arguments("byte[]", "BinaryConverter")]
    [Arguments("ReadOnlyMemory<byte>", "ReadOnlyMemoryConverter")]
    [Arguments("AttributeValue", "AttributeValueConverter")]
    [Arguments("int", "NumberConverter<int>")]
    [Arguments("sbyte", "NumberConverter<sbyte>")]
    [Arguments("byte", "NumberConverter<byte>")]
    [Arguments("short", "NumberConverter<short>")]
    [Arguments("ushort", "NumberConverter<ushort>")]
    [Arguments("uint", "NumberConverter<uint>")]
    [Arguments("long", "NumberConverter<long>")]
    [Arguments("ulong", "NumberConverter<ulong>")]
    [Arguments("float", "NumberConverter<float>")]
    [Arguments("double", "NumberConverter<double>")]
    [Arguments("decimal", "NumberConverter<decimal>")]
    [Arguments("Half", "NumberConverter<global::System.Half>")]
    [Arguments("Int128", "NumberConverter<global::System.Int128>")]
    [Arguments("UInt128", "NumberConverter<global::System.UInt128>")]
    [Arguments("System.Numerics.BigInteger", "NumberConverter<global::System.Numerics.BigInteger>")]
    public async Task Resolve_Scalar_IsTheBuiltIn(string type, string converter)
    {
        // Arrange, Act
        var model = Property(type);

        // Assert
        await Assert.That((model.Converter!.WriteConverter, model.Converter.ReadConverter, model.Write))
            .IsEqualTo((Ser + converter, Ser + converter, WriteCondition.Always));
    }

    [Test]
    [Arguments("int?", "NumberConverter<int>", "NullableConverter<int, global::DynaSharp.Serialization.NumberConverter<int>>")]
    [Arguments("string?", "StringConverter", "NullableReferenceConverter<string, global::DynaSharp.Serialization.StringConverter>")]
    [Arguments("Doc?", "DocumentConverter<global::Shop.Doc>", "NullableReferenceConverter<global::Shop.Doc, global::DynaSharp.Serialization.DocumentConverter<global::Shop.Doc>>")]
    public async Task Resolve_Nullable_WrapsTheReadConverterOnly(string type, string write, string read)
    {
        // Arrange, Act
        var model = Property(type);

        // Assert
        await Assert.That((model.Converter!.WriteConverter, model.Converter.ReadConverter, model.Write))
            .IsEqualTo((Ser + write, Ser + read, WriteCondition.WhenNotNull));
    }

    [Test]
    [Arguments("int[]", "ArrayConverter<int, global::DynaSharp.Serialization.NumberConverter<int>>")]
    [Arguments("List<string>", "ListConverter<string, global::DynaSharp.Serialization.StringConverter>")]
    [Arguments("IReadOnlyList<Doc>", "ReadOnlyListConverter<global::Shop.Doc, global::DynaSharp.Serialization.DocumentConverter<global::Shop.Doc>>")]
    [Arguments("IList<int>", "MutableListConverter<int, global::DynaSharp.Serialization.NumberConverter<int>>")]
    [Arguments("Dictionary<string, int>", "DictionaryConverter<int, global::DynaSharp.Serialization.NumberConverter<int>>")]
    [Arguments("IReadOnlyDictionary<string, string>", "ReadOnlyDictionaryConverter<string, global::DynaSharp.Serialization.StringConverter>")]
    [Arguments("List<List<int>>", "ListConverter<global::System.Collections.Generic.List<int>, global::DynaSharp.Serialization.ListConverter<int, global::DynaSharp.Serialization.NumberConverter<int>>>")]
    [Arguments("List<int?>", "ListConverter<int?, global::DynaSharp.Serialization.NullableConverter<int, global::DynaSharp.Serialization.NumberConverter<int>>>")]
    [Arguments("List<string?>", "ListConverter<string?, global::DynaSharp.Serialization.NullableReferenceConverter<string, global::DynaSharp.Serialization.StringConverter>>")]
    [Arguments("List<Other>", "ListConverter<global::Shop.Other, global::DynaSharp.Serialization.DocumentConverter<global::Shop.Other>>")]
    public async Task Resolve_Collection_RecursesOnTheElement(string type, string converter)
    {
        // Arrange, Act
        var model = Property(type);

        // Assert
        await Assert.That((model.Converter!.WriteConverter, model.Write, model.Converter.EmptySet)).IsEqualTo((Ser + converter, WriteCondition.Always, (string?)null));
    }

    [Test]
    [Arguments("HashSet<string>", "HashSetConverter<string, global::DynaSharp.Serialization.StringSetElement>", "string")]
    [Arguments("HashSet<int>", "HashSetConverter<int, global::DynaSharp.Serialization.NumberSetElement<int>>", "int")]
    [Arguments("HashSet<byte[]>", "HashSetConverter<byte[], global::DynaSharp.Serialization.BinarySetElement>", "byte[]")]
    [Arguments("HashSet<Guid>", "HashSetConverter<global::System.Guid, global::DynaSharp.Serialization.GuidSetElement>", "global::System.Guid")]
    [Arguments("IReadOnlySet<string>", "ReadOnlySetConverter<string, global::DynaSharp.Serialization.StringSetElement>", "string")]
    public async Task Resolve_Set_IsLeftOutWhenEmptyAndMissingMeansEmpty(string type, string converter, string element)
    {
        // Arrange, Act
        var model = Property(type);

        // Assert
        await Assert.That((model.Converter!.WriteConverter, model.Write, model.Missing, model.Converter.EmptySet)).IsEqualTo((
            Ser + converter,
            WriteCondition.WhenNotEmptyCount,
            MissingBehavior.EmptySet,
            (string?)$"new global::System.Collections.Generic.HashSet<{element}>()"));
    }

    [Test]
    public async Task Resolve_SetOfEnums_UsesTheGeneratedConverterAsElement()
    {
        // Arrange, Act
        var model = Property("HashSet<Color>");

        // Assert
        await Assert.That(model.Converter!.WriteConverter).IsEqualTo(Ser + "HashSetConverter<global::Shop.Color, Shop_ColorConverter>");
    }

    [Test]
    public async Task Resolve_NullableSet_IsReadAsNullWhenMissing()
    {
        // Arrange, Act
        var model = Property("HashSet<string>?");

        // Assert
        await Assert.That((model.Write, model.Missing)).IsEqualTo((WriteCondition.WhenNotEmptyCount, MissingBehavior.KeepCurrent));
    }

    [Test]
    public async Task Resolve_Enum_IsTheGeneratedConverter()
    {
        // Arrange, Act
        var model = Property("Color");

        // Assert
        await Assert.That((model.Converter!.WriteConverter, model.Converter.ReadConverter)).IsEqualTo(("Shop_ColorConverter", "Shop_ColorConverter"));
    }

    [Test]
    public async Task Resolve_NullableEnum_WrapsTheGeneratedConverter()
    {
        // Arrange, Act
        var model = Property("Color?");

        // Assert
        await Assert.That(model.Converter!.ReadConverter).IsEqualTo(Ser + "NullableConverter<global::Shop.Color, Shop_ColorConverter>");
    }

    [Test]
    [Arguments("Doc", "global::Shop.Doc")]
    [Arguments("Other", "global::Shop.Other")]
    [Arguments("Ready", "global::Shop.Ready")]
    public async Task Resolve_DocumentOrEntityOrInterface_IsTheDocumentConverter(string type, string fullName)
    {
        // Arrange, Act
        var model = Property(type);

        // Assert
        await Assert.That(model.Converter!.WriteConverter).IsEqualTo($"{Ser}DocumentConverter<{fullName}>");
    }

    [Test]
    public async Task Resolve_ConverterOnTheType_WinsEverywhereIncludingCollections()
    {
        // Arrange, Act
        var (value, list) = (Property("Money"), Property("List<Money>"));

        // Assert
        await Assert.That((value.Converter!.WriteConverter, list.Converter!.WriteConverter))
            .IsEqualTo(("global::Shop.MoneyConverter", Ser + "ListConverter<global::Shop.Money, global::Shop.MoneyConverter>"));
    }

    [Test]
    public async Task Resolve_ConverterOnANullableProperty_ImplementsTheInnerTypeAndGetsAWrapper()
    {
        // Arrange
        var source = $$"""
            {{Types}}
            [DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(MoneyConverter))] public Money? X { get; set; } }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.A").Properties[0];

        // Assert
        await Assert.That((model.Converter!.WriteConverter, model.Converter.ReadConverter))
            .IsEqualTo(("global::Shop.MoneyConverter", Ser + "NullableConverter<global::Shop.Money, global::Shop.MoneyConverter>"));
    }

    [Test]
    public async Task Resolve_ConverterOnThePropertyOfASetType_IsNotTreatedAsASet()
    {
        // Arrange
        const string Source = """
            public readonly struct Listed : IDynamoDbConverter<HashSet<string>>
            {
                public static void Write(DynamoDbWriter writer, HashSet<string> value) { }
                public static HashSet<string> Read(ref DynamoDbReader reader, AttributeType type) => [];
            }
            [DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(Listed))] public HashSet<string> X { get; set; } = []; }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.A").Properties[0];

        // Assert
        await Assert.That((model.Write, model.Missing, model.Converter!.EmptySet)).IsEqualTo((WriteCondition.Always, MissingBehavior.KeepCurrent, (string?)null));
    }

    [Test]
    public async Task Resolve_ArrayStoredAsASet_IsLeftOutWhenEmpty()
    {
        // Arrange
        var source = """
            [DynamoDbDocument] public partial class A
            {
                [DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))] public string[] X { get; set; } = ["a"];
            }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.A").Properties[0];

        // Assert
        await Assert.That((model.Write, model.Missing, model.Converter!.EmptySet))
            .IsEqualTo((WriteCondition.WhenNotEmptyLength, MissingBehavior.EmptySet, (string?)"global::System.Array.Empty<string>()"));
    }

    [Test]
    public async Task Resolve_SetOfACustomElement_UsesItsConverterWhenItIsASetElement()
    {
        // Arrange
        var source = """
            [DynamoDbConverter(typeof(CodeConverter))] public readonly struct Code { }
            public readonly struct CodeConverter : IDynamoDbConverter<Code>, IDynamoDbSetElementConverter<Code>
            {
                public static AttributeType SetType => AttributeType.StringSet;
                public static void Write(DynamoDbWriter writer, Code value) { }
                public static Code Read(ref DynamoDbReader reader, AttributeType type) => default;
                public static void WriteElement(DynamoDbWriter writer, Code value) { }
                public static Code ReadElement(ref DynamoDbReader reader) => default;
            }
            [DynamoDbDocument] public partial class A { public HashSet<Code> X { get; set; } = []; }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.A").Properties[0];

        // Assert
        await Assert.That(model.Converter!.WriteConverter).IsEqualTo(Ser + "HashSetConverter<global::Shop.Code, global::Shop.CodeConverter>");
    }

    [Test]
    public async Task Resolve_SetOfACustomElementWithoutSetSupport_ReportsDyna003()
    {
        // Arrange
        const string Source = """
            [DynamoDbConverter(typeof(CodeConverter))] public readonly struct Code { }
            public readonly struct CodeConverter : IDynamoDbConverter<Code>
            {
                public static void Write(DynamoDbWriter writer, Code value) { }
                public static Code Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            [DynamoDbDocument] public partial class A { public HashSet<Code> [|X|] { get; set; } = []; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA003 Error: Property 'X' has unsupported type 'System.Collections.Generic.HashSet<Shop.Code>'. Add a [DynamoDbConverter] or use a supported type."));
    }

    private static PropertyModel Property(string type)
    {
        var source = $$"""
            {{Types}}
            [DynamoDbDocument] public partial class A { public {{type}} X { get; set; } = default!; }
            """;
        return Analysis.Model(source, "global::Shop.A").Properties[0];
    }
}
