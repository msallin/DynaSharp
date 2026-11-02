using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary><c>[assembly: DynamoDbDefaultConverter]</c>: where it stands in the order of converters, how far it reaches, what it does in a key, and what it reports. Each source is a whole file, because an assembly attribute goes before the namespace.</summary>
public sealed class DefaultConverterTests
{
    private const string Serialization = "global::DynaSharp.Serialization.";

    private const string Stored = "global::Shop.StoredTimestampConverter";

    private const string Timestamps = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.StoredTimestampConverter))]";

    private const string Converters = """

        public readonly struct StoredTimestampConverter : IDynamoDbConverter<DateTimeOffset>
        {
            public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

            public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct OtherTimestampConverter : IDynamoDbConverter<DateTimeOffset>
        {
            public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

            public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct MoneyConverter : IDynamoDbConverter<Money>
        {
            public static void Write(DynamoDbWriter writer, Money value) => writer.WriteNull();

            public static Money Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct AddableMoneyConverter : IDynamoDbConverter<Money>, IAddableConverter
        {
            public static void Write(DynamoDbWriter writer, Money value) => writer.WriteNull();

            public static Money Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct OmittingConverter : IDynamoDbConverter<DateTimeOffset>
        {
            public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

            public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type) => default;

            public static bool IsOmitted(DateTimeOffset value) => value == default;
        }

        public readonly struct CountingConverter : IDynamoDbConverter<DateTimeOffset>
        {
            public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

            public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type) => default;

            public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => false;
        }

        public readonly struct ExplicitConverter : IDynamoDbConverter<DateTimeOffset>
        {
            static void IDynamoDbConverter<DateTimeOffset>.Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

            static DateTimeOffset IDynamoDbConverter<DateTimeOffset>.Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct TimestampElement : IDynamoDbConverter<DateTimeOffset>, IDynamoDbSetElementConverter<DateTimeOffset>
        {
            public static AttributeType SetType => AttributeType.StringSet;

            public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

            public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type) => default;

            public static void WriteElement(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteString("x");

            public static DateTimeOffset ReadElement(ref DynamoDbReader reader) => default;
        }

        public readonly struct SetStoredAsListConverter : IDynamoDbConverter<HashSet<string>>
        {
            public static void Write(DynamoDbWriter writer, HashSet<string> value) => writer.WriteNull();

            public static HashSet<string> Read(ref DynamoDbReader reader, AttributeType type) => [];
        }

        public readonly struct LongAsTextConverter : IDynamoDbConverter<long>
        {
            public static void Write(DynamoDbWriter writer, long value) => writer.WriteNull();

            public static long Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct NothingConverter : IDynamoDbConverter<Nothing>
        {
            public static void Write(DynamoDbWriter writer, Nothing value) => writer.WriteNull();

            public static Nothing Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct OptionalIntConverter : IDynamoDbConverter<int?>
        {
            public static void Write(DynamoDbWriter writer, int? value) => writer.WriteNull();

            public static int? Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public readonly struct OpenConverter<T> : IDynamoDbConverter<List<T>>
        {
            public static void Write(DynamoDbWriter writer, List<T> value) => writer.WriteNull();

            public static List<T> Read(ref DynamoDbReader reader, AttributeType type) => [];
        }

        public readonly record struct Money(decimal Amount);

        public enum Nothing
        {
        }
        """;

    [Test]
    public async Task Build_PropertyOfTheType_TakesTheDefault()
    {
        // Arrange
        var source = File(Timestamps, "[DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.WriteConverter).IsEqualTo(Stored);
    }

    [Test]
    public async Task Build_ConverterOnTheProperty_WinsOverTheDefault()
    {
        // Arrange
        var source = File(Timestamps, "[DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(OtherTimestampConverter))] public DateTimeOffset At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.WriteConverter).IsEqualTo("global::Shop.OtherTimestampConverter");
    }

    [Test]
    public async Task Build_ConverterOnTheType_WinsOverTheDefault()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(Shop.Badge), typeof(Shop.BadgeTwo))]";
        var source = File(Defaults, """
            [DynamoDbDocument] public partial class A { public Badge? B { get; set; } }

            [DynamoDbConverter(typeof(BadgeOne))]
            public sealed record Badge(string Text);

            public readonly struct BadgeOne : IDynamoDbConverter<Badge>
            {
                public static void Write(DynamoDbWriter writer, Badge value) => writer.WriteNull();

                public static Badge Read(ref DynamoDbReader reader, AttributeType type) => null!;
            }

            public readonly struct BadgeTwo : IDynamoDbConverter<Badge>
            {
                public static void Write(DynamoDbWriter writer, Badge value) => writer.WriteNull();

                public static Badge Read(ref DynamoDbReader reader, AttributeType type) => null!;
            }
            """);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("B").Converter!.WriteConverter).IsEqualTo("global::Shop.BadgeOne");
    }

    [Test]
    public async Task Build_OtherTypes_KeepTheirBuiltIns()
    {
        // Arrange
        var source = File(Timestamps, "[DynamoDbDocument] public partial class A { public DateTime At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.WriteConverter).IsEqualTo(Serialization + "DateTimeConverter");
    }

    [Test]
    public async Task Build_NullableForm_IsCoveredWithTheNullHandlingAroundIt()
    {
        // Arrange
        var source = File(Timestamps, "[DynamoDbDocument] public partial class A { public DateTimeOffset? At { get; set; } }");

        // Act
        var property = Analysis.ModelOfWholeFile(source, "global::Shop.A").Property("At");

        // Assert
        await Assert.That((property.Converter!.WriteConverter, property.Converter.ReadConverter, property.Write))
            .IsEqualTo((Stored, $"{Serialization}NullableConverter<global::System.DateTimeOffset, {Stored}>", WriteCondition.WhenNotNull));
    }

    [Test]
    [Arguments("List<DateTimeOffset>", "ListConverter<global::System.DateTimeOffset, global::Shop.StoredTimestampConverter>")]
    [Arguments("IReadOnlyList<DateTimeOffset>", "ReadOnlyListConverter<global::System.DateTimeOffset, global::Shop.StoredTimestampConverter>")]
    [Arguments("DateTimeOffset[]", "ArrayConverter<global::System.DateTimeOffset, global::Shop.StoredTimestampConverter>")]
    [Arguments("Dictionary<string, DateTimeOffset>", "DictionaryConverter<global::System.DateTimeOffset, global::Shop.StoredTimestampConverter>")]
    [Arguments("List<DateTimeOffset?>", "ListConverter<global::System.DateTimeOffset?, global::DynaSharp.Serialization.NullableConverter<global::System.DateTimeOffset, global::Shop.StoredTimestampConverter>>")]
    [Arguments("List<List<DateTimeOffset>>", "ListConverter<global::System.Collections.Generic.List<global::System.DateTimeOffset>, global::DynaSharp.Serialization.ListConverter<global::System.DateTimeOffset, global::Shop.StoredTimestampConverter>>")]
    public async Task Build_ElementOfACollection_TakesTheDefault(string type, string expected)
    {
        // Arrange
        var source = File(Timestamps, $$"""[DynamoDbDocument] public partial class A { public {{type}} X { get; set; } = default!; }""");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("X").Converter!.WriteConverter).IsEqualTo(Serialization + expected);
    }

    [Test]
    public async Task Build_NullableElementOfAReferenceType_KeepsTheWrapperOfNull()
    {
        // Arrange (a string? compares equal to a string, and the converter of a string takes no null)
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(string), typeof(Shop.TextConverter))]";
        var source = File(Defaults, """
            [DynamoDbDocument] public partial class A { public List<string?> X { get; set; } = []; }

            public readonly struct TextConverter : IDynamoDbConverter<string>
            {
                public static void Write(DynamoDbWriter writer, string value) => writer.WriteNull();

                public static string Read(ref DynamoDbReader reader, AttributeType type) => string.Empty;
            }
            """);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("X").Converter!.WriteConverter)
            .IsEqualTo($"{Serialization}ListConverter<string?, {Serialization}NullableReferenceConverter<string, global::Shop.TextConverter>>");
    }

    [Test]
    public async Task Build_DefaultOfACollectionType_ReplacesTheConverterOfTheCollection()
    {
        // Arrange (no empty set of its own, because nothing tells what this converter stores)
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(HashSet<string>), typeof(Shop.SetStoredAsListConverter))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public HashSet<string> X { get; set; } = []; }");

        // Act
        var property = Analysis.ModelOfWholeFile(source, "global::Shop.A").Property("X");

        // Assert
        await Assert.That((property.Converter!.WriteConverter, property.Converter.EmptySet, property.Write)).IsEqualTo(("global::Shop.SetStoredAsListConverter", (string?)null, WriteCondition.Always));
    }

    [Test]
    public async Task Build_ElementOfASet_TakesADefaultThatIsAnElementConverter()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.TimestampElement))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public HashSet<DateTimeOffset> X { get; set; } = []; }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("X").Converter!.WriteConverter).IsEqualTo($"{Serialization}HashSetConverter<global::System.DateTimeOffset, global::Shop.TimestampElement>");
    }

    [Test]
    public async Task Build_ElementOfASet_ReportsDyna003ForADefaultThatIsNoElementConverter()
    {
        // Arrange
        var source = File(Timestamps, "[DynamoDbDocument] public partial class A { public HashSet<DateTimeOffset> [|X|] { get; set; } = []; }");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA003 Error: Property 'X' has unsupported type 'System.Collections.Generic.HashSet<System.DateTimeOffset>'. Add a [DynamoDbConverter] or use a supported type."));
    }

    [Test]
    public async Task Build_DefaultThatAnswersIsOmitted_IsAskedOnAWrite()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.OmittingConverter))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.OmissionForwarder).IsEqualTo($"{Serialization}NullableHandleConverter<global::System.DateTimeOffset, global::Shop.OmittingConverter>");
    }

    [Test]
    public async Task Build_DefaultThatCountsAValueAsMissing_ReadsAsAConverterOnThePropertyDoes()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.CountingConverter))]";
        var byDefault = File(Defaults, "[DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } public DateTimeOffset? MaybeAt { get; set; } }");
        var onTheProperty = File(string.Empty, """
            [DynamoDbDocument]
            public partial class A
            {
                [DynamoDbConverter(typeof(CountingConverter))]
                public DateTimeOffset At { get; set; }

                [DynamoDbConverter(typeof(CountingConverter))]
                public DateTimeOffset? MaybeAt { get; set; }
            }
            """);

        // Act
        var models = (Analysis.ModelOfWholeFile(byDefault, "global::Shop.A"), Analysis.ModelOfWholeFile(onTheProperty, "global::Shop.A"));

        // Assert
        await Assert.That((models.Item1.Property("At").Converter, models.Item1.Property("MaybeAt").Converter)).IsEqualTo((models.Item2.Property("At").Converter, models.Item2.Property("MaybeAt").Converter));
    }

    [Test]
    public async Task Build_DefaultThatCountsAValueAsMissing_IsAskedOnARead()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.CountingConverter))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.CountsAsMissing).IsTrue();
    }

    [Test]
    public async Task Build_DefaultWithoutAnAnswer_IsNotAskedOnARead()
    {
        // Arrange
        var source = File(Timestamps, "[DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.CountsAsMissing).IsFalse();
    }

    [Test]
    public async Task Build_ElementOfACollection_IsNotAskedOnARead()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.CountingConverter))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public List<DateTimeOffset> At { get; set; } = new(); }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.CountsAsMissing).IsFalse();
    }

    [Test]
    public async Task Build_DefaultThatImplementsTheContractExplicitly_GetsAForwarder()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.ExplicitConverter))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").Converter!.WriteForwarder).IsNotNull();
    }

    [Test]
    public async Task Build_InternalDefault_MakesTheHandleInternal()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.HiddenConverter))]";
        var source = File(Defaults, """
            [DynamoDbDocument] public partial class A { public DateTimeOffset At { get; set; } }

            internal readonly struct HiddenConverter : IDynamoDbConverter<DateTimeOffset>
            {
                public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNull();

                public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
            """);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("At").IsPublic).IsFalse();
    }

    [Test]
    public async Task Build_PlainKeyOfATypeAKeyCannotHold_IsNoErrorWithADefault()
    {
        // Arrange (what a converter on the property does, so DYNA017 is what it would be there)
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(Shop.Money), typeof(Shop.MoneyConverter))]";
        var source = File(Defaults, """[DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey] public Money Id { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_PlainKeyOfATypeWithoutAConverter_ReportsItAndGoesOn()
    {
        // Arrange (a plain key with nothing to read it, which has no answer to take back)
        var source = File(Timestamps, """[DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey] public object Id { get; set; } = "x"; }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).Contains("Property 'Id' has unsupported type 'object'");
    }

    [Test]
    public async Task Build_PlainKeyOfATypeAKeyCannotHold_ReportsDyna017WithoutADefault()
    {
        // Arrange
        var source = File(Timestamps, """[DynamoDbEntity, Discriminator("E")] public partial class E { [[|PartitionKey|]] public bool Id { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA017 Error: Property 'Id' of type 'bool' cannot be a DynamoDB key, which must be a string, number or binary"));
    }

    [Test]
    [Arguments("MoneyConverter", nameof(KeyWireType.Unknown))]
    [Arguments("AddableMoneyConverter", nameof(KeyWireType.Number))]
    public async Task Build_PlainKey_SaysHowItIsStoredAsAConverterOnThePropertyDoes(string converter, string expected)
    {
        // Arrange
        var defaults = $"[assembly: DynamoDbDefaultConverter(typeof(Shop.Money), typeof(Shop.{converter}))]";
        var source = File(defaults, """[DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey] public Money Id { get; set; } }""");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.E");

        // Assert
        await Assert.That(model.Entity!.PartitionKey.WireType.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_InputOfATemplate_KeepsTheFormatOfTheTemplate()
    {
        // Arrange (a placeholder has formats of its own, with or without a converter on the property, and its attribute goes through the default)
        var source = File(Timestamps, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{At:yyyy-MM-dd}")]
            public partial class E { public DateTimeOffset At { get; set; } }
            """);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.E");

        // Assert
        await Assert.That((model.Entity!.PartitionKey.Template!.Text, model.Property("At").Converter!.WriteConverter)).IsEqualTo(("E#{At:yyyy-MM-dd}", Stored));
    }

    [Test]
    public async Task Build_InputOfATemplateThatNoFormatFits_ReportsDyna103AsWithAConverterOnTheProperty()
    {
        // Arrange
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(Shop.Money), typeof(Shop.MoneyConverter))]";
        var source = File(Defaults, """[DynamoDbEntity, Discriminator("E"), PartitionKey([|"E#{Id}"|])] public partial class E { public Money Id { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA103 Error: Property 'Id' of type 'Shop.Money' cannot be used in a key"));
    }

    [Test]
    public async Task Build_Version_KeepsTheBuiltInConverter()
    {
        // Arrange (an update adds to it, which only a number can take, and a project with a default for long still needs a version)
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(long), typeof(Shop.LongAsTextConverter))]";
        var source = File(Defaults, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")]
            public partial class E { public required string Id { get; set; } [Version] public long Revision { get; set; } public long Count { get; set; } }
            """);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.E");

        // Assert
        await Assert.That((model.Property("Revision").Converter!.WriteConverter, model.Property("Count").Converter!.WriteConverter)).IsEqualTo(($"{Serialization}NumberConverter<long>", "global::Shop.LongAsTextConverter"));
    }

    [Test]
    public async Task Build_EnumWithoutMembers_IsNoErrorWithADefault()
    {
        // Arrange (DYNA024 says that a write throws, which is not so with a converter)
        const string Defaults = "[assembly: DynamoDbDefaultConverter(typeof(Shop.Nothing), typeof(Shop.NothingConverter))]";
        var source = File(Defaults, "[DynamoDbDocument] public partial class A { public Nothing X { get; set; } }");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_ConverterOfAnotherType_ReportsDyna008OnTheAttribute()
    {
        // Arrange
        var source = File("[assembly: [|DynamoDbDefaultConverter(typeof(Guid), typeof(Shop.StoredTimestampConverter))|]]", string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA008 Error: Converter 'Shop.StoredTimestampConverter' does not implement IDynamoDbConverter<System.Guid> or has no accessible definition"));
    }

    [Test]
    public async Task Build_ConverterOfAnotherType_ReportsOnceWhateverUsesIt()
    {
        // Arrange (it belongs to the assembly, and neither of these two types is to blame)
        var source = File(
            "[assembly: [|DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.MoneyConverter))|]]",
            """
            [DynamoDbDocument] public partial class A { public DateTimeOffset X { get; set; } }

            [DynamoDbDocument] public partial class B { public DateTimeOffset X { get; set; } }
            """);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA008 Error: Converter 'Shop.MoneyConverter' does not implement IDynamoDbConverter<System.DateTimeOffset> or has no accessible definition"));
    }

    [Test]
    public async Task Build_NoConverterAtAll_ReportsDyna008()
    {
        // Arrange
        var source = File("[assembly: [|DynamoDbDefaultConverter(typeof(Guid), null!)|]]", string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA008 Error: Converter 'null' does not implement IDynamoDbConverter<System.Guid> or has no accessible definition"));
    }

    [Test]
    public async Task Build_TwoDefaultsForOneType_ReportsDyna029OnTheSecond()
    {
        // Arrange
        var source = File(
            """
            [assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.StoredTimestampConverter))]
            [assembly: [|DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.OtherTimestampConverter))|]]
            """,
            string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA029 Error: [DynamoDbDefaultConverter] for 'System.DateTimeOffset' is not valid: another default names the same type"));
    }

    [Test]
    public async Task Build_NullableType_ReportsDyna029()
    {
        // Arrange (no property could match it, because a property goes by the type without its ?)
        var source = File("[assembly: [|DynamoDbDefaultConverter(typeof(int?), typeof(Shop.OptionalIntConverter))|]]", string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA029 Error: [DynamoDbDefaultConverter] for 'int?' is not valid: name the type without its ?, which covers the nullable form too"));
    }

    [Test]
    public async Task Build_OpenGenericType_ReportsDyna029()
    {
        // Arrange
        var source = File("[assembly: [|DynamoDbDefaultConverter(typeof(List<>), typeof(Shop.OpenConverter<>))|]]", string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA029 Error: [DynamoDbDefaultConverter] for 'System.Collections.Generic.List<>' is not valid: a default is for one closed type"));
    }

    [Test]
    public async Task Build_NoTypeAtAll_ReportsDyna029()
    {
        // Arrange
        var source = File("[assembly: [|DynamoDbDefaultConverter(null!, typeof(Shop.StoredTimestampConverter))|]]", string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA029 Error: [DynamoDbDefaultConverter] for 'null' is not valid: it names no type"));
    }

    [Test]
    [Arguments("[assembly: DynamoDbDefaultConverter(typeof(Guid))]")]
    [Arguments("[assembly: DynamoDbDefaultConverter(typeof(Missing), typeof(Shop.StoredTimestampConverter))]")]
    public async Task Build_AttributeThatDoesNotBind_LeavesItToTheCompiler(string defaults)
    {
        // Arrange
        var source = File(defaults, string.Empty);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_DefaultThatIsWrong_LeavesTheBuiltInSoNoErrorFollowsFromIt()
    {
        // Arrange
        var source = File("[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(Shop.MoneyConverter))]", "[DynamoDbDocument] public partial class A { public DateTimeOffset X { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("X").Converter!.WriteConverter).IsEqualTo(Serialization + "DateTimeOffsetConverter");
    }

    private static string File(string defaults, string members) => $$"""
        using System;
        using System.Collections.Generic;
        using DynaSharp;
        using DynaSharp.Mapping;
        using DynaSharp.Serialization;

        {{defaults}}

        namespace Shop;

        {{members}}
        {{Converters}}
        """;
}
