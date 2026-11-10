using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TUnit.Assertions.Enums;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// The fixes of the analyzers that carry their edits, applied in a real workspace to the diagnostics the analyzer reports on the same text, and analyzed and compiled again.
/// </summary>
public sealed class SuggestedEditCodeFixTests
{
    private const string Model = """
        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        [SortKey("ORDER#{OrderId}")]
        [GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "ORDER#{OrderId}")]
        [GlobalIndexKey("ByUser", PartitionKey = "USER#{UserId}", SortKey = "AT#{PlacedAt}")]
        [GlobalIndexKey("ByMarket", PartitionKey = "MARKET#{Market}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            public required string OrderId { get; init; }

            public required string Status { get; init; }

            public required string Market { get; init; }

            public DateTimeOffset PlacedAt { get; init; }

            public decimal Total { get; init; }

            public string Name { get; init; } = "";

            [DynamoDbAttribute("ship-to")]
            public string? ShipTo { get; init; }

            [DynamoDbAttribute("1st")]
            public string? First { get; init; }

            public string? Markets { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("CART")]
        [PartitionKey("CART#{Id}")]
        public partial record Cart
        {
            public required string Id { get; init; }

            [Version]
            public long Revision { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("BADGE")]
        [PartitionKey("BADGE#{Id}")]
        [GlobalIndexKey("ByCode", PartitionKey = "{Code}", PartitionKeyAttributeName = "code")]
        public partial class Badge
        {
            public required string Id { get; init; }

            [KeyOnly]
            public required string Code { get; init; }

            public int Level { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TAG")]
        [PartitionKey("TAG#{Id}")]
        public partial class Tag
        {
            public required string Id { get; init; }

            [DynamoDbAttribute("a.b")]
            public required string Dotted { get; init; }

            public int Level { get; init; }
        }

        """;

    private const string Fields = """
        private static readonly DynamoDbClient client = null!;
        private static readonly Table<Order> orders = null!;
        private static readonly Table<Cart> carts = null!;
        private static readonly Table<Badge> badges = null!;
        private static readonly Table<Tag> tags = null!;
        private static readonly Cart cart = null!;
        private static readonly Order order = null!;
        private static readonly Order.Keys.PrimaryKey key = default;
        private static bool Flag => true;

        """;

    private static readonly AnalyzerFixture Fixture = new(Model);

    [Test]
    public async Task FixableDiagnosticIds_AreTheRulesThatCarryEdits()
    {
        // Arrange
        var fix = new SuggestedEditCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA204", "DYNA206", "DYNA207", "DYNA208", "DYNA213", "DYNA224", "DYNA227", "DYNA233", "DYNA235"]);
    }

    [Test]
    public async Task GetFixAllProvider_FixesAllInOneGo()
    {
        // Arrange
        var fix = new SuggestedEditCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsEqualTo(WellKnownFixAllProviders.BatchFixer);
    }

