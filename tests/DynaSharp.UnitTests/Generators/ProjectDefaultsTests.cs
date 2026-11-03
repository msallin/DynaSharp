using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary><c>[assembly: DynamoDbDefaults]</c>: what a project says once, as the generator reads it. Each source is a whole file, because an assembly attribute goes before the namespace.</summary>
public sealed class ProjectDefaultsTests
{
    private const string SnakeCase = "[assembly: DynamoDbDefaults(Naming = AttributeNaming.SnakeCase)]";

    private const string CamelCase = "[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase)]";

    private const string OmitEmpty = "[assembly: DynamoDbDefaults(OmitEmpty = true)]";

    private const string DiscriminatorOptOut = "[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]";

    private const string Keyed = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")]""";

    [Test]
    [Arguments(CamelCase, "orderId")]
    [Arguments(SnakeCase, "order_id")]
    [Arguments("[assembly: DynamoDbDefaults(Naming = AttributeNaming.AsDeclared)]", "OrderId")]
    [Arguments("[assembly: DynamoDbDefaults]", "OrderId")]
    [Arguments("[assembly: DynamoDbDefaults(Naming = (AttributeNaming)7)]", "OrderId")]
    [Arguments("", "OrderId")]
    public async Task Build_Naming_GivesTheAttributeOfAProperty(string defaults, string expected)
    {
        // Arrange
        var source = File(defaults, "[DynamoDbDocument] public partial class A { public int OrderId { get; set; } }");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("OrderId").AttributeName).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_NamingOnANestedDocument_ConvertsItsAttributesToo()
    {
        // Arrange
        var source = File(SnakeCase, $$"""
            {{Keyed}} public partial class E { public string Id { get; set; } = "id"; public Place? ShipTo { get; set; } }
            [DynamoDbDocument] public partial class Place { public string? ZipCode { get; set; } }
            """);

        // Act
        var names = (Analysis.ModelOfWholeFile(source, "global::Shop.E").Property("ShipTo").AttributeName, Analysis.ModelOfWholeFile(source, "global::Shop.Place").Property("ZipCode").AttributeName);

        // Assert
        await Assert.That(names).IsEqualTo(("ship_to", "zip_code"));
    }

