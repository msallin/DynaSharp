namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA213 and DYNA214 on real snippets.</summary>
public sealed class PathAnalyzerTests
{
    private const string Order = """
        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        [SortKey("ORDER#{OrderId}")]
        [GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Total:n10.2}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            [KeyOnly]
            public required string OrderId { get; init; }

            public required string Status { get; init; }

            public required decimal Total { get; init; }

            public Address? ShipTo { get; init; }

            public List<string> Tags { get; init; } = [];

            [DynamoDbAttribute("ttl")]
            public long? ExpiresAt { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string Street);
        """;

    private const string Invoice = """

        [DynamoDbEntity]
        [Discriminator("INVOICE")]
        [PartitionKey("USER#{UserId}")]
        public partial class Invoice
        {
            public required string UserId { get; init; }

            public decimal Refund { get; init; }
        }
        """;

    private const string Mine = "public static class Mine { public static void Set(string path) { } public static void Other(string path) { } }";

    private const string MyConverter = """

        public readonly struct MyConverter : IDynamoDbConverter<Address>
        {
            public static void Write(DynamoDbWriter writer, Address value) { }
            public static Address Read(ref DynamoDbReader reader, AttributeType type) => default!;
        }
        """;

    private const string Fields = """
        private static readonly Table<Order> orders = null!;
        private static UpdateCommand<Order> update => default;
        private static readonly Table<Address> addresses = null!;

        """;

    private static readonly AnalyzerFixture Orders = new(Order);

    private static readonly AnalyzerFixture WithMine = new(Mine);

