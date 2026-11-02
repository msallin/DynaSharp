using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators.Diagnostics;

/// <summary>
/// DYNA0xx: the rules about types and properties. Each test states id, severity, full message and, through the markers in the source, the span.
/// </summary>
public sealed class TypeDiagnosticsTests
{
    [Test]
    [Arguments("DYNA001", "[DynamoDbDocument] public partial class A { }")]
    [Arguments("DYNA002", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { }""")]
    [Arguments("DYNA003", "[DynamoDbDocument] public partial class A { public string X { get; set; } = string.Empty; }")]
    [Arguments("DYNA004", """[DynamoDbDocument] public partial class A { [DynamoDbAttribute("a")] public int X { get; set; } [DynamoDbAttribute("b")] public int Y { get; set; } }""")]
    [Arguments("DYNA004 key shared", """[DynamoDbEntity, Discriminator("E"), PartitionKey("{Id}"), SortKey("X#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "X#{Id}", PartitionKeyAttributeName = "sk", SortKey = "{Id}", SortKeyAttributeName = "pk")] public partial class E { public string Id { get; set; } = "id"; }""")]
    [Arguments("DYNA005", """[DynamoDbDocument] public partial class A { public A() { } public A(int x) { } }""")]
    [Arguments("DYNA006", "[DynamoDbDocument] public partial class A { public A(int x) { X = x; } public int X { get; } }")]
    [Arguments("DYNA007", "[DynamoDbDocument] public sealed partial class A { }")]
    [Arguments("DYNA008", "[DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(C))] public int X { get; set; } } public readonly struct C : IDynamoDbConverter<int> { public static void Write(DynamoDbWriter w, int v) { } public static int Read(ref DynamoDbReader r, AttributeType t) => 0; }")]
    [Arguments("DYNA009", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S")] public partial class E { }""")]
    [Arguments("DYNA010", """[DynamoDbDocument] public partial class A { [DynamoDbAttribute("name")] public int X { get; set; } }""")]
    [Arguments("DYNA010 pair", """[DynamoDbDocument] public partial class A { [DynamoDbAttribute("\uD83D\uDE00")] public int X { get; set; } }""")]
    [Arguments("DYNA011", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { [Version] public long V { get; set; } }""")]
    [Arguments("DYNA012", "[DynamoDbDocument] public partial class A { [DynamoDbIgnore] public int X { get; set; } }")]
    [Arguments("DYNA013", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { }""")]
    [Arguments("DYNA014", "[DynamoDbDocument] public partial class A { public int Keys { get; set; } }")]
    [Arguments("DYNA016", "[DynamoDbDocument] public partial class A { private int Hidden { get; set; } public int X { get; set; } }")]
    [Arguments("DYNA017", """[DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey] public long Id { get; set; } }""")]
    [Arguments("DYNA018", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("GSI1", PartitionKey = "A"), LocalIndexKey("LSI1", "B")] public partial class E { }""")]
    [Arguments("DYNA020", """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("E")] public partial class E { }""")]
    [Arguments("DYNA021", """[DynamoDbEntity, Discriminator("E")] public partial class E { [PartitionKey] public string Id { get; set; } = "id"; [DynamoDbIgnore] public int X { get; set; } }""")]
    [Arguments("DYNA022", """[DynamoDbEntity, Discriminator("E", AttributeName = "kind"), PartitionKey("E")] public partial class E { }""")]
    [Arguments("DYNA023", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; [Version] public long V { get; set; } }""")]
    [Arguments("DYNA024", """[DynamoDbDocument] public partial class A { public Nothing? X { get; set; } } public enum Nothing { }""")]
    [Arguments("DYNA025", "[DynamoDbDocument] public partial class Keys { }")]
    [Arguments("DYNA030 property", "[DynamoDbDocument] public partial class A { [OmitEmpty] public string? X { get; set; } }")]
    [Arguments("DYNA030 ignored", "[DynamoDbDocument] public partial class A { [OmitEmpty, DynamoDbIgnore] public string? X { get; set; } }")]
    [Arguments("DYNA030 type", "[DynamoDbDocument, OmitEmpty] public partial class A { public string? X { get; set; } public int Y { get; set; } public int? Z { get; set; } public string W { get; set; } = \"\"; }")]
    [Arguments("DYNA030 keys on a type", """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "O#{Owner}", Sparse = true), GlobalIndexKey("GSI2", PartitionKey = "{Plain}", PartitionKeyAttributeName = "Plain", Sparse = true), OmitEmpty] public partial class E { public string Id { get; set; } = "id"; public string? Owner { get; set; } public string? Plain { get; set; } }""")]
    public async Task Build_SourceThatKeepsTheRule_ReportsNothing(string rule, string source)
    {
        // Arrange
        _ = rule;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("int")]
    [Arguments("int?")]
    [Arguments("bool")]
    [Arguments("Guid")]
    public async Task Build_OmitEmptyOnWhatCannotBeEmpty_ReportsDyna030(string type)
    {
        // Arrange
        var source = $$"""[DynamoDbDocument] public partial class A { [[|OmitEmpty|]] public {{type}} X { get; set; } = default!; }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA030 Error: [OmitEmpty] on 'X' is not valid: it is no string, list, set, dictionary or array"));
    }

    [Test]
    [Arguments("int[,]")]
    [Arguments("ICollection<string>")]
    public async Task Build_OmitEmptyOnACollectionThatNoBuiltInStores_ReportsDyna030(string type)
    {
        // Arrange (a converter stores these, and a handle has no way to ask for their count)
        var source = $$"""
            [DynamoDbDocument] public partial class A { [[|OmitEmpty|], DynamoDbConverter(typeof(C))] public {{type}} X { get; set; } = default!; }
            public readonly record struct C : IDynamoDbConverter<{{type}}>
            {
                public static void Write(DynamoDbWriter writer, {{type}} value) { }
                public static {{type}} Read(ref DynamoDbReader reader, AttributeType type) => default!;
            }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA030 Error: [OmitEmpty] on 'X' is not valid: it is no string, list, set, dictionary or array"));
    }

    [Test]
    [Arguments("string")]
    [Arguments("string?")]
    [Arguments("List<string>")]
    [Arguments("List<string>?")]
    [Arguments("IReadOnlyList<int>")]
    [Arguments("IList<int>?")]
    [Arguments("HashSet<string>")]
    [Arguments("IReadOnlySet<string>?")]
    [Arguments("Dictionary<string, int>")]
    [Arguments("IReadOnlyDictionary<string, int>?")]
    [Arguments("string[]")]
    [Arguments("int[]?")]
    [Arguments("byte[]")]
    public async Task Build_OmitEmptyOnAStringOrACollection_ReportsNothing(string type)
    {
        // Arrange
        var source = $$"""[DynamoDbDocument] public partial class A { [OmitEmpty] public {{type}} X { get; set; } = default!; }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("public required string X { get; set; }")]
    [Arguments("public required List<string> X { get; set; }")]
    public async Task Build_OmitEmptyOnARequiredProperty_ReportsDyna030(string member)
    {
        // Arrange
        var source = $$"""[DynamoDbDocument] public partial class A { [[|OmitEmpty|]] {{member}} }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA030 Error: [OmitEmpty] on 'X' is not valid: a read throws without it, so an empty value could not be read back"));
    }

    [Test]
    public async Task Build_OmitEmptyOnAParameterWithoutADefault_ReportsDyna030()
    {
        // Arrange (the same thing as `required` for a read: nothing else gives the constructor a value)
        const string Source = """[DynamoDbDocument] public partial record A([property: [|OmitEmpty|]] string X);""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA030 Error: [OmitEmpty] on 'X' is not valid: a read throws without it, so an empty value could not be read back"));
    }

    [Test]
    [Arguments("[OmitEmpty] public required string? X { get; set; }")]
    [Arguments("[OmitEmpty] public required HashSet<string> X { get; set; }")]
    public async Task Build_OmitEmptyOnARequiredPropertyThatAReadCanDoWithout_ReportsNothing(string member)
    {
        // Arrange (a missing attribute reads as null and as an empty set, so these do come back)
        var source = $$"""[DynamoDbDocument] public partial class A { {{member}} }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_OmitEmptyOnARequiredKeyInput_ReportsDyna030Once()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { [[|OmitEmpty|]] public required string Id { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA030 Error: [OmitEmpty] on 'Id' is not valid: it feeds a key"));
    }

    [Test]
    [Arguments("[OmitEmpty(false)] public int X { get; set; }")]
    [Arguments("[OmitEmpty(false)] public required string X { get; set; }")]
    [Arguments("[OmitEmpty(true), DynamoDbAttribute(\"x\")] public string X { get; set; } = \"\";")]
    public async Task Build_OmitEmptyThatSaysWhatIsSo_ReportsNothing(string member)
    {
        // Arrange (false asks for what such a property does anyway)
        var source = $$"""[DynamoDbDocument] public partial class A { {{member}} }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_OmitEmptyFalseOnAKeyInput_ReportsNothing()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { [OmitEmpty(false)] public string Id { get; set; } = "id"; }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_OmitEmptyOnAPropertyInATemplate_ReportsDyna030()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "O#{Owner}", Sparse = true)] public partial class E { public string Id { get; set; } = ""; [[|OmitEmpty|]] public string? Owner { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA030 Error: [OmitEmpty] on 'Owner' is not valid: it feeds a key"));
    }

    [Test]
    public async Task Build_OmitEmptyOnAPlainKeyOfAnIndex_ReportsDyna030()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "{Owner}", PartitionKeyAttributeName = "Owner", Sparse = true)] public partial class E { public string Id { get; set; } = ""; [[|OmitEmpty|]] public string? Owner { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA030 Error: [OmitEmpty] on 'Owner' is not valid: it feeds a key"));
    }

    [Test]
    public async Task Build_OmitEmptyOnAKeyOnlyProperty_ReportsDyna030()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("GSI1", PartitionKey = "O#{Owner}", Sparse = true)] public partial class E { public string Id { get; set; } = ""; [KeyOnly, [|OmitEmpty|]] public string? Owner { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA030 Error: [OmitEmpty] on 'Owner' is not valid: it feeds a key"));
    }

    [Test]
    public async Task Build_OmitEmptyOnAnIntegerInAKey_ReportsDyna030Once()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { [[|OmitEmpty|]] public int Id { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA030 Error: [OmitEmpty] on 'Id' is not valid: it is no string, list, set, dictionary or array"));
    }

    [Test]
    public async Task Build_ClassNotPartial_ReportsDyna001()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public class [|A|] { }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA001 Error: Type 'A' must be partial, as must its containing types"));
    }

    [Test]
    public async Task Build_SecondPartCarriesTheAttribute_ReportsOnThatPart()
    {
        // Arrange
        const string Source = """
            public partial class A { public A(int x) { } }
            [DynamoDbDocument] public partial class [|A|] { public A(string y) { } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA005 Error: Type 'A' has no single usable constructor. Mark one with [DynamoDbConstructor]."));
    }

    [Test]
    public async Task Build_ContainingTypeNotPartial_ReportsDyna001OnTheMappedType()
    {
        // Arrange
        const string Source = "public class Outer { [DynamoDbDocument] public partial class [|A|] { } }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA001 Error: Type 'A' must be partial, as must its containing types"));
    }

    [Test]
    public async Task Build_EntityWithoutPartitionKey_ReportsDyna002()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E")] public partial class [|E|] { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA002 Error: Entity 'E' has no partition key. Add [PartitionKey], or use [DynamoDbDocument] for a nested type."));
    }

