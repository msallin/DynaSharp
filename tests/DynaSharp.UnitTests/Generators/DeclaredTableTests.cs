using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The table an entity declares, with <c>[DynamoDbEntity(Table = ...)]</c> or <c>[assembly: DynamoDbDefaults(Table = ...)]</c>, as the generator reads, checks and emits it: DYNA039 and DYNA040.</summary>
public sealed class DeclaredTableTests
{
    private const string Order = """[DynamoDbEntity(Table = "orders"), Discriminator("ORDER"), PartitionKey("ORDER#{Id}"), SortKey("ORDER")] public partial class Order { public string Id { get; set; } = "id"; }""";

    private const string Plain = """[DynamoDbEntity(Table = "plain"), Discriminator("PLAIN")] public partial class Plain { [PartitionKey] public string Id { get; set; } = "id"; }""";

    private const string Undeclared = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; }""";

    private const string AppData = "[assembly: DynamoDbDefaults(Table = \"appdata\")]";

    [Test]
    public async Task Build_TableOnTheEntity_IsTheTableOfTheModel()
    {
        // Arrange, Act
        var model = Analysis.Model(Order, "global::Shop.Order");

        // Assert
        await Assert.That(model.Entity!.TableName).IsEqualTo("orders");
    }

    [Test]
    public async Task Build_NoTableAnywhere_LeavesTheTableOfTheModelNull()
    {
        // Arrange, Act
        var model = Analysis.Model(Undeclared, "global::Shop.E");

        // Assert
        await Assert.That(model.Entity!.TableName).IsNull();
    }

    [Test]
    public async Task Build_TableOfTheAssembly_IsTheTableOfAnEntityWithoutItsOwn()
    {
        // Arrange
        var source = File(AppData, Undeclared);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.E");

        // Assert
        await Assert.That(model.Entity!.TableName).IsEqualTo("appdata");
    }

    [Test]
    public async Task Build_TableOfTheEntityBesideTheTableOfTheAssembly_WinsOverIt()
    {
        // Arrange
        var source = File(AppData, Order);

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.Order");

        // Assert
        await Assert.That(model.Entity!.TableName).IsEqualTo("orders");
    }

    [Test]
    [Arguments("", "it is empty")]
    [Arguments("ab", "it has 2 characters, and a table name has 3 to 255")]
    [Arguments("my table", "' ' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("tablé", "'é' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab!", "'!' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab,", "',' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab/", "'/' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab:", "':' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab@", "'@' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab[", "'[' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab^", "'^' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab`", "'`' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    [Arguments("ab{", "'{' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'")]
    public async Task Build_InvalidTableOnTheEntity_ReportsDyna039OnTheArgument(string name, string reason)
    {
        // Arrange
        var source = $$"""[DynamoDbEntity(Table = [|"{{name}}"|]), Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA039 Error: The table name '{name}' is not valid: {reason}"));
    }

    [Test]
    public async Task Build_TableLongerThanTheLimit_ReportsDyna039()
    {
        // Arrange
        var name = new string('a', 256);
        var source = $$"""[DynamoDbEntity(Table = [|"{{name}}"|]), Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(source, $"DYNA039 Error: The table name '{name}' is not valid: it has 256 characters, and a table name has 3 to 255"));
    }

    [Test]
    [Arguments("abc")]
    [Arguments("App_Data-2.v1")]
    [Arguments("azAZ09_-.")]
    public async Task Build_ValidTable_ReportsNothing(string name)
    {
        // Arrange
        var source = $$"""[DynamoDbEntity(Table = "{{name}}"), Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_TableAtTheLimit_ReportsNothing()
    {
        // Arrange
        var source = $$"""[DynamoDbEntity(Table = "{{new string('a', 255)}}"), Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; }""";

        // Act
        var report = Analysis.Report(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_InvalidTableOnTheEntity_GeneratesNoCode()
    {
        // Arrange
        const string Source = """using DynaSharp.Mapping; namespace Shop; [DynamoDbEntity(Table = "x"), Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public string Id { get; set; } = "id"; }""";

        // Act
        var result = GeneratorRun.Generate(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(result.Results[0].GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task Build_InvalidTableOfTheAssembly_ReportsDyna039OnTheArgumentOnce()
    {
        // Arrange
        var source = File("[assembly: DynamoDbDefaults(Table = [|\"a b\"|])]", Undeclared + "\n" + Undeclared.Replace("class E", "class F", StringComparison.Ordinal).Replace("\"E\"", "\"F\"", StringComparison.Ordinal));

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA039 Error: The table name 'a b' is not valid: ' ' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'"));
    }

    [Test]
    public async Task Build_TableNullOnTheEntity_TakesTheTableOfTheAssembly()
    {
        // Arrange
        var source = File(AppData, Undeclared.Replace("[DynamoDbEntity,", "[DynamoDbEntity(Table = null),", StringComparison.Ordinal));

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.E");

        // Assert
        await Assert.That(model.Entity!.TableName).IsEqualTo("appdata");
    }

    [Test]
    [Arguments(Order, "Shop.Order.g.cs", "}) { TableName = \"orders\" };")]
    [Arguments(Plain, "Shop.Plain.g.cs", "discriminatorAttributeName: \"_type\") { TableName = \"plain\" };")]
    public async Task Run_DeclaredTable_IsATablePropertyOfTheInfo(string entity, string file, string line)
    {
        // Arrange
        var source = "using DynaSharp.Mapping;\nnamespace Shop;\n" + entity;

        // Act
        var generated = GeneratorRun.Generate(source, new DynaSharpGenerator(), LanguageVersion.Latest).Results[0].GeneratedSources.Single(generated => generated.HintName == file).SourceText.ToString();

        // Assert
        await Assert.That(generated).Contains(line);
    }

    [Test]
    public async Task Run_DeclaredTables_CompileWithoutWarnings()
    {
        // Arrange
        var source = "using DynaSharp.Mapping;\nnamespace Shop;\n" + Order + "\n" + Plain;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.CSharp11);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Run_TwoEntitiesOfOneTableWithOtherKeys_ReportsDyna040OnTheEntityThatDiffers()
    {
        // Arrange
        var source = File(AppData, """
            [DynamoDbEntity, Discriminator("CART"), PartitionKey("CART#{Id}"), SortKey("CART")] public partial class Cart { public string Id { get; set; } = "id"; }
            [DynamoDbEntity, Discriminator("LINE"), PartitionKey("LINE#{Id}"), SortKey("LINE")] public partial class Line { public string Id { get; set; } = "id"; }
            [[|DynamoDbEntity|], Discriminator("SESSION"), PartitionKey("SESSION#{Id}", AttributeName = "id")] public partial class Session { public string Id { get; set; } = "id"; }
            """);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(
            source,
            "DYNA040 Warning: 'Shop.Session' is in the table 'appdata' with the keys 'id', and 'Shop.Cart' is in it with 'pk' and 'sk'. A table has one key schema: name their key attributes the same, or put one of them in a table of its own.")
            + "\n" + Dyna037("'pk' and 'sk' of Shop.Cart, 'id' of Shop.Session"));
    }

    [Test]
    public async Task Run_TwoEntitiesOfOneTableTiedOnTheirKeys_ReportsTheSecondByName()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity(Table = "shared"), Discriminator("A"), PartitionKey("A#{Id}", AttributeName = "id")] public partial class A { public string Id { get; set; } = "id"; }
            [[|DynamoDbEntity(Table = "shared")|], Discriminator("B"), PartitionKey("B#{Id}")] public partial class B { public string Id { get; set; } = "id"; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(
            Source,
            "DYNA040 Warning: 'Shop.B' is in the table 'shared' with the keys 'pk', and 'Shop.A' is in it with 'id'. A table has one key schema: name their key attributes the same, or put one of them in a table of its own.")
            + "\n" + Dyna037("'id' of Shop.A, 'pk' of Shop.B"));
    }

    [Test]
    public async Task Run_EntitiesOfOtherKeysInTablesOfTheirOwn_ReportOnlyDyna037()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity(Table = "one"), Discriminator("A"), PartitionKey("A#{Id}", AttributeName = "id")] public partial class A { public string Id { get; set; } = "id"; }
            [DynamoDbEntity(Table = "two"), Discriminator("B"), PartitionKey("B#{Id}")] public partial class B { public string Id { get; set; } = "id"; }
            [DynamoDbEntity, Discriminator("C"), PartitionKey("C#{Id}", AttributeName = "cid")] public partial class C { public string Id { get; set; } = "id"; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Dyna037("'id' of Shop.A, 'pk' of Shop.B, 'cid' of Shop.C"));
    }

    [Test]
    public async Task Run_NestedEntityOfAPrivateTypeWithOtherKeys_ReportsDyna040BesideDyna037()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity(Table = "shared"), Discriminator("A"), PartitionKey("A#{Id}"), SortKey("A")] public partial class A { public string Id { get; set; } = "id"; }
            [DynamoDbEntity(Table = "shared"), Discriminator("B"), PartitionKey("B#{Id}"), SortKey("B")] public partial class B { public string Id { get; set; } = "id"; }
            public partial class Tests
            {
                [[|DynamoDbEntity(Table = "shared")|], Discriminator("ROW"), PartitionKey("ROW#{Id}")]
                private partial class Row { public string Id { get; set; } = "id"; }
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(
            Source,
            "DYNA040 Warning: 'Shop.Tests.Row' is in the table 'shared' with the keys 'pk', and 'Shop.A' is in it with 'pk' and 'sk'. A table has one key schema: name their key attributes the same, or put one of them in a table of its own.")
            + "\nDYNA037 Info [0..0]: No IncludingAll() is generated for the entities of this assembly: 'Shop.Tests.Row' is private to a type around it, so no class outside that type can name it. Name the entities of a table with Including<T>().");
    }

    [Test]
    public async Task EntityAttributeOf_TypeThatIsNotThereOrHasNoAttribute_IsNone()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation("namespace Shop; public class Plain { }", LanguageVersion.Latest);

        // Act
        var locations = (DiagnosticReporter.EntityAttributeOf(compilation, "Shop.Missing"), DiagnosticReporter.EntityAttributeOf(compilation, "Shop.Plain"));

        // Assert
        await Assert.That(locations).IsEqualTo((Location.None, Location.None));
    }

    private static string Dyna037(string keys) =>
        $"DYNA037 Info [0..0]: No IncludingAll() is generated for the entities of this assembly: they declare the keys of more than one table, {keys}. Name the entities of a table with Including<T>().";

    private static string File(string defaults, string members) => $$"""
        using System;
        using DynaSharp.Mapping;

        {{defaults}}

        namespace Shop;

        {{members}}
        """;
}
