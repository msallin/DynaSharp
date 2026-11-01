namespace DynaSharp.UnitTests.Generators.Diagnostics;

/// <summary>
/// DYNA1xx: the rules of the key grammar. Each test states id, severity, full message and, through the markers in the source, the span.
/// </summary>
public sealed class KeyDiagnosticsTests
{
    private const string Members = """
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
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
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA104 Error: Format '{format}' does not fit type '{shown}'"));
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
    [Arguments("""[[|KeyOnly|]] public DateTime At { get; set; }""", "ORDER#{At}", "At", "no key holds it without loss, a timestamp needs the format ticks")]
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
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA114 Error: Computed property 'Computed' cannot be used in an index template, because an update cannot keep the index current"));
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
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA114 Error: Computed property 'Computed' cannot be used in an index template, because an update cannot keep the index current"));
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
}
