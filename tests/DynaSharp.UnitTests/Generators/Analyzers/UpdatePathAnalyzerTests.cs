using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA223: two actions of one update on paths that overlap, which DynamoDB rejects.</summary>
public sealed class UpdatePathAnalyzerTests
{
    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            public decimal Total { get; init; }

            public string? Note { get; init; }

            [OmitEmpty]
            public string? Comment { get; init; }

            [DynamoDbAttribute("note_text")]
            public string? Remark { get; init; }

            public Address? ShipTo { get; init; }

            public List<string> Tags { get; init; } = [];
        }

        [DynamoDbDocument]
        public partial record Address(string City, string Street);

        public sealed class Hand
        {
            public static class Attrs
            {
                public static Attr<string, StringConverter> X => new("x");
            }
        }

        public static class Mine
        {
            public static Attr<decimal, NumberConverter<decimal>> At(Attr<decimal, NumberConverter<decimal>> attribute) => attribute;

            public static string Attr(string path) => path;

            public static void Use(this int count, UpdateCommand<Order> update) { }

            public sealed class UpdateCommand { }

            public static UpdateCommand Make() => new();

            public static readonly string? Empty = null;
        }
        """;

    private const string Fields = """
        private static readonly Table<Order> orders = null!;
        private static readonly Order.Keys.PrimaryKey key = default;
        private static readonly DynamoDbClient client = null!;
        private static readonly string? text = null;
        private static readonly Order item = null!;

        """;

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Total, 1m).Set([|Order.Attrs.Total|], 2m)""", "Set", "Total", "Total")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Total, 1m).Add([|Order.Attrs.Total|], 2m)""", "Add", "Total", "Total")]
    [Arguments("""orders.Update(key).Remove(Order.Attrs.ShipTo).Set([|Order.Attrs.ShipTo.At(Address.Attrs.City)|], "c")""", "Set", "ShipTo.City", "ShipTo")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.ShipTo.At(Address.Attrs.City), "c").Set([|Order.Attrs.ShipTo|], null)""", "Set", "ShipTo", "ShipTo.City")]
    [Arguments("""orders.Update(key).Set("Total", 1).Set([|Order.Attrs.Total|], 2m)""", "Set", "Total", "Total")]
    [Arguments("""orders.Update(key).Remove("Tags").Remove([|"Tags[0]"|])""", "Remove", "Tags[0]", "Tags")]
    [Arguments("""orders.Update(key).SetIfNotNull(Order.Attrs.Note, text).Set([|Order.Attrs.Note|], "n")""", "Set", "Note", "Note")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Note, "n").SetIfNotNull([|Order.Attrs.Note|], "m")""", "SetIfNotNull", "Note", "Note")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Comment, "n").SetIfNotNull([|Order.Attrs.Comment|], "m")""", "SetIfNotNull", "Comment", "Comment")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Note, "n").SetIfNotNull([|Order.Attrs.Note|], "")""", "SetIfNotNull", "Note", "Note")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Comment, "n").SetIfNotNull([|Order.Attrs.Comment|], Mine.Empty)""", "SetIfNotNull", "Comment", "Comment")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Remark, "r").Remove([|"note_text"|])""", "Remove", "note_text", "note_text")]
    [Arguments("""orders.Update(key).Set(Attr<int>("n"), 1).Add([|new Attr<int, NumberConverter<int>>("n")|], 1)""", "Add", "n", "n")]
    [Arguments("""orders.Update(key).Set("n", 1).Remove([|Attr("n")|])""", "Remove", "n", "n")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Total, 1m).IfExists().SetIfNotExists([|"Total"|], 2)""", "SetIfNotExists", "Total", "Total")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Total, 1m).StoreKeyInputs().Set(value: 2m, attribute: [|Order.Attrs.Total|])""", "Set", "Total", "Total")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Tags, []).Delete([|Order.Attrs.Tags|], [])""", "Delete", "Tags", "Tags")]
    [Arguments("""shop.Update("a").Set("x", 1).Remove([|"x"|])""", "Remove", "x", "x")]
    [Arguments("""client.TransactWrite().Update(orders, key).Set(Order.Attrs.Total, 1m).Add([|Order.Attrs.Total|], 2m)""", "Add", "Total", "Total")]
    [Arguments("""client.TransactWrite().Put(orders, item).Update(orders, key).Set(Order.Attrs.Total, 1m).Add([|Order.Attrs.Total|], 2m).Put(orders, item)""", "Add", "Total", "Total")]
    public async Task Analyze_ActionsOnOverlappingPaths_ReportsTheLaterOne(string chain, string verb, string path, string earlier)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(verb, path, earlier)));
    }

    [Test]
    public async Task Analyze_ThreeActionsOnOnePath_ReportsEachLaterOne()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { _ = orders.Update(key).Set(Order.Attrs.Total, 1m).Add([|Order.Attrs.Total|], 2m).Remove([|Order.Attrs.Total|]).ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Add", "Total", "Total"), Message("Remove", "Total", "Total")));
    }

    [Test]
    public async Task Analyze_UpdateGivenToAMethod_IsReadAsItEnds()
    {
        // Arrange (an extension method of the consumer that takes the update as its second argument)
        var source = AnalyzerRun.Member(Fields + """public static void Run() { 5.Use(orders.Update(key).Set(Order.Attrs.Total, 1m).Remove([|Order.Attrs.Total|])); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Remove", "Total", "Total")));
    }

    [Test]
    [Arguments("""orders.Update(key).Set(Order.Attrs.ShipTo.At(Address.Attrs.City), "c").Set(Order.Attrs.ShipTo.At(Address.Attrs.Street), "s")""")]
    [Arguments("""orders.Update(key).Remove("Tags[0]").Remove("Tags[1]")""")]
    [Arguments("""orders.Update(key).Set("Total", 1).Set("Totals", 1)""")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Note, "n").SetIfNotNull(Order.Attrs.Note, null)""")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Comment, "n").SetIfNotNull(Order.Attrs.Comment, "")""")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Comment, "n").SetIfNotNull(Order.Attrs.Comment, string.Empty)""")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.Total, 1m).IfExists().Set(Order.Attrs.Note, "n")""")]
    [Arguments("""orders.Update(key).Set($"{Order.Attrs.Total} = {1}").Set(Order.Attrs.Total, 2m)""")]
    [Arguments("""orders.Update(key).Set(Attr<int>("a..b"), 1).Set(Attr<int>("a..b"), 1)""")]
    [Arguments("""orders.Update(key).Set(new Attr<int, NumberConverter<int>>(text!), 1).Set(new Attr<int, NumberConverter<int>>(text!), 1)""")]
    [Arguments("""orders.Update(key).Set(Hand.Attrs.X, "x").Set(Hand.Attrs.X, "y")""")]
    [Arguments("""orders.Update(key).Set(Mine.At(Order.Attrs.Total), 1m).Set(Mine.At(Order.Attrs.Total), 2m)""")]
    [Arguments("""orders.Update(key).Remove(Mine.Attr("x")).Remove(Mine.Attr("x"))""")]
    [Arguments("""orders.Update(key).Remove(new Attr()).Remove(new Attr())""")]
    [Arguments("""orders.Update(key).Remove(new string('x', 1)).Remove(new string('x', 1))""")]
    [Arguments("""orders.Update(key).Remove(Attr("x").ToString()).Remove(Attr("x").ToString())""")]
    [Arguments("""orders.Update(key).Set(Order.Attrs.ShipTo.At(new Attr<string, StringConverter>(text!)), "c").Set(Order.Attrs.ShipTo.At(new Attr<string, StringConverter>(text!)), "c")""")]
    [Arguments("""client.TransactWrite().Update(orders, key).Set(Order.Attrs.Total, 1m).Update(orders, key).Set(Order.Attrs.Total, 2m)""")]
    [Arguments("""Mine.Make()""")]
    [Arguments("""Array.Empty<int>()""")]
    [Arguments("""orders.Update(key).SetIfNotNull(Hand.Attrs.X, "").SetIfNotNull(Hand.Attrs.X, "")""")]
    [Arguments("""orders.Update(key).Remove(item.UserId).Remove(item.UserId)""")]
    public async Task Analyze_ActionsOnSeparatePathsOrThatCannotBeKnown_ReportNothing(string chain)
    {
        // Arrange (paths that only share a parent, SetIfNotNull of what writes nothing, a raw action, paths that are not known, and two steps of a transaction)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_DocumentInAVariable_IsNotFollowed()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { var shipTo = Order.Attrs.ShipTo; _ = orders.Update(key).Set(shipTo.At(Address.Attrs.City), "c").Set(shipTo.At(Address.Attrs.City), "d"); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_UpdateInAVariable_IsNotFollowed()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { var update = orders.Update(key).Set(Order.Attrs.Total, 1m); _ = update.Set(Order.Attrs.Total, 2m); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_StaticMethodUnderTheNamespaceOfTheLibrary_EndsTheChain()
    {
        // Arrange (a helper of the consumer in DynaSharp.Extras counts as the library and has no receiver)
        var source = """
            using DynaSharp;
            using DynaSharp.Mapping;

            namespace DynaSharp.Extras
            {
                [DynamoDbEntity]
                [Discriminator("NOTE")]
                [PartitionKey("NOTE#{Id}")]
                public partial class Note
                {
                    public required string Id { get; init; }

                    public string? Text { get; init; }
                }

                public static class Helpers
                {
                    public static UpdateCommand<Note> Start() => default;

                    public static void Run() => _ = Start().Set(Note.Attrs.Text, "a").Set([|Note.Attrs.Text|], "b");
                }
            }
            """;

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new UpdatePathAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Set", "Text", "Text")));
    }

    private static string Message(string verb, string path, string earlier) =>
        $"DYNA223 Warning: This {verb} changes '{path}', and an earlier action of this update changes '{earlier}', so DynamoDB rejects the request for paths that overlap. Keep one action per path.";
}