    [Test]
    public async Task Build_NamingBesideANameOfItsOwn_TakesTheNameOfItsOwn()
    {
        // Arrange
        var source = File(SnakeCase, """[DynamoDbDocument] public partial class A { [DynamoDbAttribute("Kept")] public int OrderId { get; set; } }""");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("OrderId").AttributeName).IsEqualTo("Kept");
    }

    [Test]
    [Arguments("[PartitionKey]", "user_id")]
    [Arguments("""[PartitionKey(AttributeName = "UserId")]""", "UserId")]
    [Arguments("""[PartitionKey, DynamoDbAttribute("uid")]""", "uid")]
    public async Task Build_NamingOnAPlainKey_ConvertsWhatDerivesFromTheProperty(string attributes, string expected)
    {
        // Arrange (a plain key is the attribute of its property, which no list of frozen names holds)
        var source = File(SnakeCase, $$"""[DynamoDbEntity, Discriminator("E")] public partial class E { {{attributes}} public required string UserId { get; set; } }""");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.E");

        // Assert
        await Assert.That((model.Entity!.PartitionKey.AttributeName, model.Property("UserId").AttributeName)).IsEqualTo((expected, expected));
    }

    [Test]
    [Arguments(CamelCase)]
    [Arguments(SnakeCase)]
    public async Task Build_NamingOnTheFrozenNames_LeavesThemAlone(string defaults)
    {
        // Arrange
        var source = File(defaults, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), SortKey("S#{Id}"), GlobalIndexKey("ByAll", PartitionKey = "A#{Id}", SortKey = "B#{Id}")]
            public partial class E { public required string Id { get; set; } }
            """);

        // Act
        var entity = Analysis.ModelOfWholeFile(source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That((entity.PartitionKey.AttributeName, entity.SortKey!.AttributeName, entity.Indexes[0].PartitionKey.AttributeName, entity.Indexes[0].SortKey!.AttributeName, entity.DiscriminatorAttribute))
            .IsEqualTo(("pk", "sk", "byallpk", "byallsk", "_type"));
    }

    [Test]
    public async Task Build_TwoPropertiesThatCollideAfterTheConversion_ReportsDyna004()
    {
        // Arrange
        var source = File(SnakeCase, "[DynamoDbDocument] public partial class A { public int UserId { get; set; } public int [|UserID|] { get; set; } }");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA004 Error: Attribute name 'user_id' is used by both 'UserId' and 'UserID'"));
    }

    [Test]
    public async Task Build_PropertyThatCollidesWithAFrozenNameAfterTheConversion_ReportsDyna004()
    {
        // Arrange (the frozen name stays, and the property has to move)
        var source = File(CamelCase, """[DynamoDbEntity, Discriminator("E"), PartitionKey([|"E#{Id}"|])] public partial class E { public required string Id { get; set; } public string? Pk { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA004 Error: Attribute name 'pk' is used by both 'Pk' and 'PartitionKey'"));
    }

    [Test]
    public async Task Build_PropertyThatBecomesTheDiscriminatorAfterTheConversion_ReportsDyna010()
    {
        // Arrange
        var source = File(SnakeCase, $$"""{{Keyed}} public partial class E { public required string Id { get; set; } public string? [|_Type|] { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA010 Error: Attribute name '_type' is invalid: it is reserved for the discriminator"));
    }

    [Test]
    public async Task Build_BarePlaceholderOverTheConvertedName_IsTheAttributeOfItsProperty()
    {
        // Arrange (a number stays a number)
        var source = File(SnakeCase, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("ByAge", PartitionKey = "{OwnerAge}", PartitionKeyAttributeName = "owner_age")]
            public partial class E { public required string Id { get; set; } public int OwnerAge { get; set; } }
            """);

        // Act
        var key = Analysis.ModelOfWholeFile(source, "global::Shop.E").Entity!.Indexes[0].PartitionKey;

        // Assert
        await Assert.That((key.PlainProperty, key.WireType)).IsEqualTo(("OwnerAge", KeyWireType.Number));
    }

    [Test]
    public async Task Build_InputOfAKey_ReachesAnUpdateUnderItsConvertedName()
    {
        // Arrange (an update finds a template by the path of the handle)
        var source = File(SnakeCase, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("ByOwner", PartitionKey = "O#{OwnerName}")]
            public partial class E { public required string Id { get; set; } public required string OwnerName { get; set; } }
            """);

        // Act
        var generated = GeneratorRun.Run(source, new DynaSharpGenerator(), Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest).Result.GeneratedTrees.Single().ToString();

        // Assert
        await Assert.That(generated).Contains("""new __Map.DynamoDbKeyInput("OwnerName", "owner_name")""");
    }

    [Test]
    public async Task Build_EntityWithoutADiscriminator_OptsOutUnderTheDefault()
    {
        // Arrange
        var source = File(DiscriminatorOptOut, """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class E { public required string Id { get; set; } }""");

        // Act
        var discriminator = Analysis.ModelOfWholeFile(source, "global::Shop.E").Entity!.Discriminator;

        // Assert
        await Assert.That(discriminator).IsNull();
    }

    [Test]
    public async Task Build_EntityWithoutADiscriminator_ReportsNothingUnderTheDefault()
    {
        // Arrange
        var source = File(DiscriminatorOptOut, """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class E { public required string Id { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_EntityWithADiscriminator_KeepsItUnderTheDefault()
    {
        // Arrange
        var source = File(DiscriminatorOptOut, """[DynamoDbEntity, Discriminator("X"), PartitionKey("E#{Id}")] public partial class E { public required string Id { get; set; } }""");

        // Act
        var discriminator = Analysis.ModelOfWholeFile(source, "global::Shop.E").Entity!.Discriminator;

        // Assert
        await Assert.That(discriminator).IsEqualTo("X");
    }

    [Test]
    public async Task Build_EntityWithoutADiscriminator_ReportsDyna020WhenTheDefaultSaysNothing()
    {
        // Arrange
        var source = File(CamelCase, """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class [|E|] { public required string Id { get; set; } }""");

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(source, "DYNA020 Error: Entity 'E' has no discriminator. Add [Discriminator(\"E\")]. For a table written by other tools, opt out with [Discriminator(OptOut = true)], or with [assembly: DynamoDbDefaults(DiscriminatorOptOut = true)] for every entity of the project."));
    }

    [Test]
    [Arguments("public string X { get; set; } = \"\";", nameof(WriteCondition.WhenNotEmptyLength))]
    [Arguments("public string? X { get; set; }", nameof(WriteCondition.WhenNotEmptyLength))]
    [Arguments("public List<string> X { get; set; } = [];", nameof(WriteCondition.WhenNotEmptyCount))]
    [Arguments("public Dictionary<string, int>? X { get; set; }", nameof(WriteCondition.WhenNotEmptyCount))]
    [Arguments("public string[] X { get; set; } = [];", nameof(WriteCondition.WhenNotEmptyLength))]
    [Arguments("public int X { get; set; }", nameof(WriteCondition.Always))]
    [Arguments("public int? X { get; set; }", nameof(WriteCondition.WhenNotNull))]
    [Arguments("public required string X { get; set; }", nameof(WriteCondition.Always))]
    [Arguments("[OmitEmpty(false)] public string X { get; set; } = \"\";", nameof(WriteCondition.Always))]
    public async Task Build_OmitEmptyDefault_CoversWhatTheAttributeIsValidOn(string member, string expected)
    {
        // Arrange
        var source = File(OmitEmpty, $$"""[DynamoDbDocument] public partial class A { {{member}} }""");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That(model.Property("X").Write.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_OmitEmptyDefault_ReportsNothingForWhatItPassesOver()
    {
        // Arrange (the default says what it covers, and a type full of other things is no mistake)
        var source = File(OmitEmpty, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("ByLabel", PartitionKey = "L#{Label}", Sparse = true)]
            public partial class E
            {
                public required string Id { get; set; }

                public string? Label { get; set; }

                public required string Name { get; set; }

                public int Count { get; set; }
            }

            [DynamoDbDocument] public partial record Place(string City);
            """);

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Build_OmitEmptyDefault_LeavesAKeyInputWritten()
    {
        // Arrange
        var source = File(OmitEmpty, """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("ByLabel", PartitionKey = "L#{Label}", Sparse = true)]
            public partial class E { public required string Id { get; set; } public string? Label { get; set; } }
            """);

        // Act
        var label = Analysis.ModelOfWholeFile(source, "global::Shop.E").Property("Label");

        // Assert
        await Assert.That((label.Write, label.OmitEmptyConverter)).IsEqualTo((WriteCondition.WhenNotNull, (string?)null));
    }

    [Test]
    public async Task Build_OmitEmptyFalseOnAType_TakesTheDefaultBackForAllOfIt()
    {
        // Arrange
        var source = File(OmitEmpty, """[DynamoDbDocument, OmitEmpty(false)] public partial class A { public string X { get; set; } = ""; [OmitEmpty] public string Y { get; set; } = ""; }""");

        // Act
        var model = Analysis.ModelOfWholeFile(source, "global::Shop.A");

        // Assert
        await Assert.That((model.Property("X").Write, model.Property("Y").Write)).IsEqualTo((WriteCondition.Always, WriteCondition.WhenNotEmptyLength));
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
        """;
}