    [Test]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); q.Limit(1); _ = q.ToRequestJson();""", "q", """var q = orders.Query(Order.Keys.Pk.Of("u")); q = q.Limit(1); _ = q.ToRequestJson();""")]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); _ = q.Limit(1).Descending(); _ = q.ToRequestJson();""", "q", """var q = orders.Query(Order.Keys.Pk.Of("u")); q = q.Limit(1).Descending(); _ = q.ToRequestJson();""")]
    [Arguments("""var @event = orders.Query(Order.Keys.Pk.Of("u")); @event.Limit(1); if (Flag) { _ = @event.ToRequestJson(); }""", "@event", """var @event = orders.Query(Order.Keys.Pk.Of("u")); @event = @event.Limit(1); if (Flag) { _ = @event.ToRequestJson(); }""")]
    public async Task Fix_CommandReusedAfterACallWhoseResultIsDropped_AssignsItBack(string statements, string variable, string expected)
    {
        // Arrange
        var source = Run(statements);

        // Act
        var fixes = await FixesAsync(source, "DYNA204", new CommandReuseAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([("Assign the result back to " + variable, Run(expected))]);
    }

    [Test]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); var a = q.Limit(1); _ = q.ToRequestJson();""")]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); var r = q; r = q.Limit(1); _ = q.ToRequestJson();""")]
    [Arguments("""var c = carts.Put(cart); _ = c.ToRequestJson(); _ = c.ToRequestJson();""")]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); if (Flag) { q.Limit(1); } _ = q.ToRequestJson();""")]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); if (Flag) q.Limit(1); _ = q.ToRequestJson();""")]
    [Arguments("""var q = orders.Query(Order.Keys.Pk.Of("u")); q.Limit(1); Keep(q); _ = q.ToRequestJson();""")]
    public async Task Fix_CommandReusedWhereNoResultCanBeAssignedBack_OffersNothing(string statements)
    {
        // Arrange (a result kept in another variable, a call that gives no command, a call in a block the reuse is not in, and a mention between the two)
        var source = Run(statements);

        // Act
        var fixes = await FixesAsync(source, "DYNA204", new CommandReuseAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    [Arguments("public static void Run(QueryCommand<Order> q) { for (var i = 0; i < 2; i++) { _ = q.ToRequestJson(); } }")]
    [Arguments("public static void Run(in QueryCommand<Order> q) { q.Limit(1); _ = q.ToRequestJson(); }")]
    public async Task Fix_ParameterThatCannotTakeTheResult_OffersNothing(string member)
    {
        // Arrange (a reuse around a loop has no earlier call, and an in parameter cannot be assigned)
        var source = AnalyzerRun.Member(Fields + member);

        // Act
        var fixes = await FixesAsync(source, "DYNA204", new CommandReuseAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    public async Task Fix_ParameterReusedAfterACallWhoseResultIsDropped_AssignsItBack()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run(QueryCommand<Order> q) { q.Limit(1); _ = q.ToRequestJson(); }");

        // Act
        var fixes = await FixesAsync(source, "DYNA204", new CommandReuseAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([("Assign the result back to q", AnalyzerRun.Member(Fields + "public static void Run(QueryCommand<Order> q) { q = q.Limit(1); _ = q.ToRequestJson(); }"))]);
    }

    [Test]
    public async Task Fix_CommandReused_LeavesNoDiagnosticAndCompiles()
    {
        // Arrange
        var source = Run("""var q = orders.Query(Order.Keys.Pk.Of("u")); q.Limit(1); _ = q.ToRequestJson();""");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA204", new CommandReuseAnalyzer())).Single().Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new CommandReuseAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    public async Task Fix_IfNotExistsOnAPutWithAVersion_SetsItTo0OrIgnoresIt()
    {
        // Arrange
        var source = Run("""_ = carts.Put(new Cart { Id = "a", Revision = 3 }).IfNotExists();""");

        // Act
        var fixes = await FixesAsync(source, "DYNA206", new EntityCommandAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo(
        [
            ("Set Revision = 0", Run("""_ = carts.Put(new Cart { Id = "a", Revision = 0 }).IfNotExists();""")),
            ("Add IgnoreVersion()", Run("""_ = carts.Put(new Cart { Id = "a", Revision = 3 }).IfNotExists().IgnoreVersion();""")),
        ]);
    }

    [Test]
    public async Task Fix_IfNotExistsOnALineOfItsOwnWithCrLf_PutsIgnoreVersionOnTheNextLine()
    {
        // Arrange
        var source = CrLf(Run("_ = carts.Put(cart with { Revision = 1 })\n    .IfNotExists()\n    .ReturnOld();"));

        // Act
        var fixes = await FixesAsync(source, "DYNA206", new EntityCommandAnalyzer());

        // Assert
        await Assert.That(fixes[1].Text).IsEqualTo(CrLf(Run("_ = carts.Put(cart with { Revision = 1 })\n    .IfNotExists()\n    .IgnoreVersion()\n    .ReturnOld();")));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Fix_IfNotExistsOnAPutWithAVersion_LeavesNoDiagnosticAndCompiles(int rank)
    {
        // Arrange
        var source = Run("""_ = client.TransactWrite().Put(carts, cart with { Revision = 2 }).IfNotExists();""");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA206", new EntityCommandAnalyzer()))[rank].Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new EntityCommandAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    public async Task Fix_IndexThatIsNotDeclared_OffersEachIndexTheClosestFirst()
    {
        // Arrange
        var source = Run("""_ = orders.Index("ByStatuz");""");
        (string, string)[] expected =
        [
            ("Use Order.Indexes.ByStatus", Run("_ = orders.Index(Order.Indexes.ByStatus);")),
            ("Use Order.Indexes.ByMarket", Run("_ = orders.Index(Order.Indexes.ByMarket);")),
            ("Use Order.Indexes.ByUser", Run("_ = orders.Index(Order.Indexes.ByUser);")),
        ];

        // Act
        var fixes = await FixesAsync(source, "DYNA207", new EntityCommandAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fix_IndexOfAnEntityWithoutIndexes_OffersNothing()
    {
        // Arrange
        var source = Run("""_ = carts.Index("ByStatus");""");

        // Act
        var fixes = await FixesAsync(source, "DYNA207", new EntityCommandAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    public async Task Fix_IndexThatIsNotDeclared_LeavesNoDiagnosticAndCompiles()
    {
        // Arrange
        var source = Run("""_ = orders.Index("bystatus");""");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA207", new EntityCommandAnalyzer()))[0].Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new EntityCommandAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    [Arguments("""Project("Total")""", "\"Status\", \"Market\"", """Project("Total", "Status", "Market")""")]
    [Arguments("""Project(["Total"])""", "\"Status\", \"Market\"", """Project(["Total", "Status", "Market"])""")]
    [Arguments("""Project(Order.Attrs.Total)""", "Order.Attrs.Status, Order.Attrs.Market", """Project(Order.Attrs.Total, Order.Attrs.Status, Order.Attrs.Market)""")]
    public async Task Fix_ProjectThatLeavesOutRequiredMembers_AddsThemInItsForm(string project, string added, string expected)
    {
        // Arrange
        var source = Run("_ = orders.Query(Order.Keys.Pk.Of(\"u\"))." + project + ".ItemsAsync();");

        // Act
        var fixes = await FixesAsync(source, "DYNA208", new ProjectionAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([("Add " + added + " to Project", Run("_ = orders.Query(Order.Keys.Pk.Of(\"u\"))." + expected + ".ItemsAsync();"))]);
    }

    [Test]
    public async Task Fix_ProjectThatLeavesOutAMemberAKeyRestores_AddsTheKey()
    {
        // Arrange
        var source = Run("""_ = badges.Query(Badge.Keys.Pk.Of("b")).Project("Level").ItemsAsync();""");

        // Act
        var fixes = await FixesAsync(source, "DYNA208", new ProjectionAnalyzer());

        // Assert
        await Assert.That(fixes.Single().Text).IsEqualTo(Run("""_ = badges.Query(Badge.Keys.Pk.Of("b")).Project("Level", "code").ItemsAsync();"""));
    }

    [Test]
    [Arguments("""_ = badges.Query(Badge.Keys.Pk.Of("b")).Project(Badge.Attrs.Level).ItemsAsync();""")]
    [Arguments("""_ = tags.Query(Tag.Keys.Pk.Of("t")).Project("Level").ItemsAsync();""")]
    [Arguments("""_ = tags.Query(Tag.Keys.Pk.Of("t")).Project(Tag.Attrs.Level).ItemsAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("u")).Project("Total").Project(new string[] { }).ItemsAsync();""")]
    public async Task Fix_ProjectOfAMemberNoPathReaches_OffersNothing(string statement)
    {
        // Arrange (a member a key restores has no handle, an attribute with a dot is no path, and an empty Project has no place to add to)
        var source = Run(statement);

        // Act
        var fixes = await FixesAsync(source, "DYNA208", new ProjectionAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    public async Task Fix_ProjectThatLeavesOutRequiredMembers_LeavesNoDiagnosticAndCompiles()
    {
        // Arrange
        var source = Run("""_ = orders.Get(key).Project(Order.Attrs.Total).ItemAsync();""");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA208", new ProjectionAnalyzer())).Single().Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new ProjectionAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    [Arguments("""_ = orders.Update(key).Set("Totl", 1m);""", "Use \"Total\"", """_ = orders.Update(key).Set("Total", 1m);""")]
    [Arguments("""_ = orders.Update(key).Remove("shipto.street");""", "Use \"ship-to.street\"", """_ = orders.Update(key).Remove("ship-to.street");""")]
    [Arguments("""_ = orders.Scan().Filter(Attr("Totl").Gt(1m)).ToRequestJson();""", "Use \"Total\"", """_ = orders.Scan().Filter(Attr("Total").Gt(1m)).ToRequestJson();""")]
    public async Task Fix_PathOfAStringLiteral_OffersTheAttributeOfAClosSpelling(string statement, string title, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixes = await FixesAsync(source, "DYNA213", new PathAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([(title, Run(expected))]);
    }

    [Test]
    public async Task Fix_PathOfAConstant_OffersNothing()
    {
        // Arrange (the constant may be used elsewhere)
        var source = Run("""const string Path = "Totl"; _ = orders.Update(key).Set(Path, 1m);""");

        // Act
        var fixes = await FixesAsync(source, "DYNA213", new PathAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    public async Task Fix_BareNameInTheText_OffersTheNamesThatStandBare()
    {
        // Arrange (Market is one letter off, Markets two; ship-to, 1st and the reserved Total cannot stand bare)
        var source = Run("""_ = orders.Scan().Filter($"Markt = {"eu"} OR shipto = {1} OR ist = {1} OR Totl = {1}");""");

        // Act
        var titles = new List<string>();
        foreach (var diagnostic in await Fixture.DiagnosticsWithAsync(source, new ExpressionAnalyzer()))
        {
            titles.AddRange((await ActionsAsync(source, diagnostic)).Select(action => action.Title));
        }

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Use Market"]);
    }

    [Test]
    public async Task Fix_BareNameInTheText_LeavesNoDiagnosticAndCompiles()
    {
        // Arrange
        var source = Run("""_ = orders.Scan().Filter($"Markt = {"eu"}");""");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA213", new ExpressionAnalyzer()))[0].Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new ExpressionAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    [Arguments("""_ = orders.Index(Order.Indexes.ByUser).Query(Order.Keys.Pk.Of("u"));""", "Use Order.Keys.ByUserPk", """_ = orders.Index(Order.Indexes.ByUser).Query(Order.Keys.ByUserPk.Of("u"));""")]
    [Arguments("""_ = orders.Index(Order.Indexes.ByUser).Query(new[] { "u" }.Select(Order.Keys.Pk.Of));""", "Use Order.Keys.ByUserPk", """_ = orders.Index(Order.Indexes.ByUser).Query(new[] { "u" }.Select(Order.Keys.ByUserPk.Of));""")]
    [Arguments("""_ = orders.Index(Order.Indexes.ByUser).Query(Order.Keys.ByUserPk.Of("u"), Order.Keys.Sk.BeginsWith());""", "Use Order.Keys.ByUserSk", """_ = orders.Index(Order.Indexes.ByUser).Query(Order.Keys.ByUserPk.Of("u"), Order.Keys.ByUserSk.BeginsWith());""")]
    [Arguments("""var byUser = orders.Index(Order.Indexes.ByUser); _ = byUser.Query(Order.Keys.Pk.Of("u"));""", "Use Order.Keys.ByUserPk", """var byUser = orders.Index(Order.Indexes.ByUser); _ = byUser.Query(Order.Keys.ByUserPk.Of("u"));""")]
    public async Task Fix_KeyOfAnotherAttribute_UsesTheBuilderOfTheRead(string statement, string title, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixes = await FixesAsync(source, "DYNA224", new QueryKeyAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([(title, Run(expected))]);
    }

    [Test]
    [Arguments("""_ = orders.Index(Order.Indexes.ByStatus).Query(Order.Keys.Pk.Of("u"));""")]
    [Arguments("""_ = orders.Index(Order.Indexes.ByMarket).Query(Order.Keys.ByMarketPk.Of("m"), Order.Keys.Sk.BeginsWith());""")]
    [Arguments("""_ = orders.Index("Other", "otherpk").Query(Order.Keys.Pk.Of("u"));""")]
    public async Task Fix_KeyOfAnotherAttributeWithoutABuilderThatTakesTheSameArguments_OffersNothing(string statement)
    {
        // Arrange (ByStatusPk takes a status, an index without a sort key has none to offer, and an index the entity does not declare has no builder)
        var source = Run(statement);

        // Act
        var fixes = await FixesAsync(source, "DYNA224", new QueryKeyAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    public async Task Fix_KeyOfABuilderImportedByUsingStatic_OffersNothing()
    {
        // Arrange (no receiver to change)
        var source = "using static Shop.Order.Keys.Pk;\n" + Run("""_ = orders.Index(Order.Indexes.ByUser).Query(Of("u"));""");

        // Act
        var fixes = await FixesAsync(source, "DYNA224", new QueryKeyAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    public async Task Fix_KeyOfAnotherAttribute_LeavesNoDiagnosticAndCompiles()
    {
        // Arrange
        var source = Run("""_ = orders.Index(Order.Indexes.ByUser).Query(Order.Keys.Pk.Of("u"));""");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA224", new QueryKeyAnalyzer())).Single().Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new QueryKeyAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    [Arguments("var total = result.Value;", "var total = result.GetValueOrDefault();", "var total = result.Found ? result.Value : default;")]
    [Arguments("Console.WriteLine(result.Value);", "Console.WriteLine(result.GetValueOrDefault());", "Console.WriteLine(result.Found ? result.Value : default);")]
    [Arguments("var more = result.Value + 1;", "var more = result.GetValueOrDefault() + 1;", "var more = (result.Found ? result.Value : default) + 1;")]
    public async Task Fix_ValueOfAResultInAVariable_OffersGetValueOrDefaultAndATestOfFound(string statement, string orDefault, string tested)
    {
        // Arrange
        var source = Async("var result = await orders.Get(key).Select(Order.Attrs.Total); " + statement);

        // Act
        var fixes = await FixesAsync(source, "DYNA227", new SelectResultAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo(
        [
            ("Use GetValueOrDefault()", Async("var result = await orders.Get(key).Select(Order.Attrs.Total); " + orDefault)),
            ("Use result.Found ? result.Value : default", Async("var result = await orders.Get(key).Select(Order.Attrs.Total); " + tested)),
        ]);
    }

    [Test]
    public async Task Fix_ValueOfAnExpression_OffersGetValueOrDefaultAlone()
    {
        // Arrange (a test of Found would read it twice)
        var source = Async("var total = (await orders.Get(key).Select(Order.Attrs.Total)).Value;");

        // Act
        var fixes = await FixesAsync(source, "DYNA227", new SelectResultAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([("Use GetValueOrDefault()", Async("var total = (await orders.Get(key).Select(Order.Attrs.Total)).GetValueOrDefault();"))]);
    }

    [Test]
    public async Task Fix_ValueInAPropertyPattern_OffersNothing()
    {
        // Arrange
        var source = Async("var result = await orders.Get(key).Select(Order.Attrs.Total); if (result is { Value: > 3 }) { }");

        // Act
        var fixes = await FixesAsync(source, "DYNA227", new SelectResultAnalyzer());

        // Assert
        await Assert.That(fixes).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Fix_ValueWithoutFound_LeavesNoDiagnosticAndCompiles(int rank)
    {
        // Arrange
        var source = Async("var result = await orders.Get(key).Select(Order.Attrs.Total); var more = result.Value + 1;");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA227", new SelectResultAnalyzer()))[rank].Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new SelectResultAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    [Arguments("""_ = orders.Update(key).Add(Order.Attrs.Name, "x");""", """_ = orders.Update(key).Set(Order.Attrs.Name, "x");""")]
    [Arguments("""_ = client.TransactWrite().Update(orders, key).Add(Order.Attrs.Name, "x");""", """_ = client.TransactWrite().Update(orders, key).Set(Order.Attrs.Name, "x");""")]
    public async Task Fix_AddOfAValueThatIsNoNumberOrSet_WritesSet(string statement, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixes = await FixesAsync(source, "DYNA233", new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([("Use Set", Run(expected))]);
    }

    [Test]
    public async Task FixAll_AddsInADocument_WritesSetForEach()
    {
        // Arrange
        var source = Run("""_ = orders.Update(key).Add(Order.Attrs.Name, "x"); _ = orders.Update(key).Add(Order.Attrs.Status, "y");""");

        // Act
        var fixedSource = await FixAllAsync(source, "DYNA233.0", new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new TypeArgumentAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    [Test]
    public async Task Fix_IfVersionOnAPutWithAVersion_RemovesItOrIgnoresTheVersion()
    {
        // Arrange
        var source = Run("_ = carts.Put(cart).IfVersion(3);");

        // Act
        var fixes = await FixesAsync(source, "DYNA235", new VersionClauseAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo(
        [
            ("Remove IfVersion(...)", Run("_ = carts.Put(cart);")),
            ("Use IgnoreVersion()", Run("_ = carts.Put(cart).IgnoreVersion();")),
        ]);
    }

    [Test]
    public async Task Fix_IfVersionOnAPutWithoutAVersion_RemovesIt()
    {
        // Arrange
        var source = Run("_ = orders.Put(order).IfVersion(1);");

        // Act
        var fixes = await FixesAsync(source, "DYNA235", new VersionClauseAnalyzer());

        // Assert
        await Assert.That(fixes).IsEquivalentTo([("Remove IfVersion(...)", Run("_ = orders.Put(order);"))]);
    }

    [Test]
    public async Task Fix_IfVersionOnALineOfItsOwn_RemovesTheLine()
    {
        // Arrange
        var source = Run("_ = carts.Put(cart)\n    .IfVersion(3)\n    .ReturnOld();");

        // Act
        var fixes = await FixesAsync(source, "DYNA235", new VersionClauseAnalyzer());

        // Assert
        await Assert.That(fixes[0].Text).IsEqualTo(Run("_ = carts.Put(cart)\n    .ReturnOld();"));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Fix_IfVersionOnAPutOfATransaction_LeavesNoDiagnosticAndCompiles(int rank)
    {
        // Arrange
        var source = Run("_ = client.TransactWrite().Put(carts, cart).IfVersion(1);");

        // Act
        var fixedSource = (await FixesAsync(source, "DYNA235", new VersionClauseAnalyzer()))[rank].Text;

        // Assert
        await Assert.That(await CheckAsync(fixedSource, new VersionClauseAnalyzer())).IsEqualTo((string.Empty, 0));
    }

    private static string Run(string statements) => AnalyzerRun.Member(Fields + "public static void Run()\n{\n" + statements + "\n}\n\nprivate static void Keep(QueryCommand<Order> query) { }\n");

    private static string Async(string statements) => AnalyzerRun.Member(Fields + "public static async Task Run()\n{\n" + statements + "\n}\n");

    private static string CrLf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    // What the analyzer reports of every rule and how many errors the compiler has, on the fixed text.
    private static async Task<(string Report, int Errors)> CheckAsync(string source, DiagnosticAnalyzer analyzer) =>
        (await Fixture.ReportWithAsync(source, analyzer), Fixture.CompilerErrors(source).Count);

    // Each code action of the one diagnostic of the rule, by title, with the text it leaves.
    private static async Task<List<(string Title, string Text)>> FixesAsync(string source, string id, DiagnosticAnalyzer analyzer)
    {
        var diagnostic = (await Fixture.DiagnosticsWithAsync(source, analyzer)).Single(found => found.Id == id);
        var fixes = new List<(string, string)>();
        foreach (var action in await ActionsAsync(source, diagnostic))
        {
            fixes.Add((action.Title, await ApplyAsync(action)));
        }

        return fixes;
    }

    private static async Task<List<CodeAction>> ActionsAsync(string source, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(Document(source), diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new SuggestedEditCodeFix().RegisterCodeFixesAsync(context);
        return actions;
    }

    private static async Task<string> FixAllAsync(string source, string key, DiagnosticAnalyzer analyzer)
    {
        var document = Document(source);
        var tree = (await document.GetSyntaxTreeAsync())!;

        // A fix-all finds the document of a diagnostic by its tree, so each is moved to the document's tree, at the same spans.
        var diagnostics = (await Fixture.DiagnosticsWithAsync(source, analyzer))
            .Select(found => Diagnostic.Create(found.Descriptor, Location.Create(tree, found.Location.SourceSpan), found.AdditionalLocations.Select(at => Location.Create(tree, at.SourceSpan)), found.Properties))
            .ToImmutableArray();
        var fix = new SuggestedEditCodeFix();
        var context = new FixAllContext(document, fix, FixAllScope.Document, key, fix.FixableDiagnosticIds, new Diagnostics(diagnostics), CancellationToken.None);
        return await ApplyAsync((await fix.GetFixAllProvider().GetFixAsync(context))!);
    }

    // The diagnostics come from the fixture's compilation of the same text, so their spans are the document's.
    private static Document Document(string source)
    {
        var workspace = new AdhocWorkspace();
        return workspace.CurrentSolution
            .AddProject("Consumer", "Consumer", LanguageNames.CSharp)
            .WithParseOptions(GeneratorRun.ParseOptions(LanguageVersion.Latest))
            .AddDocument("Code.cs", SourceText.From(source));
    }

    private static async Task<string> ApplyAsync(CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var document = solution.Projects.Single().Documents.Single();
        return (await document.GetTextAsync()).ToString();
    }

    private sealed class Diagnostics(ImmutableArray<Diagnostic> all) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(all);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(all);
    }
}
