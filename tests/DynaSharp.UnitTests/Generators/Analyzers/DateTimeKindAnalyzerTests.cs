using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA237: a DateTime of kind Unspecified where DynaSharp writes it as an instant, which throws when it runs.</summary>
public sealed class DateTimeKindAnalyzerTests
{
    private const string Model = """
        [DynamoDbEntity]
        [Discriminator("SHIFT")]
        [PartitionKey("SHIFT#{Id}")]
        [SortKey("{Start:yyyy-MM-dd}")]
        public partial record Shift
        {
            public required string Id { get; init; }

            public required DateTime Start { get; init; }

            public DateTime? End { get; init; }

            [DynamoDbConverter(typeof(LocalTime))]
            public DateTime Local { get; init; }

            public DateTimeOffset At { get; init; }

            public Slot? Slot { get; init; }

            public List<DateTime> Breaks { get; init; } = [];

            [DynamoDbIgnore]
            public DateTime Cached { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("STAMP")]
        [PartitionKey("STAMP#{Day}")]
        public partial class Stamp
        {
            [DynamoDbConverter(typeof(LocalTime))]
            public required DateTime Day { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("VISIT")]
        [PartitionKey("VISIT#{At}")]
        public partial class Visit
        {
            [KeyOnly]
            public required DateTime At { get; init; }
        }

        [DynamoDbDocument]
        public partial class Slot
        {
            public DateTime From { get; init; }

            [DynamoDbConverter(typeof(LocalTime))]
            public DateTime Local { get; init; }

            public static class Keys
            {
                public static int Of(DateTime from) => 0;
            }
        }

        public readonly struct LocalTime : IDynamoDbConverter<DateTime>
        {
            public static void Write(DynamoDbWriter writer, DateTime value) => writer.WriteString(value.ToString("O"));

            public static DateTime Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        public sealed class Plain
        {
            public DateTime When { get; set; }

            public static class Keys
            {
                public static int Of(DateTime when) => 0;

                internal static int Hidden(DateTime when) => 0;
            }

            public static class Inner
            {
                public static int Of(DateTime when) => 0;
            }
        }

        """;

    private const string Run = """
        public static void Run(Shift shift, Table<Shift> shifts, Shift.Keys.PrimaryKey key)
        {

        """;

    private static readonly AnalyzerFixture Fixture = new(Model);

    [Test]
    [Arguments("""_ = new Shift { Id = "a", Start = [|new DateTime(2026, 1, 15)|] };""", "new DateTime(2026, 1, 15)", "Shift")]
    [Arguments("""_ = new Shift { Id = "a", Start = [|new(2026, 1, 15)|] };""", "new(2026, 1, 15)", "Shift")]
    [Arguments("""_ = new Shift { Id = "a", Start = [|new DateTime(638000000000000000)|] };""", "new DateTime(638000000000000000)", "Shift")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.UtcNow, End = [|new DateTime(2026, 1, 15)|] };""", "new DateTime(2026, 1, 15)", "Shift")]
    [Arguments("""_ = shift with { Start = [|new DateTime(2026, 1, 15, 8, 0, 0)|] };""", "new DateTime(2026, 1, 15, 8, 0, 0)", "Shift")]
    [Arguments("""_ = new Slot { From = [|new DateTime(2026, 1, 15)|] };""", "new DateTime(2026, 1, 15)", "Slot")]
    [Arguments("""_ = new Stamp { Day = [|new DateTime(2026, 1, 15)|] };""", "new DateTime(2026, 1, 15)", "Stamp")]
    [Arguments("""_ = new Visit { At = [|new DateTime(2026, 1, 15)|] };""", "new DateTime(2026, 1, 15)", "Visit")]
    public async Task Analyze_UnspecifiedTimeInTheInitializerOfAMappedType_ReportsTheTime(string statement, string time, string type)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new DateTimeKindAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(time, $"a write of '{type}' throws")));
    }

    [Test]
    [Arguments("""_ = Shift.Keys.Of("a", [|new DateTime(2026, 1, 15)|]);""")]
    [Arguments("""_ = Shift.Keys.Sk.Format([|new DateTime(2026, 1, 15)|]);""")]
    [Arguments("""_ = Shift.Keys.Sk.Eq([|new DateTime(2026, 1, 15)|]);""")]
    [Arguments("""_ = Shift.Keys.Sk.Ge(start: [|new DateTime(2026, 1, 15)|]);""")]
    public async Task Analyze_UnspecifiedTimeGivenToAKeyBuilder_ReportsTheTime(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new DateTimeKindAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("new DateTime(2026, 1, 15)", "this call throws KeyFormatException")));
    }

    [Test]
    public async Task Analyze_BothBoundsOfBetween_ReportsEach()
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + """_ = Shift.Keys.Sk.Between([|new DateTime(2026, 1, 1)|], [|new DateTime(2026, 2, 1)|]);""" + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new DateTimeKindAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(
            source,
            Message("new DateTime(2026, 1, 1)", "this call throws KeyFormatException"),
            Message("new DateTime(2026, 2, 1)", "this call throws KeyFormatException")));
    }

    [Test]
    [Arguments("""_ = new Shift { Id = "a", Start = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc) };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = new DateTime(DateOnly.MinValue, TimeOnly.MinValue, DateTimeKind.Local) };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.UtcNow };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = default };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = new DateTime() };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.MinValue };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.Parse("2026-01-15") };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.UtcNow, Local = new DateTime(2026, 1, 15) };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.UtcNow, At = new DateTime(2026, 1, 15) };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.UtcNow, Cached = new DateTime(2026, 1, 15) };""")]
    [Arguments("""_ = new Shift { Id = "a", Start = DateTime.UtcNow, Breaks = { new DateTime(2026, 1, 15) } };""")]
    [Arguments("""_ = new Plain { When = new DateTime(2026, 1, 15) };""")]
    [Arguments("""_ = Plain.Keys.Of(new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = Plain.Keys.Hidden(new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = Slot.Keys.Of(new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = new Slot { Local = new DateTime(2026, 1, 15) };""")]
    [Arguments("""_ = Plain.Inner.Of(new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = Shift.Keys.TryOf("a", new DateTime(2026, 1, 15), out _);""")]
    [Arguments("""_ = Shift.Keys.Sk.TryOf(new DateTime(2026, 1, 15), out _);""")]
    [Arguments("""var start = new DateTime(2026, 1, 15); _ = new Shift { Id = "a", Start = start };""")]
    [Arguments("""Console.WriteLine(new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = new List<DateTime> { new DateTime(2026, 1, 15) };""")]
    [Arguments("""_ = new DateTimeOffset(new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = shifts.Update(key).Set(Shift.Attrs.End, new DateTime(2026, 1, 15));""")]
    [Arguments("""_ = shifts.Update(key).If(Shift.Attrs.End.Lt(new DateTime(2026, 1, 15)));""")]
    public async Task Analyze_TimeThatHasAKindOrIsNotWrittenAsAnInstantHere_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new DateTimeKindAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CodeWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var source = "public static class Code { public static System.DateTime Run() => new System.DateTime(2026, 1, 15); }";

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new DateTimeKindAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Message(string time, string consequence) =>
        $"DYNA237 Warning: '{time}' is a DateTime of kind Unspecified, which names no instant, so {consequence}. Pass DateTimeKind.Utc, as in new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), or use DateOnly for a date.";
}