    [Test]
    [Arguments("object", "object")]
    [Arguments("Stack<int>", "System.Collections.Generic.Stack<int>")]
    [Arguments("Dictionary<int, string>", "System.Collections.Generic.Dictionary<int, string>")]
    [Arguments("HashSet<object>", "System.Collections.Generic.HashSet<object>")]
    [Arguments("HashSet<string?>", "System.Collections.Generic.HashSet<string?>")]
    [Arguments("HashSet<bool>", "System.Collections.Generic.HashSet<bool>")]
    [Arguments("List<object>", "System.Collections.Generic.List<object>")]
    [Arguments("object?[]", "object?[]")]
    [Arguments("int[,]", "int[*,*]")]
    [Arguments("dynamic", "dynamic")]
    public async Task Build_UnsupportedType_ReportsDyna003(string type, string shown)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class A {{ public {type} [|X|] {{ get; set; }} = default!; }}";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA003 Error: Property 'X' has unsupported type '{shown}'. Add a [DynamoDbConverter] or use a supported type."));
    }

    [Test]
    [Arguments("IEnumerable<string>")]
    [Arguments("ICollection<string>")]
    [Arguments("IReadOnlyCollection<string>")]
    public async Task Build_CollectionWithoutOrder_ReportsDyna003SuggestingAList(string type)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class A {{ public {type} [|X|] {{ get; set; }} = default!; }}";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(
            source,
            $"DYNA003 Error: Property 'X' has unsupported type 'System.Collections.Generic.{type}'. Use IReadOnlyList<T>, or add a [DynamoDbConverter]."));
    }

    [Test]
    public async Task Build_TwoPropertiesOneAttribute_ReportsDyna004OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbDocument] public partial class A
            {
                [DynamoDbAttribute("a")] public int X { get; set; }
                [DynamoDbAttribute("a")] public int [|Y|] { get; set; }
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA004 Error: Attribute name 'a' is used by both 'X' and 'Y'"));
    }

    [Test]
    public async Task Build_KeyAttributeEqualToAProperty_ReportsDyna004OnTheKey()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey([|"A#{Id}"|], AttributeName = "Id")]
            public partial class E { public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA004 Error: Attribute name 'Id' is used by both 'Id' and 'PartitionKey'"));
    }

    [Test]
    public async Task Build_TwoKeysOneAttributeOtherTemplates_ReportsDyna004()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}"), GlobalIndexKey("GSI1", PartitionKey = [|"B#{Id}"|], PartitionKeyAttributeName = "pk")]
            public partial class E { public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA004 Error: Attribute name 'pk' is used by both 'PartitionKey' and 'GSI1.PartitionKey'"));
    }

    [Test]
    public async Task Build_TwoConstructorsWithParameters_ReportsDyna005()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class [|A|] { public A(int x) { } public A(string y) { } }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA005 Error: Type 'A' has no single usable constructor. Mark one with [DynamoDbConstructor]."));
    }

    [Test]
    public async Task Build_TwoConstructorsMarked_ReportsDyna005()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class [|A|] { [DynamoDbConstructor] public A() { } [DynamoDbConstructor] public A(int x) { } }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA005 Error: Type 'A' has no single usable constructor. Mark one with [DynamoDbConstructor]."));
    }

    [Test]
    [Arguments("public A(int [|y|]) { } public int X { get; set; }", "y")]
    [Arguments("public A(string [|x|]) { } public int X { get; set; }", "x")]
    [Arguments("public A(int [|x|]) { } [DynamoDbIgnore] public int X { get; set; }", "x")]
    public async Task Build_ParameterWithoutMappedProperty_ReportsDyna006(string members, string parameter)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class A {{ {members} }}";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA006 Error: Constructor parameter '{parameter}' has no matching mapped property"));
    }

    [Test]
    [Arguments("[DynamoDbDocument] public abstract partial class [|A|] { }", "it is abstract")]
    [Arguments("[DynamoDbDocument] public static partial class [|A|] { }", "it is static")]
    [Arguments("[DynamoDbDocument] public partial class [|A|]<T> { }", "it is generic")]
    [Arguments("public partial class Outer<T> { [DynamoDbDocument] public partial class [|A|] { } }", "it is generic")]
    [Arguments("[DynamoDbDocument] public ref partial struct [|A|] { }", "it is a ref struct")]
    [Arguments("[DynamoDbDocument] public partial interface [|A|] { }", "it is an interface")]
    [Arguments("[DynamoDbDocument] file partial class [|A|] { }", "it is file-local")]
    [Arguments("file partial class Outer { [DynamoDbDocument] public partial class [|A|] { } }", "it is file-local")]
    public async Task Build_ShapeThatCannotBeMapped_ReportsDyna007(string source, string reason)
    {
        // Arrange, Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA007 Error: Type 'A' cannot be mapped: {reason}"));
    }

    [Test]
    [Arguments("""[DynaSharp.Mapping.DynamoDbDocument] file partial class A { }""")]
    [Arguments("""[DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.Discriminator("E"), DynaSharp.Mapping.PartitionKey("E")] file partial class A { }""")]
    [Arguments("""file partial class Outer { [DynaSharp.Mapping.DynamoDbDocument] public partial class A { } }""")]
    public async Task Run_FileLocalType_ReportsDyna007AndEmitsNothing(string declaration)
    {
        // Arrange (a part in another file would be another type, so the generated code could not merge)
        var source = $"namespace Shop; {declaration}";

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((output.Problems.Count(problem => problem.Contains("error DYNA007", StringComparison.Ordinal)), output.Problems.Length, output.Result.GeneratedTrees.Length)).IsEqualTo((1, 1, 0));
    }

    [Test]
    public async Task Run_TypeBesideAFileLocalType_IsMapped()
    {
        // Arrange (only the mapped type and what holds it count)
        const string Source = """namespace Shop; file class Helper { } [DynaSharp.Mapping.DynamoDbDocument] public partial class A { }""";

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((string.Join(", ", output.Problems), output.Result.GeneratedTrees.Length)).IsEqualTo((string.Empty, 1));
    }

    [Test]
    public async Task Build_ConverterForAnotherType_ReportsDyna008OnTheAttribute()
    {
        // Arrange
        const string Source = """
            [DynamoDbDocument] public partial class A { [[|DynamoDbConverter(typeof(C))|]] public string X { get; set; } = ""; }
            public readonly struct C : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter w, int v) { }
                public static int Read(ref DynamoDbReader r, AttributeType t) => 0;
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA008 Error: Converter 'Shop.C' does not implement IDynamoDbConverter<string> or has no accessible definition"));
    }

    [Test]
    [Arguments("typeof(IDynamoDbConverter<int>)", "DynaSharp.Serialization.IDynamoDbConverter<int>")]
    [Arguments("typeof(Open<>)", "Shop.Open<>")]
    [Arguments("typeof(Outer.Hidden)", "Shop.Outer.Hidden")]
    [Arguments("typeof(int[])", "int[]")]
    [Arguments("null!", "null")]
    public async Task Build_ConverterThatCannotBeUsed_ReportsDyna008(string argument, string shown)
    {
        // Arrange
        var source = $$"""
            [DynamoDbDocument] public partial class A { [[|DynamoDbConverter({{argument}})|]] public int X { get; set; } }
            public readonly struct Open<T> : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter w, int v) { }
                public static int Read(ref DynamoDbReader r, AttributeType t) => 0;
            }
            public class Outer
            {
                private readonly struct Hidden : IDynamoDbConverter<int>
                {
                    public static void Write(DynamoDbWriter w, int v) { }
                    public static int Read(ref DynamoDbReader r, AttributeType t) => 0;
                }
            }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA008 Error: Converter '{shown}' does not implement IDynamoDbConverter<int> or has no accessible definition"));
    }

    [Test]
    public async Task Build_ConverterOnATypeThatDoesNotFit_ReportsDyna008OnThatAttribute()
    {
        // Arrange
        const string Source = """
            [DynamoDbDocument] public partial class A { public Money X { get; set; } }
            [[|DynamoDbConverter(typeof(C))|]] public readonly struct Money { }
            public readonly struct C { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA008 Error: Converter 'Shop.C' does not implement IDynamoDbConverter<Shop.Money> or has no accessible definition"));
    }

    [Test]
    public async Task Build_TwoPartitionKeysOnProperties_ReportsDyna009OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")] public partial class E
            {
                [PartitionKey] public string A { get; set; } = "";
                [[|PartitionKey|]] public string B { get; set; } = "";
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA009 Error: Key 'PartitionKey' is declared more than once"));
    }

    [Test]
    public async Task Build_SortKeyOnTypeAndProperty_ReportsDyna009OnTheProperty()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S")] public partial class E
            {
                [[|SortKey|]] public string B { get; set; } = "";
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA009 Error: Key 'SortKey' is declared more than once"));
    }

    [Test]
    [Arguments("""[DynamoDbAttribute([|""|])]""", "", "it is empty")]
    [Arguments("""[DynamoDbAttribute([|"\uD800"|])]""", "\uD800", "it is not well-formed UTF-16")]
    [Arguments("""[DynamoDbAttribute([|"a\uDC00"|])]""", "a\uDC00", "it is not well-formed UTF-16")]
    public async Task Build_InvalidAttributeName_ReportsDyna010(string attribute, string name, string reason)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class A {{ {attribute} public int X {{ get; set; }} }}";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA010 Error: Attribute name '{name}' is invalid: {reason}"));
    }

    [Test]
    public async Task Build_AttributeNameLongerThanTheLimit_ReportsDyna010()
    {
        // Arrange
        var name = new string('a', 65536);
        var source = $$"""[DynamoDbDocument] public partial class A { [DynamoDbAttribute([|"{{name}}"|])] public int X { get; set; } }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA010 Error: Attribute name '{name}' is invalid: it is longer than 65535 bytes of UTF-8"));
    }

    [Test]
    public async Task Build_AttributeNameAtTheLimit_ReportsNothing()
    {
        // Arrange
        var source = $$"""[DynamoDbDocument] public partial class A { [DynamoDbAttribute("{{new string('a', 65535)}}")] public int X { get; set; } }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_PropertyNamedLikeTheDiscriminator_ReportsDyna010OnTheProperty()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { public string [|_type|] { get; set; } = ""; }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA010 Error: Attribute name '_type' is invalid: it is reserved for the discriminator"));
    }

    [Test]
    public async Task Build_AttributeEqualToARenamedDiscriminator_ReportsDyna010OnTheArgument()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E", AttributeName = "kind"), PartitionKey("E")]
            public partial class E { [DynamoDbAttribute([|"kind"|])] public string K { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA010 Error: Attribute name 'kind' is invalid: it is reserved for the discriminator"));
    }

    [Test]
    public async Task Build_TypeAttributeOnAForeignTableOrADocument_IsNotReserved()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("E")] public partial class E { public string _type { get; set; } = ""; }
            [DynamoDbDocument] public partial class A { public string _type { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_KeyAttributeNameTooLongForAKey_ReportsDyna010OnTheArgument()
    {
        // Arrange
        var name = new string('a', 256);
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E", AttributeName = [|"{{name}}"|])]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA010 Error: Attribute name '{name}' is invalid: it is longer than 255 bytes of UTF-8"));
    }

    [Test]
    public async Task Build_KeyAttributeNamedLikeTheDiscriminator_ReportsDyna010OnTheArgument()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E", AttributeName = [|"_type"|])] public partial class E { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA010 Error: Attribute name '_type' is invalid: it is reserved for the discriminator"));
    }

    [Test]
    public async Task Build_IndexNameTooLongForItsDefaultAttributes_ReportsDyna010OnTheAttribute()
    {
        // Arrange
        var name = new string('a', 254);
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), [|GlobalIndexKey("{{name}}", PartitionKey = "A")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA010 Error: Attribute name '{name}pk' is invalid: it is longer than 255 bytes of UTF-8"));
    }

    [Test]
    public async Task Build_PlainKeyNameTooLongForAKey_ReportsDyna010()
    {
        // Arrange
        var name = new string('a', 256);
        var source = $$"""
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [PartitionKey, DynamoDbAttribute([|"{{name}}"|])] public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA010 Error: Attribute name '{name}' is invalid: it is longer than 255 bytes of UTF-8"));
    }

    [Test]
    public async Task Build_PlainKeyNameDiffersFromDynamoDbAttribute_ReportsDyna010()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [PartitionKey(AttributeName = "a"), DynamoDbAttribute([|"b"|])] public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA010 Error: Attribute name 'a' is invalid: it differs from [DynamoDbAttribute] on the same property"));
    }

    [Test]
    public async Task Build_DiscriminatorAttributeNameEmpty_ReportsDyna010OnTheArgument()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E", AttributeName = [|""|]), PartitionKey("E")]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA010 Error: Attribute name '' is invalid: it is empty"));
    }

    [Test]
    [Arguments("[[|Version|]] public string V { get; set; } = \"\";")]
    [Arguments("[[|Version|]] public long? V { get; set; }")]
    [Arguments("[[|Version|], DynamoDbIgnore] public long V { get; set; }")]
    [Arguments("[[|Version|]] public long V => 1;")]
    public async Task Build_VersionOnAnUnsuitableProperty_ReportsDyna011(string member)
    {
        // Arrange
        var source = $$"""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { {{member}} }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA011 Error: [Version] requires a single integer property without a converter; 'V' is not valid"));
    }

    [Test]
    public async Task Build_VersionWithAConverter_ReportsDyna011()
    {
        // Arrange (a condition compares numbers, and a converter may store something else)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E
            {
                [[|Version|], DynamoDbConverter(typeof(C))] public int V { get; set; }
            }

            public readonly struct C : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter writer, int value) => writer.WriteString("x");
                public static int Read(ref DynamoDbReader reader, AttributeType type) => 0;
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA011 Error: [Version] requires a single integer property without a converter; 'V' is not valid"));
    }

    [Test]
    [Arguments("""[PartitionKey("E#{V}")]""", "[[|Version|]] public long V { get; set; }")]
    [Arguments("""[PartitionKey("E"), SortKey("{V:n8}")]""", "[[|Version|]] public long V { get; set; }")]
    [Arguments("""[PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "V#{V}")]""", "[[|Version|]] public long V { get; set; }")]
    [Arguments("", "[PartitionKey, [|Version|]] public long V { get; set; }")]
    [Arguments("""[PartitionKey("E")]""", "[SortKey, [|Version|]] public long V { get; set; }")]
    public async Task Build_VersionInAKey_ReportsDyna023(string keys, string member)
    {
        // Arrange (a put writes the next version, and a key would move with it)
        var source = $$"""[DynamoDbEntity, Discriminator("E")]{{keys}} public partial class E { {{member}} }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA023 Error: [Version] property 'V' is part of a key. A write changes the version, and a key cannot follow it."));
    }

    [Test]
    [Arguments("[DynamoDbDocument] public partial class A { public Nothing [|X|] { get; set; } } public enum Nothing { }")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { public Nothing [|X|] { get; set; } } public enum Nothing { }""")]
    [Arguments("[DynamoDbDocument] public partial class A { public Nothing [|X|] { get; set; } } public enum Nothing : byte { }")]
    [Arguments("[DynamoDbDocument] public partial record A(Nothing [|X|]); public enum Nothing { }")]
    public async Task Build_PropertyOfAnEnumWithoutMembers_ReportsDyna024(string source)
    {
        // Arrange, Act (nothing can be written: an enum goes by the name of a member)
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, "DYNA024 Error: Property 'X' has enum type 'Shop.Nothing', which has no members, so every write throws. Add a member, make the property nullable, or add a [DynamoDbConverter]."));
    }

    [Test]
    [Arguments("public enum Mode { One } [DynamoDbDocument] public partial class A { public Mode X { get; set; } }")]
    [Arguments("public enum Mode { } [DynamoDbDocument] public partial class A { public Mode? X { get; set; } }")]
    [Arguments("[Flags] public enum Mode { } [DynamoDbDocument] public partial class A { public Mode X { get; set; } }")]
    [Arguments("public enum Mode { } [DynamoDbDocument] public partial class A { public List<Mode> X { get; set; } = new(); }")]
    [Arguments("public enum Mode { } [DynamoDbDocument] public partial class A { public HashSet<Mode> X { get; set; } = new(); }")]
    [Arguments("public enum Mode { } [DynamoDbDocument] public partial class A { public Dictionary<string, Mode> X { get; set; } = new(); }")]
    [Arguments("""public enum Mode { } [DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { [DynamoDbIgnore] public Mode X { get; set; } }""")]
    [Arguments("public enum Mode { } public readonly struct C : IDynamoDbConverter<Mode> { public static void Write(DynamoDbWriter w, Mode v) { } public static Mode Read(ref DynamoDbReader r, AttributeType t) => default; } [DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(C))] public Mode X { get; set; } }")]
    [Arguments("[DynamoDbConverter(typeof(C))] public enum Mode { } public readonly struct C : IDynamoDbConverter<Mode> { public static void Write(DynamoDbWriter w, Mode v) { } public static Mode Read(ref DynamoDbReader r, AttributeType t) => default; } [DynamoDbDocument] public partial class A { public Mode X { get; set; } }")]
    public async Task Build_EnumThatCanBeWritten_ReportsNothing(string source)
    {
        // Arrange, Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_TwoVersionProperties_ReportsDyna011OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E
            {
                [Version] public long A { get; set; }
                [[|Version|]] public long B { get; set; }
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA011 Error: [Version] requires a single integer property without a converter; 'B' is not valid"));
    }

    [Test]
    [Arguments("""[[|PartitionKey("A")|]] public partial class A { }""", "PartitionKey")]
    [Arguments("""[[|SortKey("A")|]] public partial class A { }""", "SortKey")]
    [Arguments("""[[|GlobalIndexKey("GSI1")|]] public partial class A { }""", "GlobalIndexKey")]
    [Arguments("""[[|LocalIndexKey("LSI1", "A")|]] public partial class A { }""", "LocalIndexKey")]
    [Arguments("""[[|Discriminator("A")|]] public partial class A { }""", "Discriminator")]
    [Arguments("public partial class A { [[|PartitionKey|]] public string X { get; set; } = \"\"; }", "PartitionKey")]
    [Arguments("public partial class A { [[|SortKey|]] public string X { get; set; } = \"\"; }", "SortKey")]
    [Arguments("public partial class A { [[|KeyOnly|]] public string X { get; set; } = \"\"; }", "KeyOnly")]
    [Arguments("public partial class A { [[|Version|]] public long X { get; set; } }", "Version")]
    [Arguments("""public partial class A { public int X { get; set; } [[|ComputedFrom(nameof(X))|]] public int Y => X; }""", "ComputedFrom")]
    public async Task Build_EntityAttributeOnADocument_ReportsDyna012(string declaration, string attribute)
    {
        // Arrange
        var source = "[DynamoDbDocument] " + declaration;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA012 Error: [{attribute}] is not valid on a document"));
    }

    [Test]
    public async Task Build_EntityAndDocument_ReportsDyna013Once()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, DynamoDbDocument] public partial class [|E|] { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA013 Error: Type 'E' cannot be both an entity and a document"));
    }

    [Test]
    [Arguments("public int [|Keys|] { get; set; }", "Keys")]
    [Arguments("public int [|Attrs|] { get; set; }", "Attrs")]
    [Arguments("public static class [|Keys|] { }", "Keys")]
    [Arguments("public void [|Keys|]() { }", "Keys")]
    [Arguments("public int [|Indexes|] { get; set; }", "Indexes")]
    [Arguments("public static class [|Indexes|] { }", "Indexes")]
    public async Task Build_MemberNamedLikeAGeneratedOne_ReportsDyna014(string member, string name)
    {
        // Arrange
        var source = $$"""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { {{member}} }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA014 Error: Generated member '{name}' conflicts with a member of 'E'"));
    }

    [Test]
    public async Task Build_DocumentWithAnIndexesMember_ReportsNothing()
    {
        // Arrange (only an entity has indexes)
        const string Source = "[DynamoDbDocument] public partial class A { public int Indexes { get; set; } }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_DocumentWithAnAttrsMember_ReportsDyna014()
    {
        // Arrange (a document has handles too, for the paths into it)
        const string Source = "[DynamoDbDocument] public partial class A { public int [|Attrs|] { get; set; } }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA014 Error: Generated member 'Attrs' conflicts with a member of 'A'"));
    }

    [Test]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class [|Keys|] { }""", "Keys")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class [|Attrs|] { }""", "Attrs")]
    [Arguments("[DynamoDbDocument] public partial class [|Attrs|] { }", "Attrs")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = "A")] public partial class [|Indexes|] { }""", "Indexes")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1", "B")] public partial class [|Indexes|] { }""", "Indexes")]
    public async Task Build_TypeNamedLikeAClassGeneratedIntoIt_ReportsDyna025(string source, string name)
    {
        // Arrange (C# has no member with the name of its enclosing type, so generating one would be CS0542)

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA025 Error: Type '{name}' has the name of a class that is generated into it, which C# does not allow. Rename the type."));
    }

    [Test]
    public async Task Run_TypeNamedLikeAClassGeneratedIntoIt_EmitsNothingForThatType()
    {
        // Arrange
        const string Source = """namespace Shop; [DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.Discriminator("E"), DynaSharp.Mapping.PartitionKey("E")] public partial class Keys { }""";

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((output.Problems.Count(problem => problem.Contains("error DYNA025", StringComparison.Ordinal)), output.Problems.Length, output.Result.GeneratedTrees.Length)).IsEqualTo((1, 1, 0));
    }

    [Test]
    [Arguments("[DynaSharp.Mapping.DynamoDbDocument] public partial class Keys { }")]
    [Arguments("[DynaSharp.Mapping.DynamoDbDocument] public partial class Indexes { }")]
    [Arguments("""[DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.Discriminator("E"), DynaSharp.Mapping.PartitionKey("E")] public partial class Indexes { }""")]
    public async Task Run_TypeNamedLikeAClassItDoesNotGet_Compiles(string declaration)
    {
        // Arrange (a document gets Attrs alone, and an entity gets Indexes only with an index)
        var source = $"namespace Shop; {declaration}";

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((string.Join(", ", output.Problems), output.Result.GeneratedTrees.Length)).IsEqualTo((string.Empty, 1));
    }

    [Test]
    public async Task Build_CSharp10_ReportsDyna015AndNoModel()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class [|E|] { }""";

        // Act
        var report = Analysis.Report(Source, LanguageVersion.CSharp10);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA015 Error: DynaSharp generated code needs C# 11 or later; this project uses 10.0"));
    }

    [Test]
    public async Task Build_CSharp10_SuppressesAllOutputForTheType()
    {
        // Arrange
        const string Source = """namespace Shop { [DynaSharp.Mapping.DynamoDbDocument] public partial class A { } }""";

        // Act
        var result = Analysis.Results(Source, [], LanguageVersion.CSharp10).Single();

        // Assert
        await Assert.That(result.Type).IsNull();
    }

    [Test]
    public async Task Build_CSharp11_ReportsNothing()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { }""";

        // Act
        var report = Analysis.Report(Source, LanguageVersion.CSharp11);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("private enum Hidden { One } public Hidden [|X|] { get; set; }", "Shop.A.Hidden")]
    [Arguments("private enum Hidden { One } public List<Hidden> [|X|] { get; set; } = [];", "System.Collections.Generic.List<Shop.A.Hidden>")]
    [Arguments("protected enum Hidden { One } public Hidden[] [|X|] { get; set; } = [];", "Shop.A.Hidden[]")]
    public async Task Build_PropertyOfAPrivateType_ReportsDyna016(string members, string shown)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class A {{ {members} }}";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA016 Error: Type '{shown}' used by 'X' is not accessible to generated code"));
    }

    [Test]
    public async Task Build_PropertyOfAFileLocalType_ReportsDyna016()
    {
        // Arrange
        const string Source = """
            file enum Hidden { One }
            [DynamoDbDocument] internal partial class A { public Hidden [|X|] { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).Contains("is not accessible to generated code");
    }

    [Test]
    [Arguments("bool", "bool")]
    [Arguments("List<string>", "System.Collections.Generic.List<string>")]
    [Arguments("AttributeValue", "DynaSharp.AttributeValue")]
    public async Task Build_PlainKeyOfANonKeyType_ReportsDyna017(string type, string shown)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [[|PartitionKey|]] public {{type}} X { get; set; } = default!; }
            """;

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA017 Error: Property 'X' of type '{shown}' cannot be a DynamoDB key, which must be a string, number or binary"));
    }

    [Test]
    [Arguments("""[|GlobalIndexKey("GSI1")|]""", "GSI1", "it has no partition key")]
    [Arguments("""[|GlobalIndexKey("GSI1", SortKey = "A")|]""", "GSI1", "it has no partition key")]
    [Arguments("""[|GlobalIndexKey("ab", PartitionKey = "A")|]""", "ab", "its name must be 3 to 255 of a-z, A-Z, 0-9, '_', '-' and '.'")]
    [Arguments("""[|GlobalIndexKey("bad name", PartitionKey = "A")|]""", "bad name", "its name must be 3 to 255 of a-z, A-Z, 0-9, '_', '-' and '.'")]
    [Arguments("""[|GlobalIndexKey("indéx", PartitionKey = "A")|]""", "indéx", "its name must be 3 to 255 of a-z, A-Z, 0-9, '_', '-' and '.'")]
    [Arguments("""SortKey("S"), [|LocalIndexKey("ab", "A")|]""", "ab", "its name must be 3 to 255 of a-z, A-Z, 0-9, '_', '-' and '.'")]
    [Arguments("""[|LocalIndexKey("LSI1", "A")|]""", "LSI1", "a local index needs a table with a sort key")]
    public async Task Build_InvalidIndex_ReportsDyna018OnTheAttribute(string attribute, string name, string reason)
    {
        // Arrange
        var source = $$"""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), {{attribute}}] public partial class E { }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA018 Error: Index '{name}' is invalid: {reason}"));
    }

    [Test]
    public async Task Build_IndexNameUsedTwice_ReportsDyna018OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("IDX", PartitionKey = "A"), [|LocalIndexKey("IDX", "B")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA018 Error: Index 'IDX' is invalid: the name is used twice"));
    }

    [Test]
    public async Task Build_TwoIndexesOneBuilderName_ReportsDyna018OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("my-idx", PartitionKey = "A"), [|GlobalIndexKey("my_idx", PartitionKey = "B")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA018 Error: Index 'my_idx' is invalid: its key builder 'MyIdxPk' collides with another index"));
    }

    [Test]
    public async Task Build_PlainKeyLessVisibleThanTheEntity_ReportsDyna016()
    {
        // Arrange (Keys.Of takes the value, and Keys is as public as the entity)
        const string Source = """
            internal enum Kind { A }
            [DynamoDbEntity, Discriminator("E")] public partial class E { [[|PartitionKey|]] internal Kind Id { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA016 Error: Type 'Shop.Kind' used by 'Id' is not accessible to generated code"));
    }

    [Test]
    public async Task Build_GlobalAndLocalIndexOneSortBuilderName_ReportsDyna018OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S")]
            [LocalIndexKey("my_idx", "L")]
            [[|GlobalIndexKey("my-idx", PartitionKey = "G", SortKey = "H")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert (global indexes come first, so the local one is the second)
        await Assert.That(report).Contains("its key builder 'MyIdxSk' collides with another index");
    }

    [Test]
    public async Task Build_TwoLocalIndexesOneBuilderName_ReportsDyna018OnTheSecond()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), LocalIndexKey("my-idx", "A"), [|LocalIndexKey("my_idx", "B")|]]
            public partial class E { }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA018 Error: Index 'my_idx' is invalid: its key builder 'MyIdxSk' collides with another index"));
    }

    [Test]
    public async Task Build_OutsideANullableContext_ReportsDyna019()
    {
        // Arrange
        const string Source = """
            #nullable disable
            [DynamoDbDocument] public partial class [|A|] { public string X { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA019 Error: Type 'A' must be in an enabled nullable context, otherwise nullable properties cannot be told from others"));
    }

    [Test]
    public async Task Build_AnnotationsContext_ReportsNothing()
    {
        // Arrange
        const string Source = """
            #nullable enable annotations
            [DynamoDbDocument] public partial class A { public string? X { get; set; } }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_EntityWithoutDiscriminator_ReportsDyna020()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, PartitionKey("E")] public partial class [|E|] { }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA020 Error: Entity 'E' has no discriminator. Add [Discriminator(\"E\")], or [Discriminator(OptOut = true)] for a table written by other tools."));
    }

    [Test]
    public async Task Build_PlainKeyThatIsIgnored_ReportsDyna021()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [[|PartitionKey|], DynamoDbIgnore] public string Id { get; set; } = ""; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA021 Error: Property 'Id' is a plain key, so it cannot be [DynamoDbIgnore]"));
    }

    [Test]
    [Arguments("""[|Discriminator("")|]""", "the value is empty")]
    [Arguments("""[|Discriminator|]""", "set either a value or OptOut")]
    [Arguments("""[|Discriminator("E", OptOut = true)|]""", "set either a value or OptOut")]
    public async Task Build_InvalidDiscriminator_ReportsDyna022(string attribute, string reason)
    {
        // Arrange
        var source = $$"""[DynamoDbEntity, {{attribute}}, PartitionKey("E")] public partial class E { }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA022 Error: [Discriminator] on 'E' is invalid: {reason}"));
    }
}
