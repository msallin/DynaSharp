using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA226: an update that sets some inputs of a key of an index and not the others, or one key of a sparse index without the other, which throws when it is sent.</summary>
public sealed class IndexKeyInputsAnalyzerTests
{
    private const string Entities = """

        public enum WorkStatus { Open, Closed }

        [DynamoDbEntity]
        [Discriminator("WORK")]
        [PartitionKey("ITEM#{Id}")]
        [SortKey("DATA")]
        [GlobalIndexKey("GSI1", PartitionKey = "OWNER#{Owner}", SortKey = "{Status}#{CreatedAt}")]
        [GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "{Id}")]
        [GlobalIndexKey("ByQueue", PartitionKey = "QUEUE#{Queue}#{Region}#{Desk}")]
        [GlobalIndexKey("Featured", PartitionKey = "FEATURED", SortKey = "ON#{FeaturedOn}", Sparse = true)]
        [LocalIndexKey("ByLabel", "LABEL#{Label}", Sparse = true)]
        public partial class WorkItem
        {
            public required string Id { get; init; }

            public required string Owner { get; init; }

            public required WorkStatus Status { get; init; }

            public required DateTimeOffset CreatedAt { get; init; }

            public required string Queue { get; init; }

            public required string Region { get; init; }

            public required string Desk { get; init; }

            public DateOnly? FeaturedOn { get; init; }

            public string? Label { get; init; }

            public string? Note { get; init; }

            public Place? Where { get; init; }
        }

        [DynamoDbDocument]
        public partial record Place(string City);

        [DynamoDbEntity]
        [Discriminator("NOTE")]
        [PartitionKey("NOTE#{Id}")]
        [GlobalIndexKey("GSI2", PartitionKey = "OWNER#{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
        public partial class Note
        {
            public required string Id { get; init; }

            public string? Owner { get; init; }

            public DateTimeOffset? LiveSince { get; init; }

            public DateTimeOffset? DeletedAt { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("SEALED")]
        [PartitionKey("SEALED#{Id}")]
        [GlobalIndexKey("BySeal", PartitionKey = "SEAL#{Seal}#{Kind}")]
        public partial class Sealed
        {
            public required string Id { get; init; }

            [KeyOnly]
            public required string Seal { get; init; }

            public required string Kind { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TALLY")]
        [PartitionKey("TALLY#{Id}")]
        [GlobalIndexKey("ByTotal", PartitionKey = "TOTAL#{Total:d6}#{Kind}")]
        public partial class Tally
        {
            public required string Id { get; init; }

            public required string Kind { get; init; }

            public int Count { get; init; }

            [ComputedFrom(nameof(Count))]
            public int Total => Count * 2;
        }

        [DynamoDbEntity]
        [Discriminator("BADGE")]
        [PartitionKey("BADGE#{Id}")]
        [GlobalIndexKey("ByLabel", PartitionKey = "LABEL#{Label}#{Kind}")]
        public partial class Badge
        {
            public required string Id { get; init; }

            public required string Name { get; init; }

            public required string Kind { get; init; }

            [DynamoDbAttribute("label"), ComputedFrom(nameof(Name))]
            public string Label => Name.Trim();
        }

        [DynamoDbEntity]
        [Discriminator("HALF")]
        [PartitionKey("{Id}")]
        [GlobalIndexKey("Half", PartitionKey = "H#{Tag}", SortKey = "{Mark}", SortKeyAttributeName = "Mark", Sparse = true)]
        public partial class Half
        {
            public required string Id { get; init; }

            public string? Tag { get; init; }

            public string? Mark { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("COPIED")]
        [PartitionKey("{Id}")]
        [GlobalIndexKey("Live", PartitionKey = "{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
        [GlobalIndexKey("Counted", PartitionKey = "{Count:d6}", SortKey = "C#{Mark}", Sparse = true)]
        public partial class Copied
        {
            public required string Id { get; init; }

            public string? Owner { get; init; }

            public DateTimeOffset? LiveSince { get; init; }

            public int? Count { get; init; }

            public string? Mark { get; init; }
        }

        public sealed class Hand
        {
            public static class Attrs
            {
                public static Attr<string, StringConverter> Owner => new("Owner");
            }
        }

        public static class Mine
        {
            public static UpdateCommand<WorkItem> Audited(this UpdateCommand<WorkItem> update) => update;

            public static int Count(this UpdateCommand<WorkItem> update) => 0;

            public static int Sink(UpdateCommand<WorkItem> update) => 0;
        }
        """;

