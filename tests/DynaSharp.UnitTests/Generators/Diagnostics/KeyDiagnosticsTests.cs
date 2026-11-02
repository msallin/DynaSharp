namespace DynaSharp.UnitTests.Generators.Diagnostics;

/// <summary>
/// DYNA1xx: the rules of the key grammar. Each test states id, severity, full message and, through the markers in the source, the span.
/// </summary>
public sealed class KeyDiagnosticsTests
{
    private const string RawNotLast = "DYNA118 Error: 'V' has the format raw, which reads to the end of the key, so nothing can follow it in the template";

    private const string ComputedInIndex = "DYNA114 Error: Computed property 'Computed' cannot be used in an index template, because an update cannot keep the index current. Add [ComputedFrom] naming the stored properties it reads, or use a stored property.";

    private const string Members = """
        public string Id { get; set; } = "id";
        public string Name { get; set; } = "name";
        public int Number { get; set; }
        public decimal Total { get; set; }
        public DateTimeOffset At { get; set; }
        public Guid Unique { get; set; }
        public Color Paint { get; set; }
        public string? Note { get; set; }
        public int? Count { get; set; }
        public string Computed => Id;
        """;

    private const string Enum = "public enum Color { Red }";

    [Test]
    [Arguments("USER#{Id}#{{literal}}")]
    [Arguments("{Number:plain}")]
    [Arguments("ORDER-{Unique}")]
    [Arguments("{Unique}{Id}")]
    [Arguments("{Number:d4}{Id}")]
    [Arguments("{At}#{Unique}")]
    [Arguments("{Id}#{Name}")]
    [Arguments("PROFILE")]
    public async Task Build_TemplateThatKeepsTheRules_ReportsNothing(string template)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{{template}}")]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_TemplateWithASyntaxError_ReportsDyna100OnTheArgument()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey([|"USER#{Id"|])] public partial class E { public string Id { get; set; } = ""; }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA100 Error: Key template 'USER#{Id' has a syntax error at position 8: the placeholder is not closed"));
    }

    [Test]
    [Arguments("""PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = [|"{"|])""")]
    [Arguments("""PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "A", SortKey = [|"{"|])""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", [|"{"|])""")]
    [Arguments("""PartitionKey([|"{"|]), SortKey("S"), LocalIndexKey("LSI1", "A")""")]
    public async Task Build_TemplateWithASyntaxErrorInAnIndex_ReportsDyna100AndNoFollowUps(string attributes)
    {
        // Arrange
        var source = $"[DynamoDbEntity, Discriminator(\"E\"), {attributes}] public partial class E {{ }}";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA100 Error: Key template '{' has a syntax error at position 1: the placeholder is not closed"));
    }

    [Test]
    public async Task Build_LocalIndexWithANullTemplate_ReportsDyna100()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", [|null!|])] public partial class E { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA100 Error: Key template '' has a syntax error at position 0: the template is empty"));
    }

    [Test]
    public async Task Build_EmptyTemplate_ReportsDyna100()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey([|""|])] public partial class E { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA100 Error: Key template '' has a syntax error at position 0: the template is empty"));
    }

    [Test]
    public async Task Build_LiteralThatIsNotAscii_ReportsDyna101OnTheArgument()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey([|"ÜSER"|])] public partial class E { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA101 Error: Key template literal must be printable ASCII"));
    }

    [Test]
    [Arguments("Missing", "Missing")]
    [Arguments("Ignored", "Ignored")]
    [Arguments("Shared", "Shared")]
    [Arguments("Hidden", "Hidden")]
    public async Task Build_PlaceholderWithoutAProperty_ReportsDyna102OnTheArgument(string name, string shown)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"A#{{{name}}}"|])]
            public partial class E
            {
                [DynamoDbIgnore] public string Ignored { get; set; } = "";
                public static string Shared { get; set; } = "";
                private string Hidden { get; set; } = "";
            }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA102 Error: Key template references unknown property '{shown}'"));
    }

    [Test]
    [Arguments("double", "double")]
    [Arguments("float", "float")]
    [Arguments("bool", "bool")]
    [Arguments("TimeSpan", "System.TimeSpan")]
    [Arguments("Half", "System.Half")]
    [Arguments("Int128", "System.Int128")]
    [Arguments("UInt128", "System.UInt128")]
    [Arguments("System.Numerics.BigInteger", "System.Numerics.BigInteger")]
    [Arguments("nint", "nint")]
    [Arguments("byte[]", "byte[]")]
    [Arguments("List<string>", "System.Collections.Generic.List<string>")]
    public async Task Build_TypeThatCannotBeInAKey_ReportsDyna103(string type, string shown)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"A#{V}"|])]
            public partial class E { public {{type}} V { get; set; } = default!; }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA103 Error: Property 'V' of type '{shown}' cannot be used in a key"));
    }

    [Test]
    [Arguments("string", "plain", "string")]
    [Arguments("string", "RAW", "string")]
    [Arguments("int", "raw", "int")]
    [Arguments("decimal", "raw", "decimal")]
    [Arguments("Guid", "raw", "System.Guid")]
    [Arguments("DateOnly", "raw", "System.DateOnly")]
    [Arguments("DateTimeOffset", "raw", "System.DateTimeOffset")]
    [Arguments("Color", "raw", "Shop.Color")]
    [Arguments("Guid", "d4", "System.Guid")]
    [Arguments("DateOnly", "yyyy-MM-dd", "System.DateOnly")]
    [Arguments("int", "x", "int")]
    [Arguments("int", "d", "int")]
    [Arguments("int", "d4x", "int")]
    [Arguments("int", "n", "int")]
    [Arguments("int", "n4.2", "int")]
    [Arguments("decimal", "d4", "decimal")]
    [Arguments("decimal", "n4", "decimal")]
    [Arguments("decimal", "n.2", "decimal")]
    [Arguments("decimal", "n4.", "decimal")]
    [Arguments("decimal", "x4.2", "decimal")]
    [Arguments("DateTimeOffset", "HH:mm", "System.DateTimeOffset")]
    [Arguments("DateTimeOffset", "yyyyMMddHH", "System.DateTimeOffset")]
    [Arguments("DateTime", "O", "System.DateTime")]
    [Arguments("Color", "plain", "Shop.Color")]
    [Arguments("Color", "n4", "Shop.Color")]
    [Arguments("Color", "dx", "Shop.Color")]
    public async Task Build_FormatThatDoesNotFit_ReportsDyna104(string type, string format, string shown)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"{V:{{format}}}"|])]
            public partial class E { public {{type}} V { get; set; } = default!; }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA104 Error: Format '{format}' does not fit type '{shown}'. It takes {FormatsOf(type)}."));
    }

    [Test]
    [Arguments("int", "d0")]
    [Arguments("int", "d21")]
    [Arguments("int", "d99999999999")]
    [Arguments("int", "n0")]
    [Arguments("int", "n29")]
    [Arguments("decimal", "n0.2")]
    [Arguments("decimal", "n20.9")]
    [Arguments("decimal", "n29.0")]
    [Arguments("decimal", "n1.99999999999")]
    [Arguments("Color", "d0")]
    [Arguments("Color", "d21")]
    public async Task Build_FormatOutOfRange_ReportsDyna105(string type, string format)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"{V:{{format}}}"|])]
            public partial class E { public {{type}} V { get; set; } }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA105 Error: Format '{format}' is out of range"));
    }

    [Test]
    [Arguments("{Id}{Name}", "Id", "Name")]
    [Arguments("A#{Id}{Name}#B", "Id", "Name")]
    [Arguments("{Id}{Unique}", "Id", "Unique")]
    [Arguments("{Number}{Id}", "Number", "Id")]
    public async Task Build_VariableWidthFollowedByAPlaceholder_ReportsDyna106(string template, string first, string second)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"{{template}}"|])]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA106 Error: '{first}' is variable-width and '{second}' follows with no literal between them, so the key cannot be parsed"));
    }

    [Test]
    [Arguments("""PartitionKey([|"A#{Note}"|])""", "Note")]
    [Arguments("""PartitionKey([|"A#{Count:d4}"|])""", "Count")]
    [Arguments("""PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = [|"A#{Note}"|])""", "Note")]
    [Arguments("""PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", [|"A#{Note}"|])""", "Note")]
    public async Task Build_NullableInputInAKeyThatIsNotSparse_ReportsDyna107(string attributes, string property)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), {{attributes}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA107 Error: Nullable '{property}' is used in a key that is not Sparse"));
    }

    [Test]
    public async Task Build_NullablePlainKey_ReportsDyna107()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [[|PartitionKey|]] public string? Id { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA107 Error: Nullable 'Id' is used in a key that is not Sparse"));
    }

    [Test]
    public async Task Build_GlobalAndLocalIndexWithOneConstant_ReportsDyna018()
    {
        // Arrange (their builders differ in Pk and Sk, the constant does not)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("by-name", PartitionKey = "A"), [|LocalIndexKey("by_name", "B")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA018 Error: Index 'by_name' is invalid: its constant 'ByName' in Indexes collides with another index"));
    }

    [Test]
    public async Task Build_LocalIndexesWithOneConstantAfterTheReservedNames_ReportsDyna018()
    {
        // Arrange (the name of a global index that had to change and the one that is that name)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("index-indexes", PartitionKey = "A"), [|GlobalIndexKey("indexes", PartitionKey = "B")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA018 Error: Index 'indexes' is invalid: its constant 'IndexIndexes' in Indexes collides with another index"));
    }

    [Test]
    public async Task Build_NullableInputInASparseIndex_ReportsNothing()
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("GSI1", PartitionKey = "A#{Note}", Sparse = true), LocalIndexKey("LSI1", "A#{Note}", Sparse = true)]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""SortKey([|"{Number}"|])""", "Number")]
    [Arguments("""SortKey([|"A#{Total}"|])""", "Total")]
    [Arguments("""GlobalIndexKey("GSI1", PartitionKey = "A", SortKey = [|"{Number}"|])""", "Number")]
    [Arguments("""SortKey("S"), LocalIndexKey("LSI1", [|"{Number}"|])""", "Number")]
    public async Task Build_NumberWithoutAFormatInASortKey_ReportsDyna108(string attributes, string property)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), {{attributes}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA108 Warning: Number '{property}' has no fixed format in a sort key, so 10 sorts before 9. Use d<w> or n<i>, or {{{property}:plain}}."));
    }

    [Test]
    [Arguments("{Number:plain}")]
    [Arguments("{Number:d4}")]
    [Arguments("{Number:n4}")]
    [Arguments("{Total:plain}")]
    [Arguments("{Total:n4.2}")]
    public async Task Build_NumberWithAFormatInASortKey_ReportsNothing(string template)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("{{template}}")]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_NumberWithoutAFormatInAPartitionKey_ReportsNothing()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("A#{N}")] public partial class E { public int N { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""[[|KeyOnly|]] public DateTime At { get; set; }""", "ORDER#{At:yyyy-MM-dd}", "At", "no key holds it without loss, a timestamp needs the format ticks")]
    [Arguments("""[[|KeyOnly|]] public string Other { get; set; } = "";""", "E", "Other", "no key template uses it")]
    [Arguments("""[[|KeyOnly|]] public string Calculated => "x";""", "A#{Calculated}", "Calculated", "it is computed")]
    [Arguments("""[[|KeyOnly|], DynamoDbIgnore] public string Id { get; set; } = "";""", "E", "Id", "it is also [DynamoDbIgnore]")]
    public async Task Build_InvalidKeyOnly_ReportsDyna109(string member, string template, string property, string reason)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{{template}}")]
            public partial class E { {{member}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA109 Error: [KeyOnly] on '{property}' is invalid: {reason}"));
    }

    [Test]
    public async Task Build_PlainKeyThatIsKeyOnly_ReportsDyna109()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [[|PartitionKey|], KeyOnly] public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA109 Error: [KeyOnly] on 'Id' is invalid: a plain key is stored as itself"));
    }

    [Test]
    public async Task Build_KeyOnlyInATemplateWithAnError_ReportsOnlyTheTemplate()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"A#{Missing}"|])]
            public partial class E { [KeyOnly] public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA102 Error: Key template references unknown property 'Missing'"));
    }

    [Test]
    public async Task Build_KeyOnlyWithALosslessKey_ReportsNothing()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{At:ticks}")]
            public partial class E { [KeyOnly] public DateTime At { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""[|GlobalIndexKey("GSI1", PartitionKey = "A#{Id}", Sparse = true)|]""")]
    [Arguments("""[|LocalIndexKey("GSI1", "A#{Id}", Sparse = true)|]""")]
    public async Task Build_SparseIndexWithoutNullableInputs_ReportsDyna110OnTheAttribute(string attribute)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), {{attribute}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA110 Warning: Index 'GSI1' is Sparse, but all its inputs are non-nullable, so every item is indexed"));
    }

    [Test]
    public async Task Build_SparseIndexOverANullablePlainProperty_ReportsNothing()
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "{Note}", PartitionKeyAttributeName = "Note", Sparse = true)]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("{V:raw}#x")]
    [Arguments("{V:raw}{{")]
    [Arguments("{V:raw}{W}")]
    [Arguments("A#{V:raw}#{W}")]
    public async Task Build_RawThatDoesNotEndTheTemplate_ReportsDyna118(string template)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"{{template}}"|])]
            public partial class E { public string V { get; set; } = ""; public string W { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, RawNotLast));
    }

    [Test]
    [Arguments("""SortKey([|"{V:raw}#x"|])""")]
    [Arguments("""GlobalIndexKey("Gsi", PartitionKey = [|"{V:raw}#x"|])""")]
    [Arguments("""GlobalIndexKey("Gsi", PartitionKey = "P", SortKey = [|"{V:raw}#x"|])""")]
    [Arguments("""LocalIndexKey("Lsi", [|"{V:raw}#x"|]), SortKey("S")""")]
    public async Task Build_RawThatDoesNotEndATemplateOfAnotherKey_ReportsDyna118(string attribute)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A"), {{attribute}}]
            public partial class E { public string V { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, RawNotLast));
    }

    [Test]
    [Arguments("{V:raw}")]
    [Arguments("A#{V:raw}")]
    [Arguments("{W}#{V:raw}")]
    [Arguments("{N:d3}{V:raw}")]
    public async Task Build_RawThatEndsTheTemplate_ReportsNothing(string template)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{{template}}"), SortKey("S#{V:raw}"), GlobalIndexKey("Gsi", PartitionKey = "P#{W}", SortKey = "G#{V:raw}"), LocalIndexKey("Lsi", "L#{V:raw}")]
            public partial class E { public required string V { get; set; } public required string W { get; set; } public int N { get; set; } }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_RawInASortKey_IsNoStringBeforeADelimiter()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("P"), SortKey("{N:d3}#{V:raw}")]
            public partial class E { public required string V { get; set; } public int N { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_RawAndStrictInTwoTemplates_ReportsNothing()
    {
        // Arrange (a format belongs to its template, so the strict one goes on rejecting what the raw one holds)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{V:raw}"), GlobalIndexKey("Gsi", PartitionKey = "B#{V}")]
            public partial class E { public required string V { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_RawOverTheAttributeOfItsProperty_ReportsDyna004()
    {
        // Arrange (a format makes it a template, and only a bare placeholder is the attribute of its property)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"{V:raw}"|], AttributeName = "V")]
            public partial class E { public string V { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA004 Error: Attribute name 'V' is used by both 'V' and 'PartitionKey'"));
    }

    [Test]
    public async Task Build_PropertyTwiceInATemplate_ReportsDyna111()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey([|"{Id}#{Id:plain}"|])] public partial class E { public string Id { get; set; } = ""; }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA111 Error: Property 'Id' appears twice in a key template"));
    }

    [Test]
    public async Task Build_PropertyInSeveralKeysWithOtherFormats_ReportsNothing()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{At:yyyy}"), SortKey("{At:ticks}")]
            public partial class E { public DateTime At { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_KeyOnATypeWithoutATemplate_ReportsDyna112()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), [|PartitionKey|]] public partial class E { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA112 Error: A template belongs on the type, a plain key on a property"));
    }

    [Test]
    public async Task Build_TemplateOnAProperty_ReportsDyna112()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [[|PartitionKey("A#{Id}")|]] public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA112 Error: A template belongs on the type, a plain key on a property"));
    }

    [Test]
    [Arguments("[Flags] public enum Mode { A = 1, B = 2 }")]
    [Arguments("public enum Mode { A, B = A }")]
    public async Task Build_EnumNamesThatAreNotOneToOne_ReportsDyna113(string declaration)
    {
        // Arrange
        var source = $$"""
            {{declaration}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"A#{Status}"|])]
            public partial class E { public Mode Status { get; set; } }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA113 Error: Enum 'Shop.Mode' has [Flags] or aliased members, so its names are not one-to-one in a key. Use d<w>."));
    }

    [Test]
    public async Task Build_EnumNamesThatAreNotOneToOneWithAValueFormat_ReportsNothing()
    {
        // Arrange
        const string Source = """
            [Flags] public enum Mode { A = 1, B = 2 }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Status:d3}")]
            public partial class E { public Mode Status { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""GlobalIndexKey("GSI1", PartitionKey = [|"A#{Computed}"|])""")]
    [Arguments("""SortKey("S"), LocalIndexKey("LSI1", [|"A#{Computed}"|])""")]
    public async Task Build_ComputedPropertyInAnIndexTemplate_ReportsDyna114(string attribute)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), {{attribute}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, ComputedInIndex));
    }

    [Test]
    public async Task Build_ComputedPlainPropertyOverAnIndex_ReportsDyna114()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = [|"{Computed}"|], PartitionKeyAttributeName = "c")]
            public partial class E { [DynamoDbAttribute("c")] public string Computed => "x"; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, ComputedInIndex));
    }

    [Test]
    [Arguments("""PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "A#{Computed}")""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", "A#{Computed}")""")]
    [Arguments("""PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "{Computed}", PartitionKeyAttributeName = "c")""")]
    public async Task Build_ComputedFromOnAPropertyOfAnIndexTemplate_ReportsNothing(string attribute)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), {{attribute}}]
            public partial class E
            {
                public string Id { get; set; } = "id";
                [DynamoDbAttribute("c"), ComputedFrom(nameof(Id))] public string Computed => Id;
            }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_ComputedFromOnAKeyOfTheTable_ReportsNothing()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Computed}")]
            public partial class E { public string Id { get; set; } = "id"; [ComputedFrom(nameof(Id))] public string Computed => Id; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_SeveralDependenciesOfAnother_ReportsNothing()
    {
        // Arrange (a dependency may feed another key)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "B#{Computed}"), GlobalIndexKey("GSI2", PartitionKey = "C#{Name}")]
            public partial class E
            {
                public string Id { get; set; } = "id";
                public string Name { get; set; } = "name";
                [ComputedFrom(nameof(Id), nameof(Name))] public string Computed => Id + Name;
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""[[|ComputedFrom(nameof(Id))|]] public string Stored { get; set; } = "s";""", "A#{Id}#{Stored}", "Stored", "it is not computed")]
    [Arguments("""[[|ComputedFrom|]] public string Calculated => "x";""", "A#{Calculated}", "Calculated", "it names no property")]
    [Arguments("""[[|ComputedFrom(null)|]] public string Calculated => "x";""", "A#{Calculated}", "Calculated", "it names no property")]
    [Arguments("""[[|ComputedFrom("Gone")|]] public string Calculated => "x";""", "A#{Calculated}", "Calculated", "'Gone' is not a stored property of the type")]
    [Arguments("""[[|ComputedFrom((string?)null)|]] public string Calculated => "x";""", "A#{Calculated}", "Calculated", "'' is not a stored property of the type")]
    [Arguments("""[DynamoDbIgnore] public string Hidden { get; set; } = "h"; [[|ComputedFrom(nameof(Hidden))|]] public string Calculated => Hidden;""", "A#{Calculated}", "Calculated", "'Hidden' is not a stored property of the type")]
    [Arguments("""[KeyOnly] public string Held { get; set; } = "h"; [[|ComputedFrom(nameof(Held))|]] public string Calculated => Held;""", "A#{Held}#{Calculated}", "Calculated", "'Held' is not a stored property of the type")]
    [Arguments("""public string First => "x"; [[|ComputedFrom(nameof(First))|]] public string Calculated => First;""", "A#{Calculated}", "Calculated", "'First' is computed too")]
    [Arguments("""[DynamoDbAttribute("f")] public string First => "x"; [[|ComputedFrom(nameof(First))|]] public string Calculated => First;""", "A#{Calculated}", "Calculated", "'First' is computed too")]
    [Arguments("""[Version] public long Revision { get; set; } [[|ComputedFrom(nameof(Revision))|]] public long Calculated => Revision;""", "A#{Calculated}", "Calculated", "'Revision' is the version, which an update moves without a handle")]
    [Arguments("""[[|ComputedFrom(nameof(Id), nameof(Id))|]] public string Calculated => Id;""", "A#{Calculated}", "Calculated", "'Id' is named twice")]
    [Arguments("""[[|ComputedFrom(nameof(Id))|]] public string Calculated => Id;""", "A#{Id}", "Calculated", "no key template uses the property")]
    public async Task Build_InvalidComputedFrom_ReportsDyna119(string member, string template, string property, string reason)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{{template}}")]
            public partial class E { public string Id { get; set; } = "id"; {{member}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA119 Error: [ComputedFrom] on '{property}' is not valid: {reason}"));
    }

    [Test]
    public async Task Build_InvalidComputedFromOverAnIndex_ReportsDyna119AndNotDyna114()
    {
        // Arrange (the attribute is there, so what is wrong is what it says)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "A#{Calculated}")]
            public partial class E { [[|ComputedFrom("Gone")|]] public string Calculated => "x"; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA119 Error: [ComputedFrom] on 'Calculated' is not valid: 'Gone' is not a stored property of the type"));
    }

    [Test]
    public async Task Build_ComputedPropertyInATableKey_ReportsNothing()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Computed}")] public partial class E { public string Computed => "x"; }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""SortKey([|"USER#{Name}#1"|])""", "#")]
    [Arguments("""GlobalIndexKey("GSI1", PartitionKey = "A", SortKey = [|"{Name}-x"|])""", "-")]
    public async Task Build_StringBeforeADelimiterInASortKey_ReportsDyna115(string attribute, string delimiter)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), {{attribute}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA115 Info: String 'Name' is followed by '{delimiter}' in a sort key, so values with a character below '{delimiter}' sort out of segment order"));
    }

    [Test]
    [Arguments("""SortKey("USER#{Name}")""")]
    [Arguments("""SortKey("{Name} x")""")]
    [Arguments("""SortKey("{Unique}#x")""")]
    [Arguments("""SortKey("{Paint}#x")""")]
    public async Task Build_NoStringBeforeADelimiterThatMatters_ReportsNothing(string attribute)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), {{attribute}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_StringBeforeADelimiterInAPartitionKey_ReportsNothing()
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{Name}#x")]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_InternalInputOfAPublicEntity_ReportsDyna016OnTheTemplate()
    {
        // Arrange
        const string Source = """
            internal enum Hidden { One }
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"A#{V}"|])]
            public partial class E { public Hidden V { get; set; } [DynamoDbAttribute("n")] private int N { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA016 Error: Type 'Shop.Hidden' used by 'V' is not accessible to generated code"));
    }

    [Test]
    [Arguments("""PartitionKey("E"), SortKey([|"{At}"|])""")]
    [Arguments("""PartitionKey("E"), SortKey([|"AT#{At}"|])""")]
    [Arguments("""PartitionKey("E"), SortKey([|"{Number:d4}#{At}"|])""")]
    [Arguments("""PartitionKey("E"), SortKey([|"{At:ms}"|])""")]
    [Arguments("""PartitionKey("E"), SortKey([|"{At:us}"|])""")]
    [Arguments("""PartitionKey("E"), SortKey([|"{At:s}"|])""")]
    [Arguments("""PartitionKey("E"), SortKey([|"{At}#END"|])""")]
    [Arguments("""PartitionKey([|"EVENT#{At}"|])""")]
    public async Task Build_TruncatedTimestampAtTheEndOfATableKey_ReportsDyna116(string attributes)
    {
        // Arrange
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), {{attributes}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA116 Warning: Timestamp 'At' ends the key of the table and is truncated, so two items close in time get one key and the second replaces the first. Add a placeholder after it that breaks the tie, or use {At:ticks}."));
    }

    [Test]
    public async Task Build_DateTimeAtTheEndOfATableKey_ReportsDyna116()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey([|"{Utc}"|])]
            public partial class E { public DateTime Utc { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA116 Warning: Timestamp 'Utc' ends the key of the table and is truncated, so two items close in time get one key and the second replaces the first. Add a placeholder after it that breaks the tie, or use {Utc:ticks}."));
    }

    [Test]
    [Arguments("""PartitionKey("E"), SortKey("{At:ticks}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{Unique}#{At:ticks}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{At}#{Unique}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{At:ms}#{Id}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{At:yyyy-MM-dd}#{Unique}")""")]
    [Arguments("""PartitionKey("E"), SortKey("MONTH#{At:yyyy-MM}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{At:yyyy}")""")]
    [Arguments("""PartitionKey("E"), SortKey("DAY#{At:yyyy-MM-dd}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{At:yyyy-MM-ddTHH}")""")]
    [Arguments("""PartitionKey("E"), SortKey("WINDOW#{At:yyyy-MM-ddTHH:mm}")""")]
    [Arguments("""PartitionKey("EVENT#{At:yyyy-MM-dd}"), SortKey("{Unique}")""")]
    [Arguments("""PartitionKey("EVENT#{At}"), SortKey("{Unique}")""")]
    [Arguments("""PartitionKey("EVENT#{At:ticks}")""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", "{At}")""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), GlobalIndexKey("GSI1", PartitionKey = "{Id}", SortKey = "{At}")""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), GlobalIndexKey("GSI1", PartitionKey = "{At}")""")]
    [Arguments("""PartitionKey("E"), SortKey("{At}", AttributeName = "At")""")]
    public async Task Build_TimestampThatCannotLoseAnItem_ReportsNothing(string attributes)
    {
        // Arrange (a later placeholder breaks a tie, a format of a minute or coarser says one item per bucket, an index is not unique, and a partition key beside a sort key is not all of the key)
        var source = $$"""
            {{Enum}}
            [DynamoDbEntity, Discriminator("E"), {{attributes}}]
            public partial class E { {{Members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_DateAtTheEndOfATableKey_ReportsNothing()
    {
        // Arrange (a date has no time to lose)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("{Day}")]
            public partial class E { public DateOnly Day { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_PlainTimestampKey_ReportsNothing()
    {
        // Arrange (the converter of a property keeps all its digits)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E")]
            public partial class E { [SortKey] public DateTimeOffset At { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("{X}")""", """public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("{X}", AttributeName = "X")""", """public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("E"), SortKey("S#{X}")""", """public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), GlobalIndexKey("GSI1", PartitionKey = "G#{X}")""", """public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", "L#{X}")""", """public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; init; } = "";""")]
    [Arguments("""PartitionKey("A#{X}")""", """[KeyOnly] public string [|X|] { get; set; } = "";""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = string.Empty;""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = String.Empty;""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = System.String.Empty;""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = null!;""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = default!;""")]
    [Arguments("""PartitionKey("A#{X}")""", """public string [|X|] { get; set; } = default(string)!;""")]
    [Arguments("""PartitionKey("E")""", """[SortKey] public string [|X|] { get; set; } = string.Empty;""")]
    [Arguments("""PartitionKey("E")""", """[SortKey] public byte[] [|X|] { get; set; } = [];""")]
    [Arguments("""PartitionKey("E")""", """[SortKey] public byte[] [|X|] { get; set; } = Array.Empty<byte>();""")]
    [Arguments("""PartitionKey("A#{X}")""", """public E() { Other = "y"; } public string [|X|] { get; set; } = ""; public string Other { get; set; }""")]
    public async Task Build_KeyPropertyThatStartsEmpty_ReportsDyna117(string attributes, string members)
    {
        // Arrange (the shape of an entity that a `with` fills in later: forgetting that compiles and throws on the put)
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), {{attributes}}]
            public partial class E { {{members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA117 Warning: Key property 'X' starts empty and nothing has to set it, so a write that forgets it fails at run time. Make it required, or set it in a constructor."));
    }

    [Test]
    public async Task Build_PropertyThatStartsEmptyInTwoKeys_ReportsDyna117Once()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}"), SortKey("B#{X}"), GlobalIndexKey("GSI1", PartitionKey = "G#{X}")]
            public partial class E { public string [|X|] { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA117 Warning: Key property 'X' starts empty and nothing has to set it, so a write that forgets it fails at run time. Make it required, or set it in a constructor."));
    }

    [Test]
    public async Task Build_InheritedKeyPropertyThatStartsEmpty_ReportsDyna117()
    {
        // Arrange
        const string Source = """
            public abstract class Base { public string [|X|] { get; set; } = ""; }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}")]
            public partial class E : Base { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA117 Warning: Key property 'X' starts empty and nothing has to set it, so a write that forgets it fails at run time. Make it required, or set it in a constructor."));
    }

    [Test]
    [Arguments("""public required string X { get; set; } = "";""")]
    [Arguments("""public required string X { get; set; }""")]
    [Arguments("""public string X { get; set; }""")]
    [Arguments("""public string X => "x";""")]
    [Arguments("""public string X { get; } = "";""")]
    [Arguments("""public string X { get; set; } = "main";""")]
    [Arguments("""public string X { get; set; } = Guid.NewGuid().ToString();""")]
    [Arguments("""public string X { get; set; } = Create(); private static string Create() => "x";""")]
    [Arguments("""public string X { get; set; } = Text.Empty; private static class Text { public const string Empty = "x"; }""")]
    [Arguments("""public int X { get; set; } = default;""")]
    [Arguments("""public int X { get; set; } = 0;""")]
    [Arguments("""public Guid X { get; set; } = Guid.Empty;""")]
    [Arguments("""public E() { X = "x"; } public string X { get; set; } = "";""")]
    [Arguments("""public E() => this.X = "x"; public string X { get; set; } = "";""")]
    [Arguments("""public E(string x) { X = x; } public string X { get; set; } = "";""")]
    [Arguments("""public E() { } public E(string x, string y) => (X, Y) = (x, y); public string X { get; set; } = ""; public string Y { get; set; } = "";""")]
    public async Task Build_KeyPropertyThatCannotBeForgotten_ReportsNothing(string members)
    {
        // Arrange (required and a constructor make the caller say it, a default of value is a key of its own, and a computed property has no value to forget)
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}")]
            public partial class E { {{members}} }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""public partial record E(string X)""")]
    [Arguments("""public partial record E(string X = "")""")]
    [Arguments("""public partial class E(string x) { public string X { get; set; } = x; }""")]
    public async Task Build_KeyPropertyOfAPrimaryConstructor_ReportsNothing(string declaration)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}")]
            {{declaration}};
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_KeyPropertySetByTheConstructorOfItsBase_ReportsNothing()
    {
        // Arrange
        const string Source = """
            public abstract class Base { protected Base() { X = "x"; } public string X { get; set; } = ""; }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}")]
            public partial class E : Base { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_EmptyDefaultOutsideAKey_ReportsNothing()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}"), GlobalIndexKey("GSI1", PartitionKey = "G#{Owner}", Sparse = true)]
            public partial class E
            {
                public required string X { get; set; }
                public string Note { get; set; } = "";
                public string? Owner { get; set; } = null;
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_InternalInputOfAnInternalEntity_ReportsNothing()
    {
        // Arrange
        const string Source = """
            internal enum Hidden { One }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{V}")]
            internal partial class E { public Hidden V { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string FormatsOf(string type) => type switch
    {
        "string" => "no format, or raw at the end of the template",
        "int" => "plain, the default, d<w>, such as d6, or n<i>, such as n10",
        "decimal" => "plain, the default, or n<i>.<f>, such as n10.2",
        "DateTimeOffset" or "DateTime" => "yyyy, yyyy-MM, yyyy-MM-dd, yyyy-MM-ddTHH, yyyy-MM-ddTHH:mm, s, ms, the default, us or ticks",
        "Color" => "no format for the member name, or d<w>, such as d3, for its value",
        _ => "no format",
    };
}
