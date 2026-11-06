using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>Keys and indexes in the model: names, delimiters, sharing and sparse inputs.</summary>
public sealed class EntityKeyBuilderTests
{
    [Test]
    public async Task Build_TemplateKeys_HaveDefaultAttributesAndBuilders()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("A"), SortKey("B")] public partial class E { }""";

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That((entity.PartitionKey.AttributeName, entity.PartitionKey.BuilderName, entity.SortKey!.AttributeName, entity.SortKey.BuilderName))
            .IsEqualTo(("pk", "Pk", "sk", "Sk"));
    }

    [Test]
    public async Task Build_TemplateKeys_TakeAttributeNames()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("A", AttributeName = "p"), SortKey("B", AttributeName = "s")] public partial class E { }""";

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That((entity.PartitionKey.AttributeName, entity.SortKey!.AttributeName)).IsEqualTo(("p", "s"));
    }

    [Test]
    public async Task Build_PlainKeys_AreThePropertiesOwnAttributes()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")] public partial class E
            {
                [PartitionKey, DynamoDbAttribute("id")] public string Id { get; set; } = "";
                [SortKey(AttributeName = "at")] public long At { get; set; }
            }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.E");
        var entity = model.Entity!;

        // Assert
        await Assert.That((entity.PartitionKey, entity.SortKey!.PlainProperty, entity.SortKey.AttributeName, model.Properties[1].AttributeName)).IsEqualTo((
            new KeyModel("id", "Pk", null, "Id", SharesAttribute: false),
            (string?)"At",
            "at",
            "at"));
    }

    [Test]
    public async Task Build_TemplateKey_IsAString()
    {
        // Arrange (an integer in a template is text)
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("{Id:d4}")] public partial class E { public int Id { get; set; } }""";

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That(entity.PartitionKey.WireType).IsEqualTo(KeyWireType.String);
    }

    [Test]
    [Arguments("public string V { get; set; } = \"\";", nameof(KeyWireType.String))]
    [Arguments("public Guid V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public DateTimeOffset V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public DateTime V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public DateOnly V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public TimeOnly V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public TimeSpan V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public byte V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("public int V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("public long V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("public decimal V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("public double V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("public byte[] V { get; set; } = [];", nameof(KeyWireType.Binary))]
    [Arguments("public ReadOnlyMemory<byte> V { get; set; }", nameof(KeyWireType.Binary))]
    [Arguments("public Color V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("public Modes V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("[DynamoDbConverter(typeof(EpochSecondsConverter))] public DateTimeOffset V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("[DynamoDbConverter(typeof(BoolAsNumberConverter))] public bool V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("[DynamoDbConverter(typeof(SdkDateTimeConverter))] public DateTime V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("[DynamoDbConverter(typeof(OffsetTimestampConverter))] public DateTimeOffset V { get; set; }", nameof(KeyWireType.String))]
    [Arguments("[DynamoDbConverter(typeof(NumberConverter<int>))] public int V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("[DynamoDbConverter(typeof(StringConverter))] public string V { get; set; } = \"\";", nameof(KeyWireType.String))]
    [Arguments("[DynamoDbConverter(typeof(BinaryConverter))] public byte[] V { get; set; } = [];", nameof(KeyWireType.Binary))]
    [Arguments("[DynamoDbConverter(typeof(SaysNothing))] public bool V { get; set; }", nameof(KeyWireType.Unknown))]
    [Arguments("[DynamoDbConverter(typeof(SaysNumber))] public bool V { get; set; }", nameof(KeyWireType.Number))]
    [Arguments("public Money V { get; set; }", nameof(KeyWireType.Unknown))]
    public async Task Build_PlainKey_HasTheWireTypeOfWhatItsConverterWrites(string member, string expected)
    {
        // Arrange
        var source = $$"""
            public enum Color { Red }
            [Flags] public enum Modes { None = 0, Fast = 1 }
            public readonly struct SaysNothing : IDynamoDbConverter<bool>
            {
                public static void Write(DynamoDbWriter writer, bool value) { }
                public static bool Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            public readonly struct SaysNumber : IDynamoDbConverter<bool>, IAddableConverter
            {
                public static void Write(DynamoDbWriter writer, bool value) { }
                public static bool Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            [DynamoDbConverter(typeof(MoneyText))] public readonly struct Money { }
            public readonly struct MoneyText : IDynamoDbConverter<Money>
            {
                public static void Write(DynamoDbWriter writer, Money value) { }
                public static Money Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{V}", AttributeName = "V")]
            public partial class E { {{member}} }
            """;

        // Act
        var entity = Analysis.Model(source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That(entity.PartitionKey.WireType.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_NullablePlainKeyOfASparseIndex_HasTheWireTypeOfTheValue()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("ByRank", PartitionKey = "{Rank}", PartitionKeyAttributeName = "Rank", Sparse = true)]
            public partial class E { public int? Rank { get; set; } }
            """;

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That(entity.Indexes.Single().PartitionKey.WireType).IsEqualTo(KeyWireType.Number);
    }

    [Test]
    public async Task Build_ConverterAttributeWithoutItsArgument_LeavesTheCompilerToReportIt()
    {
        // Arrange (the source does not compile, and the generator sees it on every keystroke)
        const string Source = """[DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey, DynamoDbConverter] public int V { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).DoesNotContain("CS8785");
    }

    [Test]
    [Arguments("string", "public string V { get; set; } = \"v\";")]
    [Arguments("enum", "public Color V { get; set; }")]
    [Arguments("byte[]", "public byte[] V { get; set; } = [1];")]
    [Arguments("memory", "public ReadOnlyMemory<byte> V { get; set; }")]
    [Arguments("decimal", "public decimal V { get; set; }")]
    [Arguments("Guid", "public Guid V { get; set; }")]
    [Arguments("custom on property", "[DynamoDbConverter(typeof(BoolText))] public bool V { get; set; }")]
    [Arguments("custom on type", "public Money V { get; set; }")]
    public async Task Build_PlainKey_AcceptsEveryWireTypeThatCanBeAKey(string kind, string member)
    {
        // Arrange
        _ = kind;
        var source = $$"""
            public enum Color { Red }
            public readonly struct BoolText : IDynamoDbConverter<bool>
            {
                public static void Write(DynamoDbWriter writer, bool value) { }
                public static bool Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            [DynamoDbConverter(typeof(MoneyText))] public readonly struct Money { }
            public readonly struct MoneyText : IDynamoDbConverter<Money>
            {
                public static void Write(DynamoDbWriter writer, Money value) { }
                public static Money Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            [DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey] {{member}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_ComputedFrom_GivesTheModelThePropertiesAndTheirAttributes()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "B#{Computed}")]
            public partial class E
            {
                public string Id { get; set; } = "";
                [DynamoDbAttribute("n")] public string Name { get; set; } = "";
                [ComputedFrom(nameof(Id), nameof(Name))] public string Computed => Id + Name;
            }
            """;

        // Act
        var computed = Analysis.Model(Source, "global::Shop.E").Entity!.ComputedInputs[0];

        // Assert
        await Assert.That((computed.Property, string.Join(",", computed.Dependencies), string.Join(",", computed.DependencyAttributes))).IsEqualTo(("Computed", "Id,Name", "Id,n"));
    }

    [Test]
    public async Task Build_ComputedPlainKey_IsWrittenButNotRead()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")] public partial class E
            {
                public string Id { get; set; } = "";
                [PartitionKey, DynamoDbAttribute("pk")] public string Key => "USER#" + Id;
            }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.E");

        // Assert
        await Assert.That((model.Entity!.PartitionKey.PlainProperty, model.Properties[1].IsWriteOnly)).IsEqualTo(((string?)"Key", true));
    }

    [Test]
    [Arguments("GSI1", "Gsi1", "gsi1")]
    [Arguments("StatusIndex", "StatusIndex", "statusindex")]
    [Arguments("orders-by_date.v2", "OrdersByDateV2", "orders-by_date.v2")]
    [Arguments("2024", "Index2024", "2024")]
    [Arguments("---", "Index", "---")]
    public async Task Build_IndexName_GivesBuildersAndDefaultAttributes(string name, string builder, string attribute)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), GlobalIndexKey("{{name}}", PartitionKey = "B", SortKey = "C")]
            public partial class E { }
            """;

        // Act
        var index = Analysis.Model(source, "global::Shop.E").Entity!.Indexes[0];

        // Assert
        await Assert.That((index.PartitionKey.BuilderName, index.SortKey!.BuilderName, index.PartitionKey.AttributeName, index.SortKey.AttributeName))
            .IsEqualTo(($"{builder}Pk", $"{builder}Sk", $"{attribute}pk", $"{attribute}sk"));
    }

    [Test]
    public async Task Build_IndexAttributeNames_AreTaken()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), SortKey("D"), GlobalIndexKey("GSI1", PartitionKey = "B", PartitionKeyAttributeName = "x", SortKey = "C", SortKeyAttributeName = "y"), LocalIndexKey("LSI1", "E", SortKeyAttributeName = "z")]
            public partial class E { }
            """;

        // Act
        var indexes = Analysis.Model(Source, "global::Shop.E").Entity!.Indexes;

        // Assert
        await Assert.That((indexes[0].PartitionKey.AttributeName, indexes[0].SortKey!.AttributeName, indexes[1].SortKey!.AttributeName)).IsEqualTo(("x", "y", "z"));
    }

    [Test]
    public async Task Build_LocalIndex_SharesThePartitionKeyOfTheTable()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), SortKey("D"), LocalIndexKey("LSI1", "E")]
            public partial class E { }
            """;

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;
        var index = entity.Indexes.Single();

        // Assert
        await Assert.That((index.IsLocal, index.PartitionKey == entity.PartitionKey, index.SortKey!.AttributeName, index.SortKey.BuilderName)).IsEqualTo((true, true, "lsi1sk", "Lsi1Sk"));
    }

    [Test]
    public async Task Build_BarePlaceholderOverTheOwnAttribute_IsAPlainKeyOverThatProperty()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), GlobalIndexKey("GSI1", PartitionKey = "{Status}", PartitionKeyAttributeName = "Status", SortKey = "{Total}", SortKeyAttributeName = "t")]
            public partial class E { public string Status { get; set; } = ""; [DynamoDbAttribute("t")] public decimal Total { get; set; } }
            """;

        // Act
        var index = Analysis.Model(Source, "global::Shop.E").Entity!.Indexes[0];

        // Assert
        await Assert.That((index.PartitionKey.PlainProperty, index.PartitionKey.Template, index.SortKey!.PlainProperty, index.SortKey.AttributeName))
            .IsEqualTo(((string?)"Status", (KeyTemplate?)null, (string?)"Total", "t"));
    }

    [Test]
    public async Task Build_BarePlaceholderWithAFormat_StaysATemplateAndCollides()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), GlobalIndexKey("GSI1", PartitionKey = [|"{Total:n4.2}"|], PartitionKeyAttributeName = "Total")]
            public partial class E { public decimal Total { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA004 Error: Attribute name 'Total' is used by both 'Total' and 'GSI1.PartitionKey'"));
    }

    [Test]
    public async Task Build_KeyOnlyOverItsOwnAttributeName_IsATemplate()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{Id}", AttributeName = "Id")]
            public partial class E { [KeyOnly] public string Id { get; set; } = ""; }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.E");

        // Assert
        await Assert.That((model.Entity!.PartitionKey.Template!.Text, model.Properties[0].RestoredFromKey)).IsEqualTo(("{Id}", (string?)"Id"));
    }

    [Test]
    public async Task Build_InvertedIndex_SharesTheAttributesOfTheTableKeys()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("USER#{User}"), SortKey("ORDER#{Order}")]
            [GlobalIndexKey("Inverted", PartitionKey = "ORDER#{Order}", PartitionKeyAttributeName = "sk", SortKey = "USER#{User}", SortKeyAttributeName = "pk")]
            public partial class E { public string User { get; set; } = ""; public string Order { get; set; } = ""; }
            """;

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That((entity.PartitionKey.SharesAttribute, entity.SortKey!.SharesAttribute, entity.Indexes[0].PartitionKey.SharesAttribute, entity.Indexes[0].SortKey!.SharesAttribute))
            .IsEqualTo((false, false, true, true));
    }

    [Test]
    public async Task Build_SparseIndex_ListsEachNullableInputOnce()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), SortKey("B"), GlobalIndexKey("GSI1", PartitionKey = "X#{Note}#{Id}", SortKey = "Y#{Note}#{Other}", Sparse = true)]
            public partial class E { public string Id { get; set; } = ""; public string? Note { get; set; } public string? Other { get; set; } }
            """;

        // Act
        var index = Analysis.Model(Source, "global::Shop.E").Entity!.Indexes[0];

        // Assert
        await Assert.That((string.Join(" ", index.NullableInputs), index.Sparse)).IsEqualTo(("Note Other", true));
    }

    [Test]
    public async Task Build_SparseIndexOverAPlainProperty_ListsThatProperty()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), GlobalIndexKey("GSI1", PartitionKey = "{Note}", PartitionKeyAttributeName = "Note", SortKey = "{Id}", SortKeyAttributeName = "Id", Sparse = true)]
            public partial class E { public string Id { get; set; } = ""; public string? Note { get; set; } }
            """;

        // Act
        var index = Analysis.Model(Source, "global::Shop.E").Entity!.Indexes[0];

        // Assert
        await Assert.That(string.Join(" ", index.NullableInputs)).IsEqualTo("Note");
    }

    [Test]
    public async Task Build_PlaceholderOverANullableValueType_HasTheInnerTypeAsParameter()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), GlobalIndexKey("GSI1", PartitionKey = "X#{Tracking}", Sparse = true)]
            public partial class E { public Guid? Tracking { get; set; } }
            """;

        // Act
        var segment = (PlaceholderSegment)Analysis.Model(Source, "global::Shop.E").Entity!.Indexes[0].PartitionKey.Template!.Segments[1];

        // Assert
        await Assert.That((segment.ValueType, segment.ParameterName, segment.InputCanBeAbsent, segment.Property)).IsEqualTo(("global::System.Guid", "tracking", true, "Tracking"));
    }

    [Test]
    [Arguments("USER#{Id}", "#")]
    [Arguments("USER#{Id}#ORDER", "#")]
    [Arguments("{Id}", "")]
    [Arguments("PROFILE", "")]
    [Arguments("A-{Id}#{Name}.csv", "-#.")]
    [Arguments("{Id}x{Name}", "x")]
    [Arguments("A#{Id}", "#")]
    [Arguments("{Id}#B", "#")]
    [Arguments("ORDER-{Unique}", "-")]
    [Arguments("{{{Id}}}", "{}")]
    [Arguments("#{Unique}#{Id}", "#")]
    public async Task Build_Template_ComputesTheDelimiters(string template, string delimiters)
    {
        // Arrange, Act
        var key = PartitionKey(template);

        // Assert
        await Assert.That(key.Delimiters).IsEqualTo(delimiters);
    }

    [Test]
    [Arguments("PROFILE", 7)]
    [Arguments("ORDER#{At}#{Unique}", 71)]
    [Arguments("ORDER#{At:ms}#{Unique}", 67)]
    [Arguments("{Unique}", 36)]
    [Arguments("{Id}", 256)]
    [Arguments("A#{Unique}#{Id}", 256)]
    public async Task Build_Template_ComputesTheBufferLength(string template, int length)
    {
        // Arrange, Act
        var key = PartitionKey(template);

        // Assert
        await Assert.That(key.StackBufferLength).IsEqualTo(length);
    }

    [Test]
    public async Task Build_KeyOnly_IsRestoredFromTheFirstLosslessKey()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{At:yyyy}"), SortKey("{At:ticks}"), GlobalIndexKey("GSI1", PartitionKey = "{At:ticks}")]
            public partial class E { [KeyOnly] public DateTime At { get; set; } }
            """;

        // Act
        var property = Analysis.Model(Source, "global::Shop.E").Properties[0];

        // Assert
        await Assert.That((property.RestoredFromKey, property.Write, property.Converter, property.IsWriteOnly)).IsEqualTo(((string?)"sk", WriteCondition.Never, (ConverterModel?)null, false));
    }

    [Test]
    public async Task Build_StoredInput_FallsBackOnTheFirstLosslessKey()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{At:yyyy}"), SortKey("{At:ticks}"), GlobalIndexKey("GSI1", PartitionKey = "{At:ticks}")]
            public partial class E { public DateTime At { get; set; } }
            """;

        // Act
        var property = Analysis.Model(Source, "global::Shop.E").Properties[0];

        // Assert
        await Assert.That((property.FallbackKey, property.RestoredFromKey)).IsEqualTo(((string?)"sk", (string?)null));
    }

    [Test]
    [Arguments("""PartitionKey("A#{Id}"), SortKey("{At:yyyy}")""", """public DateTime At { get; set; } public string Id { get; set; } = "";""", "At")]
    [Arguments("""PartitionKey("A"), GlobalIndexKey("GSI1", PartitionKey = "A#{Id}", SortKey = "B", Sparse = true)""", """public string? Id { get; set; }""", "Id")]
    [Arguments("""PartitionKey("A#{Id}")""", """[DynamoDbAttribute("Id")] public string Id => "x";""", "Id")]
    [Arguments("""PartitionKey("A#{Id}")""", """[KeyOnly] public string Id { get; set; } = "";""", "Id")]
    [Arguments("""PartitionKey("A")""", """public string Id { get; set; } = "";""", "Id")]
    public async Task Build_NoKeyToFallBackOn_LeavesItOut(string keys, string members, string property)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), {{keys}}]
            public partial class E { {{members}} }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.E").Properties.Single(candidate => candidate.Name == property);

        // Assert
        await Assert.That(model.FallbackKey).IsNull();
    }

    [Test]
    public async Task Build_InputAsItsOwnAttribute_HasNoTemplateToParse()
    {
        // Arrange (a bare placeholder over the attribute of the property is a plain key)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{Id}", AttributeName = "Id")]
            public partial class E { public string Id { get; set; } = ""; }
            """;

        // Act
        var property = Analysis.Model(Source, "global::Shop.E").Properties[0];

        // Assert
        await Assert.That(property.FallbackKey).IsNull();
    }

    [Test]
    public async Task Build_KeyOnlyNextToAPlainKey_IsRestoredFromTheTemplate()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), SortKey("A#{Name}")]
            public partial class E { [PartitionKey] public string Id { get; set; } = ""; [KeyOnly] public string Name { get; set; } = ""; }
            """;

        // Act
        var property = Analysis.Model(Source, "global::Shop.E").Properties[1];

        // Assert
        await Assert.That(property.RestoredFromKey).IsEqualTo("sk");
    }

    [Test]
    public async Task Build_EnumOnlyInAKeyTemplate_NeedsAConverterAnyway()
    {
        // Arrange
        const string Source = """
            public enum Color { Red }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{V}")]
            public partial class E { [KeyOnly] public Color V { get; set; } }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.E");

        // Assert
        await Assert.That(model.Enums.Single().ConverterName).IsEqualTo("Shop_ColorConverter");
    }

    private static KeyTemplate PartitionKey(string template)
    {
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{{template}}")]
            public partial class E
            {
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
                public Guid Unique { get; set; }
                public DateTimeOffset At { get; set; }
            }
            """;
        return Analysis.Model(source, "global::Shop.E").Entity!.PartitionKey.Template!;
    }
}