    private const string Fields = """
        private static readonly Table<WorkItem> items = null!;
        private static readonly Table<Note> notes = null!;
        private static readonly Table<Sealed> seals = null!;
        private static readonly Table<Tally> tallies = null!;
        private static readonly WorkItem.Keys.PrimaryKey key = default;
        private static readonly Note.Keys.PrimaryKey noteKey = default;
        private static readonly Sealed.Keys.PrimaryKey sealKey = default;
        private static readonly Tally.Keys.PrimaryKey tallyKey = default;
        private static readonly Table<Badge> badges = null!;
        private static readonly Table<Half> halves = null!;
        private static readonly Table<Copied> copies = null!;
        private static readonly DynamoDbClient client = null!;
        private static readonly WorkItem item = null!;
        private static readonly string? text = null;
        private static readonly DateTimeOffset now = default;
        private static readonly DateTimeOffset? later = null;

        """;

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""items.Update(key).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).SendAsync()""")]
    [Arguments("""items.Update(key).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).ToRequestJson()""")]
    [Arguments("""items.Update(key).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).IfExists().Return(WorkItem.Attrs.Note)""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Open).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).SendAsync()""")]
    [Arguments("""items.Update(key).Set("Note", "n").Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).Set(WorkItem.Attrs.Note, text).SendAsync()""")]
    [Arguments("""client.TransactWrite().Update(items, key).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).SendAsync()""")]
    [Arguments("""client.TransactWrite().Update(items, key).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed).Put(items, item).SendAsync()""")]
    public async Task Analyze_SetOfOneInputOfAKeyOfAnIndex_ReportsWhatIsMissing(string chain)
    {
        // Arrange (closing an item without the time it was created, sent by a terminal, a Return, or the next step of a transaction)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("WorkItem", "Status", "CreatedAt", "{Status}#{CreatedAt}", "GSI1", "Set CreatedAt as well, with the value the item holds")));
    }

    [Test]
    public async Task Analyze_AwaitedUpdate_IsSent()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static async Task Run() { await items.Update(key).Set([|WorkItem.Attrs.Status|], WorkStatus.Closed); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("WorkItem", "Status", "CreatedAt", "{Status}#{CreatedAt}", "GSI1", "Set CreatedAt as well, with the value the item holds")));
    }

    [Test]
    public async Task Analyze_SetIfNotNullOfAValue_IsASet()
    {
        // Arrange (the update throws on the path where the value is there)
        var source = AnalyzerRun.Member(Fields + """public static void Run() { _ = notes.Update(noteKey).SetIfNotNull([|Note.Attrs.Owner|], text).ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Note", "Owner", "LiveSince", "LIVE#{LiveSince}", "GSI2", "Set LiveSince as well, with the value the item holds")));
    }

    [Test]
    public async Task Analyze_RestoreOfASparseIndexWithoutTheOtherKey_ReportsWhatThatKeyIsMadeOf()
    {
        // Arrange (a soft delete undone: the time comes back and the owner, which never changed, does not)
        var source = AnalyzerRun.Member(Fields + """public static void Run() { _ = notes.Update(noteKey).Remove(Note.Attrs.DeletedAt).Set([|Note.Attrs.LiveSince|], now).ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Note", "LiveSince", "Owner", "OWNER#{Owner}", "GSI2", "Set Owner as well, with the value the item holds")));
    }

    [Test]
    public async Task Analyze_TwoInputsMissing_NamesBoth()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { _ = items.Update(key).Set([|WorkItem.Attrs.Queue|], "q").ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("WorkItem", "Queue", "Region, Desk", "QUEUE#{Queue}#{Region}#{Desk}", "ByQueue", "Set Region and Desk as well, with the values the item holds")));
    }

    [Test]
    [Arguments("""seals.Update(sealKey).Set([|Sealed.Attrs.Kind|], "k").ToRequestJson()""", "Sealed", "Seal", "SEAL#{Seal}#{Kind}", "BySeal")]
    [Arguments("""tallies.Update(tallyKey).Set([|Tally.Attrs.Kind|], "k").ToRequestJson()""", "Tally", "Total", "TOTAL#{Total:d6}#{Kind}", "ByTotal")]
    [Arguments("""badges.Update("b").Set([|Badge.Attrs.Kind|], "k").ToRequestJson()""", "Badge", "Label", "LABEL#{Label}#{Kind}", "ByLabel")]
    public async Task Analyze_MissingInputWithoutAHandle_PointsAtAPut(string chain, string entity, string missing, string template, string index)
    {
        // Arrange (a property only a key holds, one its getter computes, and one its getter computes that has an attribute and a handle of its own, which the next Put would undo)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(entity, "Kind", missing, template, index, "Put the changed entity instead, or write the attribute of the key by a raw path")));
    }

    [Test]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Set(WorkItem.Attrs.CreatedAt, now).SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Owner, "ann").SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.FeaturedOn, DateOnly.MinValue).SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Label, "l").SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Note, "n").SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Where.At(Place.Attrs.City), "Bern").SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Remove(WorkItem.Attrs.CreatedAt).SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).SetIfNotExists(WorkItem.Attrs.CreatedAt, now).SendAsync()""")]
    [Arguments("""items.Update(key).Set("Status", "Closed").SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Id, "i").Set(WorkItem.Attrs.Status, WorkStatus.Closed).SendAsync()""")]
    [Arguments("""items.Update(key).Set(Hand.Attrs.Owner, "ann").Set(WorkItem.Attrs.Status, WorkStatus.Closed).SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Audited().SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed)""")]
    [Arguments("""notes.Update(noteKey).Set(Note.Attrs.Owner, "ann").Set(Note.Attrs.LiveSince, now).SendAsync()""")]
    [Arguments("""notes.Update(noteKey).Set(Note.Attrs.DeletedAt, now).Remove(Note.Attrs.LiveSince).SendAsync()""")]
    [Arguments("""notes.Update(noteKey).Set(Note.Attrs.Owner, null).Set(Note.Attrs.LiveSince, now).SendAsync()""")]
    [Arguments("""notes.Update(noteKey).SetIfNotNull(Note.Attrs.Owner, null).SendAsync()""")]
    [Arguments("""tallies.Update(tallyKey).Set(Tally.Attrs.Count, 2).SendAsync()""")]
    [Arguments("""shop.Update("a").Set("Status", "Closed").SendAsync()""")]
    [Arguments("""items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Count()""")]
    [Arguments("""Mine.Sink(items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed))""")]
    [Arguments("""notes.Update(noteKey).Set(Note.Attrs.LiveSince, later).SendAsync()""")]
    [Arguments("""client.TransactWrite().Update(items, key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Set(WorkItem.Attrs.CreatedAt, now).Put(items, item).SendAsync()""")]
    public async Task Analyze_UpdateThatLeavesNoKeyHalfDone_ReportsNothing(string chain)
    {
        // Arrange (every input, an index of one input, a key of literals, a sparse local index, no input, a path inside a document, what leaves an index or throws for something else,
        // a raw path, a key of the table, a handle of another type, a method of the consumer, an update that is not sent, a sparse index whole or left, and an update without a model)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_UpdateInAVariable_IsNotFollowed()
    {
        // Arrange (what was set before the variable is not known here)
        var source = AnalyzerRun.Member(Fields + """public static void Run() { var update = items.Update(key); _ = update.Set(WorkItem.Attrs.Status, WorkStatus.Closed).ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_HandleInAVariable_IsNotFollowed()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + """public static void Run() { var created = WorkItem.Attrs.CreatedAt; _ = items.Update(key).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Set(created, now).ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_UpdateOfATypeParameter_IsNotFollowed()
    {
        // Arrange (a generic helper has no model to look at)
        var source = AnalyzerRun.Member(Fields + """public static string Run<T>(Table<T> table) where T : IDynamoDbEntity<T> => table.Update("a").Set(WorkItem.Attrs.Status, WorkStatus.Closed).ToRequestJson();""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ValueOfAValueTypeWithoutANullableContext_IsASet()
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "#nullable disable\npublic static void Run() { var status = WorkStatus.Closed; _ = items.Update(key).Set([|WorkItem.Attrs.Status|], status).ToRequestJson(); }\n#nullable restore\n");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("WorkItem", "Status", "CreatedAt", "{Status}#{CreatedAt}", "GSI1", "Set CreatedAt as well, with the value the item holds")));
    }

    [Test]
    [Arguments("var owner = \"ann\"; _ = notes.Update(noteKey).Set(Note.Attrs.Owner, owner).ToRequestJson();")]
    [Arguments("DateTimeOffset? since = now; _ = notes.Update(noteKey).Set(Note.Attrs.LiveSince, since).ToRequestJson();")]
    public async Task Analyze_ReferenceOrNullableWithoutANullableContext_MayBeNull(string statements)
    {
        // Arrange (the compiler tracks no null there, so the Set may be a Remove, which takes the item out of the sparse index)
        var source = AnalyzerRun.Member(Fields + "#nullable disable\npublic static void Run() { " + statements + " }\n#nullable restore\n");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_SparseIndexWhoseOtherKeyIsTheTextOfAnAttribute_ReportsNothing()
    {
        // Arrange (the update copies Owner into the partition key of the index)
        var source = AnalyzerRun.Member(Fields + """public static void Run() { _ = copies.Update("c").Set(Copied.Attrs.LiveSince, now).ToRequestJson(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""copies.Update("c").Set([|Copied.Attrs.Owner|], "ann").ToRequestJson()""", "Owner", "LiveSince", "LIVE#{LiveSince}", "Live")]
    [Arguments("""copies.Update("c").Set([|Copied.Attrs.Mark|], "m").ToRequestJson()""", "Mark", "Count", "{Count:d6}", "Counted")]
    public async Task Analyze_SparseIndexWhoseOtherKeyIsNoTextOfAnAttribute_ReportsTheOtherKey(string chain, string set, string missing, string key, string index)
    {
        // Arrange (a literal before the time, and a number padded to six digits, are other text than the attribute holds)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Copied", set, missing, key, index, $"Set {missing} as well, with the value the item holds")));
    }

    [Test]
    [Arguments("""halves.Update("h").Set([|Half.Attrs.Tag|], "t").ToRequestJson()""", "Tag", "Mark", "{Mark}")]
    [Arguments("""halves.Update("h").Set([|Half.Attrs.Mark|], "m").ToRequestJson()""", "Mark", "Tag", "H#{Tag}")]
    public async Task Analyze_SparseIndexWithAPlainKey_ReportsTheOtherKey(string chain, string set, string missing, string key)
    {
        // Arrange (a plain key of an index is the attribute of its property, and the update has to set it like any other input)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new IndexKeyInputsAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Half", set, missing, key, "Half", $"Set {missing} as well, with the value the item holds")));
    }

    private static string Message(string entity, string set, string missing, string key, string index, string remedy) =>
        $"DYNA226 Warning: This update of '{entity}' sets {set} and not {missing}, so it cannot write the key {key} of the index {index} and throws when it is sent, because an update does not read the item and cannot tell what it holds for {missing}. {remedy}.";
}