    [Test]
    [Arguments("""shop.Update("a").Set([|"a..b"|], 1);""", "a..b")]
    [Arguments("""shop.Update("a").Set([|"a["|], "x");""", "a[")]
    [Arguments("""shop.Update("a").SetIfNotExists([|"a."|], 1);""", "a.")]
    [Arguments("""shop.Update("a").Add([|".a"|], 1);""", ".a")]
    [Arguments("""shop.Update("a").Delete([|"a[x]"|], AttributeValue.Null);""", "a[x]")]
    [Arguments("""shop.Update("a").Remove([|"a[1]b"|]);""", "a[1]b")]
    [Arguments("""shop.Get("a").Project([|"a..b"|]);""", "a..b")]
    [Arguments("""shop.Get("a").Project("ok", [|"a..b"|], "fine");""", "a..b")]
    [Arguments("""shop.Scan().Project(["ok", [|"a[]"|]]);""", "a[]")]
    [Arguments("""shop.Query("a").Project(new[] { [|"[0]"|] });""", "[0]")]
    [Arguments("""Attr([|"a..b"|]);""", "a..b")]
    [Arguments("""Attr<int>([|"a..b"|]);""", "a..b")]
    [Arguments("""new Attr([|"a..b"|]);""", "a..b")]
    [Arguments("""shop.Update("a").Remove([|""|]);""", "")]
    public async Task Analyze_InvalidPath_ReportsTheString(string statement, string path)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA214 Warning: Attribute path '{path}' is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c"));
    }

    [Test]
    public async Task Analyze_InvalidNamedConstant_ReportsTheName()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            """
            private const string Path = "a..b";
            public static void Run() => shop.Update("a").Remove([|Path|]);
            """);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA214 Warning: Attribute path 'a..b' is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c"));
    }

    [Test]
    [Arguments("""shop.Update("a").Set("status", 1);""")]
    [Arguments("""shop.Update("a").Set("address.city", "x");""")]
    [Arguments("""shop.Update("a").Remove("lines[2].sku");""")]
    [Arguments("""shop.Update("a").Remove("matrix[0][1]");""")]
    [Arguments("""shop.Update("a").Set("name with spaces", 1);""")]
    [Arguments("""var path = "a..b"; shop.Update("a").Remove(path);""")]
    [Arguments("""shop.Update("a").Remove(Attr("a"));""")]
    [Arguments("""string[] paths = ["a..b"]; shop.Get("a").Project(paths);""")]
    [Arguments("""shop.Get("a").Project(new string[1]);""")]
    [Arguments("""shop.Put().With("a..b", 1);""")]
    [Arguments("""shop.Index("a..b", "pk");""")]
    [Arguments("""Mine.Set("a..b");""")]
    [Arguments("""Mine.Other(path: "a..b");""")]
    [Arguments("""System.IO.Path.GetFileName(path: "a..b");""")]
    public async Task Analyze_ValidOrNotConstant_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement);

        // Act
        var report = await WithMine.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_VarargsCall_DoesNotThrow()
    {
        // Arrange: an argument of __arglist has no parameter.
        var source = AnalyzerRun.Statements("""Mine.Arguments(__arglist("a..b"));""", "public static class Mine { public static void Arguments(__arglist) { } }");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""update.Set([|"Totl"|], 1);""", "Totl")]
    [Arguments("""update.Add([|"Totl"|], 1);""", "Totl")]
    [Arguments("""update.Remove([|"Totl"|]);""", "Totl")]
    [Arguments("""update.Set([|"Totl.x"|], 1);""", "Totl.x")]
    [Arguments("""update.Set([|"Tags2[1]"|], 1);""", "Tags2[1]")]
    [Arguments("""update.Set([|"ExpiresAt"|], 1);""", "ExpiresAt")]
    [Arguments("""update.Set([|nameof(Order.ExpiresAt)|], 1);""", "ExpiresAt")]
    [Arguments("""update.Remove([|"OrderId"|]);""", "OrderId")]
    [Arguments("""orders.Scan().Project("Total", [|"Totl"|]);""", "Totl")]
    public async Task Analyze_PathOnATypedCommandThatMatchesNoAttribute_Warns(string statement, string path)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run() { " + statement + " }");

        // Act
        var report = await Orders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA213 Warning: Path '{path}' matches no attribute of 'Order'. Check the spelling, unless the attribute is outside the entity."));
    }

    [Test]
    [Arguments("""update.Set("Total", 1);""")]
    [Arguments("""update.Set("Status", "x");""")]
    [Arguments("""update.Set("ttl", 1);""")]
    [Arguments("""update.Set("ShipTo.Stret", "x");""")]
    [Arguments("""update.Remove("Tags[1]");""")]
    [Arguments("""update.Set(nameof(Order.Total), 1);""")]
    [Arguments("""update.Remove("pk");""")]
    [Arguments("""update.Remove("sk");""")]
    [Arguments("""update.Remove("_type");""")]
    [Arguments("""update.Remove("gsi1pk");""")]
    [Arguments("""update.Remove("gsi1sk");""")]
    [Arguments("""var name = "Totl"; update.Set(name, 1);""")]
    [Arguments("""orders.Scan().Project("Total", "Status");""")]
    [Arguments("""addresses.Scan().Project("Street");""")]
    [Arguments("""shop.Update("a").Set("Totl", 1);""")]
    public async Task Analyze_PathThatMatchesOrCannotBeChecked_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run() { " + statement + " }");

        // Act
        var report = await Orders.ReportAsync(source);
        var errors = Orders.CompilerErrors(source);

        // Assert
        await Assert.That(errors).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_InvalidPathOnATransactionStep_ReportsTheString()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run(DynamoDbClient client) { client.TransactWrite().Update(shop, "a").Set([|"a..b"|], 1); }""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA214 Warning: Attribute path 'a..b' is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c"));
    }

    [Test]
    public async Task Analyze_RawPathOnATransactionStep_HasNoEntityToCheck()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run(DynamoDbClient client) { client.TransactWrite().Update(shop, "a").Set("anything", 1); }""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""update.Set([|"ShipTo"|], 1);""", "ShipTo")]
    [Arguments("""orders.Scan().Project([|"Total"|]);""", "Total")]
    public async Task Analyze_DeclaredNameUnderANaming_Warns(string statement, string path)
    {
        // Arrange (the attribute has another name now, so this looks for what is not there)
        var source = Named(statement);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA213 Warning: Path '{path}' matches no attribute of 'Order'. Check the spelling, unless the attribute is outside the entity."));
    }

    [Test]
    [Arguments("""update.Set("ship_to", 1);""")]
    [Arguments("""update.Set("ship_to.street", "x");""")]
    [Arguments("""orders.Scan().Project("total");""")]
    [Arguments("""update.Set("ttl", 1);""")]
    [Arguments("""update.Set("pk", "x");""")]
    public async Task Analyze_ConvertedNameUnderANaming_ReportsNothing(string statement)
    {
        // Arrange (a name of its own and a frozen name stay what they are)
        var source = Named(statement);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_DeclaredNameOfATypeFromMetadata_Warns()
    {
        // Arrange (the type was built with defaults of its own, and they are what its items hold, whatever this compilation says)
        var source = Fixture("""guests.Scan().Project([|"GuestName"|]);""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA213 Warning: Path 'GuestName' matches no attribute of 'Guest'. Check the spelling, unless the attribute is outside the entity."));
    }

    [Test]
    public async Task Analyze_ConvertedNameOfATypeFromMetadata_ReportsNothing()
    {
        // Arrange
        var source = Fixture("""guests.Scan().Project("guest_name");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_DocumentWithoutTheAttribute_Warns()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { addresses.Scan().Project([|"Stret"|]); }""");

        // Act
        var report = await Orders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA213 Warning: Path 'Stret' matches no attribute of 'Address'. Check the spelling, unless the attribute is outside the entity."));
    }

    [Test]
    public async Task Analyze_MixedReadWithThePathOnItsLastType_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run() { shop.Query("a").Of<Order, Invoice>().Project("Status", "Refund"); }""", Order + Invoice);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source));
    }

    [Test]
    public async Task Analyze_MixedReadWithoutThePathOnAnyType_WarnsWithTheFirstType()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run() { shop.Query("a").Of<Order, Invoice>().Project([|"Nope"|]); }""", Order + Invoice);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA213 Warning: Path 'Nope' matches no attribute of 'Order'. Check the spelling, unless the attribute is outside the entity."));
    }

    [Test]
    public async Task Analyze_InvalidPathOnATypedCommand_IsOnlyTheSyntaxError()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { update.Set([|"Totl..x"|], 1); }""");

        // Act
        var report = await Orders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA214 Warning: Attribute path 'Totl..x' is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c"));
    }

    [Test]
    public async Task Analyze_HelperOverAnyCommand_ReportsNothing()
    {
        // Arrange: the interface knows no entity, and neither does a generic type.
        var source = AnalyzerRun.Member(
            """
            public static TCommand Audited<TCommand>(this TCommand command)
                where TCommand : IUpdatable<TCommand>, allows ref struct => command.Set("updatedBy", "me");

            public static UpdateCommand<T> Stamped<T>(this UpdateCommand<T> command)
                where T : IDynamoDbDocument<T> => command.Set("updatedAt", 1);
            """);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_TypeWithErrors_ReportsNothing()
    {
        // Arrange: no model means nothing to compare with, and the generator reports the type.
        var source = AnalyzerRun.Member(
            """private static readonly Table<Broken> broken = null!; public static void Run() { broken.Scan().Project("Anything"); }""",
            "[DynamoDbEntity]\npublic partial class Broken\n{\n    public string Id { get; set; } = \"\";\n}\n");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_AttributeTypeOfAnAttrHandle_IsNotTheEntity()
    {
        // Arrange: the first type argument of Attr<T, TConverter> is the type of the value, which can be mapped too.
        var source = AnalyzerRun.Member(
            """private static readonly Attr<Address, MyConverter> Handle = new("shipTo");""",
            Order + MyConverter);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Named(string statement) =>
        AnalyzerRun.Member(Fields + "public static void Run() { " + statement + " }", Order)
            .Replace("namespace Shop;", "[assembly: DynamoDbDefaults(Naming = AttributeNaming.SnakeCase)]\n\nnamespace Shop;", StringComparison.Ordinal);

    private static string Fixture(string statement) =>
        AnalyzerRun.Member("private static readonly Table<Guest> guests = null!;\npublic static void Run() { " + statement + " }")
            .Replace("namespace Shop;", "using DynaSharp.DefaultsFixture;\n\nnamespace Shop;", StringComparison.Ordinal);
}
