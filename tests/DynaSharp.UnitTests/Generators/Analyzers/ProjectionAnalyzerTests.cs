using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA208 and DYNA209: a read of part of an item that cannot get what it wants from it, which fails at run time.</summary>
public sealed class ProjectionAnalyzerTests
{
    private const string Model = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        [SortKey("ORDER#{OrderId}")]
        [GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", PartitionKeyAttributeName = "gsi1pk", SortKey = "{PlacedAt:yyyy-MM-dd}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            public required Guid OrderId { get; init; }

            public required string Status { get; init; }

            public required DateTimeOffset PlacedAt { get; init; }

            [DynamoDbAttribute("sum")]
            public required decimal Total { get; init; }

            public required string? Note { get; init; }

            public required HashSet<string> Tags { get; init; }

            public Address? ShipTo { get; init; }

            public int Lines { get; init; }

            [DynamoDbAttribute("label")]
            public string Label => "x";

            [DynamoDbIgnore]
            public required string Ignored { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string City, string? Zip);

        [DynamoDbEntity]
        [Discriminator("VISIT")]
        [PartitionKey("VISITOR#{Visitor}")]
        [SortKey("AT#{At:ticks}")]
        public partial record Visit
        {
            [KeyOnly]
            public required string Visitor { get; init; }

            [KeyOnly]
            public required DateTimeOffset At { get; init; }

            public string? Page { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TRIP")]
        [PartitionKey("TRIP#{Id}")]
        [GlobalIndexKey("ByDriver", PartitionKey = "DRIVER#{Driver}")]
        public partial record Trip
        {
            public required string Id { get; init; }

            [KeyOnly]
            public required string Driver { get; init; }

            public string? Page { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("ROUTE")]
        [PartitionKey("ROUTE#{Id}")]
        [GlobalIndexKey("ByCity", PartitionKey = "CITY#{City}")]
        public partial record Route
        {
            public required string Id { get; init; }

            public required string City { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("DEVICE")]
        public partial record Device
        {
            [PartitionKey]
            public required string Serial { get; init; }

            [SortKey]
            public required int Slot { get; init; }

            public required string Vendor { get; init; }

            public string? Note { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("PARCEL")]
        [PartitionKey("PARCEL#{Id}")]
        public partial record Parcel(string Id, string Carrier, int Weight = 0);

        [DynamoDbEntity]
        [Discriminator("EVENT")]
        [PartitionKey("EVENT#{At:yyyy-MM-dd}")]
        [SortKey("{Id}")]
        public partial record Event
        {
            public required DateTimeOffset At { get; init; }

            public required string Id { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("READING")]
        [PartitionKey("METER#{Meter}")]
        [SortKey("AT#{At}")]
        public partial record Reading
        {
            public required string Meter { get; init; }

            public required DateTimeOffset At { get; init; }

            public int Value { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("SDKREADING")]
        [PartitionKey("METER#{Meter}")]
        [SortKey("AT#{At:ms}")]
        public partial record SdkReading
        {
            public required string Meter { get; init; }

            public required DateTimeOffset At { get; init; }

            public int Value { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("LEDGER")]
        [PartitionKey("LEDGER#{Id}")]
        public partial record Ledger
        {
            public required string Id { get; init; }

            [Version]
            public long Revision { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("STRICT")]
        [PartitionKey("STRICT#{Id}")]
        public partial record StrictLedger
        {
            public required string Id { get; init; }

            [Version]
            public required long Revision { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("POSITIONAL")]
        [PartitionKey("POSITIONAL#{Id}")]
        public partial record PositionalLedger(string Id, [property: Version] long Revision);

        [DynamoDbEntity]
        [Discriminator("DEFAULTED")]
        [PartitionKey("DEFAULTED#{Id}")]
        public partial record DefaultedLedger(string Id, [property: Version] long Revision = 0);

        [DynamoDbEntity]
        [Discriminator("ADOPTED")]
        [PartitionKey("ADOPTED#{Id}")]
        public partial record AdoptedLedger
        {
            public required string Id { get; init; }

            [Version(Missing = VersionMissing.Allow)]
            public required long Revision { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("OMITTER")]
        [PartitionKey("OMITTER#{Id}")]
        [OmitEmpty]
        public partial record Omitter
        {
            public required string Id { get; init; }

            public string Title { get; init; } = "none";

            public List<string> Lines { get; init; } = [];

            public string? Note { get; init; }

            public HashSet<string> Labels { get; init; } = [];

            [OmitEmpty(false)]
            public string Plain { get; init; } = "none";
        }

        public readonly struct Counting : IDynamoDbConverter<string>
        {
            public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

            public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();

            public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => false;
        }

        public readonly struct Answering : IDynamoDbConverter<string>
        {
            public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

            public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();

            static bool IDynamoDbConverter<string>.TryReadMissing(out string value)
            {
                value = "none";
                return true;
            }
        }

        public readonly struct CountingAndAnswering : IDynamoDbConverter<string>
        {
            public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

            public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();

            public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => false;

            public static bool TryReadMissing(out string value)
            {
                value = "none";
                return true;
            }
        }

        [DynamoDbEntity]
        [Discriminator("COUNTED")]
        [PartitionKey("COUNTED#{Id}")]
        public partial record Counted
        {
            [DynamoDbConverter(typeof(Answering))]
            public required string Id { get; init; }

            [DynamoDbConverter(typeof(Counting))]
            public string Title { get; init; } = "none";

            [DynamoDbConverter(typeof(Counting))]
            public string? Note { get; init; }

            [DynamoDbConverter(typeof(Counting))]
            public required string Kept { get; init; }

            [DynamoDbConverter(typeof(CountingAndAnswering))]
            public string Answered { get; init; } = "none";

            [OmitEmpty]
            [DynamoDbConverter(typeof(Answering))]
            public string Left { get; init; } = "none";

            [OmitEmpty]
            [DynamoDbConverter(typeof(Counting))]
            public string Both { get; init; } = "none";

            public Mood Mood { get; init; }

            public Mood? MaybeMood { get; init; }
        }

        [DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
        public enum Mood { Calm, Loud }

        [DynamoDbEntity]
        [Discriminator("COUNTEDKEY")]
        public partial record CountedKey
        {
            [PartitionKey]
            [DynamoDbConverter(typeof(Counting))]
            public string Id { get; init; } = "none";
        }

        [DynamoDbDocument]
        public partial record Note(string Text);

        public sealed class Hand : IDynamoDbEntity<Hand>
        {
            static DynamoDbEntityInfo IDynamoDbEntity<Hand>.Info => null!;

            static void IDynamoDbEntity<Hand>.WriteKey(DynamoDbWriter writer, Hand entity) { }

            static void IDynamoDbDocument<Hand>.WriteAttributes(DynamoDbWriter writer, Hand entity) { }

            static Hand IDynamoDbDocument<Hand>.ReadAttributes(ref DynamoDbReader reader) => new();

            public static class Attrs
            {
                public static Attr<string, StringConverter> X => new("x");
            }
        }

        public readonly struct Mine
        {
            public static implicit operator Attr(Mine mine) => new("sum");
        }

        public static class Helpers
        {
            public static TCommand Audited<TCommand>(this TCommand command)
                where TCommand : IFilterable<TCommand>, allows ref struct => command;

            public static ScanCommand<Order> Project(ScanCommand<Order> command, string path) => command;

            public static Attr At(Attr attribute) => attribute;

            public static Attr Attr(string path) => new(path);
        }
        """;

    private const string Fields = """
        private static readonly Table<Order> orders = null!;
        private static readonly Table<Visit> visits = null!;
        private static readonly Table<Trip> trips = null!;
        private static readonly Table<Device> devices = null!;
        private static readonly Table<Route> routes = null!;
        private static readonly Table<Parcel> parcels = null!;
        private static readonly Table<Event> events = null!;
        private static readonly Table<Reading> readings = null!;
        private static readonly Table<SdkReading> sdkReadings = null!;
        private static readonly Table<Note> notes = null!;
        private static readonly Table<Hand> hands = null!;
        private static readonly Table<Omitter> omitters = null!;
        private static readonly Table<Counted> counted = null!;
        private static readonly Table<CountedKey> countedKeys = null!;
        private static readonly Table<Ledger> ledgers = null!;
        private static readonly Table<StrictLedger> strictLedgers = null!;
        private static readonly Table<AdoptedLedger> adoptedLedgers = null!;
        private static readonly Table<PositionalLedger> positionalLedgers = null!;
        private static readonly Table<DefaultedLedger> defaultedLedgers = null!;
        private static readonly Index<Order> byStatus = null!;
        private static readonly Index<Item> byStatusRaw = null!;
        private static readonly Order.Keys.PrimaryKey key = default;
        private static readonly string[] names = [];
        private static readonly string changing = "x";
        private static readonly Attr<string, StringConverter> kept = Order.Attrs.Status;
        private static QueryCommand<Order> Helper(QueryCommand<Order> command) => command;
        private static void Take(QueryCommand<Order> command) { }

        """;

    private static readonly AnalyzerFixture Shared = new(Model);

    [Test]

    // Constant paths and handles, on every read that has Select, and the terminals that read items.
    [Arguments("""_ = orders.Get(key).[|Project|]("Status", "PlacedAt").SendAsync();""", "Total", "Order")]
    [Arguments("""_ = orders.Get(key).[|Project|]("Status", "PlacedAt").ItemAsync();""", "Total", "Order")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).[|Project|]("sum").ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).[|Project|]("sum").ItemAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).[|Project|]("sum").PageAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).[|Project|]("sum").PagesAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|]("sum").ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|]("sum").Parallel(2).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](Order.Attrs.Total).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](Order.Attrs.ShipTo.At(Address.Attrs.City)).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](Attr("sum")).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](new Attr("sum")).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](new Attr<decimal, NumberConverter<decimal>>("sum")).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](["sum"]).ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Scan().[|Project|](new[] { "sum" }).ItemsAsync();""", "Status", "Order")]

    // A command that is sent by an await, and a statement that drops it, which DYNA203 has more to say about.
    [Arguments("""static async Task Send() => await orders.Get(key).[|Project|]("Status");""", "PlacedAt", "Order")]
    [Arguments("""orders.Scan().[|Project|]("sum");""", "Status", "Order")]

    // The clauses around it change nothing, and every call counts: the last one is reported.
    [Arguments("""_ = orders.Scan().Project("sum").Limit(1).[|Project|]("Status").Consistent().ItemsAsync();""", "PlacedAt", "Order")]
    [Arguments("""_ = orders.Scan().Filter(Attr("a").Exists()).[|Project|]("sum").Limit(1).ItemsAsync();""", "Status", "Order")]

    // A key of an index is not in a read of the table, and a key that loses what it holds restores nothing, on any read.
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).[|Project|]("sum", "PlacedAt").ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = byStatus.Query(Order.Keys.ByStatusPk.Of("a")).[|Project|]("sum").ItemsAsync();""", "PlacedAt", "Order")]
    [Arguments("""_ = events.Query(Event.Keys.Pk.Of(default)).[|Project|]("Id").ItemsAsync();""", "At", "Event")]
    [Arguments("""_ = sdkReadings.Query(SdkReading.Keys.Pk.Of("a")).[|Project|]("Value").ItemsAsync();""", "At", "SdkReading")]
    [Arguments("""_ = trips.Scan().[|Project|]("Page").ItemsAsync();""", "Driver", "Trip")]

    // The name of a [KeyOnly] property is no attribute, and a parameter of a constructor counts as a required member does.
    [Arguments("""_ = trips.Scan().[|Project|]("Driver").ItemsAsync();""", "Driver", "Trip")]
    [Arguments("""_ = devices.Scan().[|Project|]("Note").ItemsAsync();""", "Vendor", "Device")]
    [Arguments("""_ = parcels.Scan().[|Project|]("Weight").ItemsAsync();""", "Carrier", "Parcel")]
    [Arguments("""_ = notes.Scan().[|Project|]("Other").ItemsAsync();""", "Text", "Note")]
    public async Task Project_LeavesOutWhatTheReadNeeds_Reports(string statement, string member, string type)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(member, type, ", or use Select(handle) to read one attribute")));
    }

    [Test]
    [Arguments("""_ = orders.BatchGet().Key(key).[|Project|]("sum").SendAsync();""", "Status", "Order")]
    [Arguments("""_ = orders.Query([Order.Keys.Pk.Of("a"), Order.Keys.Pk.Of("b")]).[|Project|]("sum").ItemsAsync();""", "Status", "Order")]
    [Arguments("""_ = shop.Query("a").Of<Device, Parcel>().[|Project|]("Vendor").ItemsAsync();""", "Carrier", "Parcel")]
    [Arguments("""_ = shop.Scan().Of<Parcel, Device>().[|Project|]("Vendor").ItemsAsync();""", "Carrier", "Parcel")]
    public async Task Project_ReadWithoutSelect_SaysOnlyToIncludeIt(string statement, string member, string type)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(member, type, string.Empty)));
    }

    [Test]
    public async Task Project_OnItemsThatAMixedReadTakesOver_ReportsIt()
    {
        // Arrange (it is the last that counts of what the chain says, and the mix comes after it)
        var source = Source("""_ = shop.Query("a").[|Project|]("Vendor").Of<Device, Parcel>().ItemsAsync();""");

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Carrier", "Parcel", ", or use Select(handle) to read one attribute")));
    }

    [Test]

    // Everything a read needs is there, by name, by a renamed attribute, or by a handle.
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project("Status", "PlacedAt", "sum").ItemsAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project(Order.Attrs.Status, Order.Attrs.PlacedAt, Order.Attrs.Total).ItemsAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project("Status").Project(Order.Attrs.PlacedAt).Project("sum").ItemsAsync();""")]
    [Arguments("""_ = orders.Get(key).Project("Status", "PlacedAt", "sum").ItemAsync();""")]
    [Arguments("""_ = orders.BatchGet().Key(key).Project("Status", "PlacedAt", "sum").SendAsync();""")]
    [Arguments("""_ = orders.Query([Order.Keys.Pk.Of("a")]).Project("Status", "PlacedAt", "sum").ItemsAsync();""")]

    // A nested path keeps the member at its top.
    [Arguments("""_ = devices.Scan().Project("Vendor.Name").ItemsAsync();""")]
    [Arguments("""_ = devices.Scan().Project("Vendor[0]").ItemsAsync();""")]

    // Not named and still there: a key the read adds, what such a key restores, and a key that is named.
    [Arguments("""_ = visits.Query(Visit.Keys.Pk.Of("a")).Project("Page").ItemsAsync();""")]
    [Arguments("""_ = readings.Query(Reading.Keys.Pk.Of("a")).Project("Value").ItemsAsync();""")]
    [Arguments("""_ = devices.Scan().Project("Vendor").ItemsAsync();""")]
    [Arguments("""_ = parcels.Scan().Project("Carrier").ItemsAsync();""")]
    [Arguments("""_ = byStatus.Query(Order.Keys.ByStatusPk.Of("a")).Project("PlacedAt", "sum").ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project("gsi1pk", "PlacedAt", "sum").ItemsAsync();""")]

    // A read of several types that has what each needs, also on an index, whose keys it adds.
    [Arguments("""_ = shop.Query("a").Of<Device, Parcel>().Project("Vendor", "Carrier").ItemsAsync();""")]
    [Arguments("""_ = byStatusRaw.Query("a").Of<Device, Order>().Project("Vendor", "PlacedAt", "sum").ItemsAsync();""")]

    // No entity, or one that nothing is known about.
    [Arguments("""_ = shop.Get("a").Project("x").SendAsync();""")]
    [Arguments("""_ = shop.Query("a").Project("x").ItemsAsync();""")]
    [Arguments("""_ = shop.Query("a").Project(Order.Attrs.Total).ItemsAsync();""")]
    [Arguments("""_ = shop.Query(["a"]).Project("x").ItemsAsync();""")]
    [Arguments("""_ = hands.Scan().Project("x").ItemsAsync();""")]
    [Arguments("""_ = shop.Query("a").Project("x").Of<Device, Hand>().ItemsAsync();""")]

    // Values that are not known say nothing.
    [Arguments("""_ = orders.Scan().Project(names).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(changing).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project("sum", changing).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(kept).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(Attr(changing)).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project([.. names]).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project("sum").Project(names).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project("a..b").ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(new Mine()).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(Helpers.Attr("sum")).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(Helpers.At(Attr("sum"))).ItemsAsync();""")]
    [Arguments("""_ = orders.Scan().Project(Hand.Attrs.X).ItemsAsync();""")]

    // A chain that goes on somewhere else, or came from somewhere else.
    [Arguments("""var query = orders.Query(Order.Keys.Pk.Of("a")).Project("sum"); query = query.Project("Status"); _ = query.ItemsAsync();""")]
    [Arguments("""var query = orders.Query(Order.Keys.Pk.Of("a")); _ = query.Project("sum").ItemsAsync();""")]
    [Arguments("""Take(orders.Query(Order.Keys.Pk.Of("a")).Project("sum"));""")]
    [Arguments("""_ = Helper(orders.Query(Order.Keys.Pk.Of("a"))).Project("sum").ItemsAsync();""")]
    [Arguments("""_ = Helper(orders.Query(Order.Keys.Pk.Of("a")).Project("sum")).ItemsAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project("sum").Audited().ItemsAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Audited().Project("sum").ItemsAsync();""")]

    // No item is read, or no entity.
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project("sum").ToRequestJson();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project("sum").CountAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Project("sum").Select(Order.Attrs.Lines).ValuesAsync();""")]

    // What the read gets along without: a required member that is nullable reads as null and a required set as empty, and what is computed or ignored is not read.
    [Arguments("""_ = devices.Scan().Project("Vendor").Project("Slot").ItemsAsync();""")]

    // A projection of nothing is none, and a method of the consumer is not this one.
    [Arguments("""_ = orders.Scan().Project(new string[0]).ItemsAsync();""")]
    [Arguments("""_ = Helpers.Project(orders.Scan(), "sum").ItemsAsync();""")]
    public async Task Project_ReadGetsWhatItNeeds_ReportsNothing(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Project_InAGenericHelper_ReportsNothing()
    {
        // Arrange (the type is a parameter, which has no model)
        var source = AnalyzerRun.Member("""public static IAsyncEnumerable<T> Read<T>(Table<T> table) where T : IDynamoDbEntity<T> => table.Scan().Project("Status").ItemsAsync();""");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]

    // Every write of the library stores the version, and an item without one throws a message that names VersionMissing.Allow, which beats the silent 0 of a read of the entity.
    [Arguments("""_ = ledgers.Get(Ledger.Keys.Of("a")).Select(Ledger.Attrs.Revision).SendAsync();""")]
    [Arguments("""_ = ledgers.Query(Ledger.Keys.Pk.Of("a")).Select(Ledger.Attrs.Revision).ValuesAsync();""")]
    [Arguments("""_ = ledgers.Scan().Select(Ledger.Attrs.Revision).ValuesAsync();""")]
    [Arguments("""_ = defaultedLedgers.Get(DefaultedLedger.Keys.Of("a")).Select(DefaultedLedger.Attrs.Revision).SendAsync();""")]

    // A version that a read of the entity needs as well.
    [Arguments("""_ = strictLedgers.Get(StrictLedger.Keys.Of("a")).Select(StrictLedger.Attrs.Revision).SendAsync();""")]
    [Arguments("""_ = positionalLedgers.Get(PositionalLedger.Keys.Of("a")).Select(PositionalLedger.Attrs.Revision).SendAsync();""")]
    public async Task Select_PlainVersion_ReportsNothing(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = adoptedLedgers.Get(AdoptedLedger.Keys.Of("a")).Select(AdoptedLedger.Attrs.Revision).SendAsync();""")]
    [Arguments("""_ = adoptedLedgers.Query(AdoptedLedger.Keys.Pk.Of("a")).Select(AdoptedLedger.Attrs.Revision).ValuesAsync();""")]
    [Arguments("""_ = adoptedLedgers.Scan().Select(AdoptedLedger.Attrs.Revision).ValuesAsync();""")]
    public async Task Select_VersionThatAllowsAnItemWithoutOne_ReportsNothing(string statement)
    {
        // Arrange (the handle reads a missing attribute as 0, so there is nothing to throw)
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Project_ConvertedNameOfARequiredMember_ReportsNothing()
    {
        // Arrange
        var source = Named("""_ = bills.Query(Bill.Keys.Pk.Of("a")).Project("total_due").ItemsAsync();""");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Project_DeclaredNameOfARequiredMember_Reports()
    {
        // Arrange (under a naming the declared name reads another attribute, so the member is left out)
        var source = Named("""_ = bills.Query(Bill.Keys.Pk.Of("a")).[|Project|]("TotalDue").ItemsAsync();""");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA208 Warning: This Project leaves out 'TotalDue' of 'Bill', and a read of the entity cannot do without it, so the read throws DynamoDbMappingException. Include it, or use Select(handle) to read one attribute."));
    }

    [Test]
    [Arguments("""_ = omitters.Get(Omitter.Keys.Of("a")).Select([|Omitter.Attrs.Title|]).SendAsync();""", "Title")]
    [Arguments("""_ = omitters.Query(Omitter.Keys.Pk.Of("a")).Select([|Omitter.Attrs.Lines|]).ValuesAsync();""", "Lines")]
    public async Task Select_PropertyThatOmitEmptyLeavesOut_Reports(string statement, string property)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA209 Warning: '{property}' of 'Omitter' has [OmitEmpty], and an item that held an empty value has no such attribute: a read of the entity keeps the initial value, Select does not and throws. Read the entity, or make the property nullable."));
    }

    [Test]
    [Arguments("""_ = omitters.Get(Omitter.Keys.Of("a")).Select(Omitter.Attrs.Note).SendAsync();""")]
    [Arguments("""_ = omitters.Get(Omitter.Keys.Of("a")).Select(Omitter.Attrs.Labels).SendAsync();""")]
    [Arguments("""_ = omitters.Get(Omitter.Keys.Of("a")).Select(Omitter.Attrs.Plain).SendAsync();""")]
    public async Task Select_PropertyWithAnAnswerForAMissingAttribute_ReportsNothing(string statement)
    {
        // Arrange (a nullable property reads null and a set an empty set, and the last one is always written)
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Select_PropertyWhoseConverterCountsAValueAsMissing_Reports()
    {
        // Arrange
        var source = Source("""_ = counted.Get(Counted.Keys.Of("a")).Select([|Counted.Attrs.Title|]).SendAsync();""");

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA209 Warning: 'Title' of 'Counted' has a converter that can count a stored value as missing: a read of the entity keeps the initial value, Select does not and throws. Read the entity, make the property nullable, or have the converter answer TryReadMissing."));
    }

    [Test]
    public async Task Select_PropertyOfAnEnumThatCountsAnUnknownNameAsMissing_Reports()
    {
        // Arrange (the generated converter answers for itself, with no [DynamoDbConverter] to find)
        var source = Source("""_ = counted.Get(Counted.Keys.Of("a")).Select([|Counted.Attrs.Mood|]).SendAsync();""");

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA209 Warning: 'Mood' of 'Counted' has a converter that can count a stored value as missing: a read of the entity keeps the initial value, Select does not and throws. Read the entity, make the property nullable, or have the converter answer TryReadMissing."));
    }

    [Test]
    public async Task Select_OmitEmptyWhoseConverterCountsAValueAsMissing_ReportsTheOmission()
    {
        // Arrange (one report, and the first reason is what a write of the entity does every day)
        var source = Source("""_ = counted.Get(Counted.Keys.Of("a")).Select([|Counted.Attrs.Both|]).SendAsync();""");

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA209 Warning: 'Both' of 'Counted' has [OmitEmpty], and an item that held an empty value has no such attribute: a read of the entity keeps the initial value, Select does not and throws. Read the entity, or make the property nullable."));
    }

    [Test]

    // Null is what a missing attribute reads as.
    [Arguments("""_ = counted.Get(Counted.Keys.Of("a")).Select(Counted.Attrs.Note).SendAsync();""")]
    [Arguments("""_ = counted.Get(Counted.Keys.Of("a")).Select(Counted.Attrs.MaybeMood).SendAsync();""")]

    // A read of the entity throws for a required member as well.
    [Arguments("""_ = counted.Get(Counted.Keys.Of("a")).Select(Counted.Attrs.Kept).SendAsync();""")]

    // A converter with an answer for a missing attribute gives Select a value, implicitly, explicitly, for a property that [OmitEmpty] leaves out and for an input of the table key.
    [Arguments("""_ = counted.Get(Counted.Keys.Of("a")).Select(Counted.Attrs.Answered).SendAsync();""")]
    [Arguments("""_ = counted.Get(Counted.Keys.Of("a")).Select(Counted.Attrs.Left).SendAsync();""")]
    [Arguments("""_ = counted.Get(Counted.Keys.Of("a")).Select(Counted.Attrs.Id).SendAsync();""")]

    // A plain key is read for what it is.
    [Arguments("""_ = countedKeys.Get(CountedKey.Keys.Of("a")).Select(CountedKey.Attrs.Id).SendAsync();""")]
    public async Task Select_PropertyWhoseConverterCountsAValueAsMissing_ReportsNothingWhereTheReadsAgree(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Project_RequiredVersionThatAllowsAnItemWithoutOne_ReportsNothing()
    {
        // Arrange (a read gives 0 for it, as it does for an item without one)
        var source = Source("""_ = adoptedLedgers.Query(AdoptedLedger.Keys.Pk.Of("a")).Project("Id").ItemsAsync();""");

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = orders.Get(key).Select([|Order.Attrs.UserId|]).SendAsync();""", "UserId", "Order", "pk")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Select([|Order.Attrs.OrderId|]).ValuesAsync();""", "OrderId", "Order", "sk")]
    [Arguments("""_ = routes.Scan().Select([|Route.Attrs.Id|]).ValuesAsync();""", "Id", "Route", "pk")]
    [Arguments("""_ = readings.Query(Reading.Keys.Pk.Of("a")).Select([|Reading.Attrs.At|]).ValuesAsync();""", "At", "Reading", "sk")]
    public async Task Select_KeyInputOfTheTable_Reports(string statement, string property, string type, string key)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA209 Warning: '{property}' of '{type}' is held by the key '{key}' of the table, and an item that an Update created has no attribute of its own for it: a read of the entity restores it from the key, Select does not and throws. Read the entity, or Select only from items a Put wrote."));
    }

    [Test]

    // An attribute that every item has: a plain property, a plain key, and a nullable input, whose missing attribute means null.
    [Arguments("""_ = orders.Get(key).Select(Order.Attrs.Total).SendAsync();""")]
    [Arguments("""_ = devices.Scan().Select(Device.Attrs.Serial).ValuesAsync();""")]
    [Arguments("""_ = orders.Query(Order.Keys.Pk.Of("a")).Select(Order.Attrs.Note).ValuesAsync();""")]

    // A key that loses what it holds restores nothing for a read of the entity either.
    [Arguments("""_ = orders.Get(key).Select(Order.Attrs.PlacedAt).SendAsync();""")]
    [Arguments("""_ = sdkReadings.Query(SdkReading.Keys.Pk.Of("a")).Select(SdkReading.Attrs.At).ValuesAsync();""")]

    // An input of an index is an attribute that an update sets, on a table with a sort key and on one without.
    [Arguments("""_ = orders.Scan().Select(Order.Attrs.Status).ValuesAsync();""")]
    [Arguments("""_ = routes.Scan().Select(Route.Attrs.City).ValuesAsync();""")]

    // Handles that cannot be followed, a nested path, and one of a type without a model.
    [Arguments("""_ = orders.Get(key).Select(kept).SendAsync();""")]
    [Arguments("""_ = orders.Get(key).Select(Order.Attrs.ShipTo.At(Address.Attrs.City)).SendAsync();""")]
    [Arguments("""_ = orders.Get(key).Select(new Attr<string, StringConverter>("UserId")).SendAsync();""")]
    [Arguments("""_ = hands.Scan().Select(Hand.Attrs.X).ValuesAsync();""")]
    [Arguments("""_ = notes.Scan().Select(Note.Attrs.Text).ValuesAsync();""")]
    public async Task Select_AttributeOfItsOwn_ReportsNothing(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportWithAsync(source, new ProjectionAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilationWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var compilation = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText("""public static class Code { public static int Project(int value) => value; public static int Run() => Project(1); }""")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Act
        var diagnostics = await compilation.WithAnalyzers([new ProjectionAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    private static string Source(string statement) => AnalyzerRun.Member(Fields + "public static void Run() {" + statement + "}");

    private static string Named(string statement) =>
        AnalyzerRun.Member(
            "private static readonly Table<Bill> bills = null!;\npublic static void Run() {" + statement + "}",
            "[DynamoDbEntity, Discriminator(\"BILL\"), PartitionKey(\"BILL#{Id}\")] public partial class Bill { public required string Id { get; init; } public required decimal TotalDue { get; init; } }")
            .Replace("namespace Shop;", "[assembly: DynamoDbDefaults(Naming = AttributeNaming.SnakeCase)]\n\nnamespace Shop;", StringComparison.Ordinal);

    private static string Message(string member, string type, string select) =>
        $"DYNA208 Warning: This Project leaves out '{member}' of '{type}', and a read of the entity cannot do without it, so the read throws DynamoDbMappingException. Include it{select}.";
}
