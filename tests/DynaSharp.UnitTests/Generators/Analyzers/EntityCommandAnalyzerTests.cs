using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA205 to DYNA207, DYNA219, DYNA225, DYNA236 and DYNA239: what a command says about its entity that the model of the entity contradicts.</summary>
public sealed class EntityCommandAnalyzerTests
{
    private const string VersionMessage = "DYNA206 Warning: This Put of 'Locked' holds a version above 0, and an item that does not exist has no version, so IfNotExists can never hold and the command throws. For a first write leave the version at 0, or call IgnoreVersion.";

    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("EVT")]
        [PartitionKey("EVT#{Device}#{Shard:d2}")]
        [SortKey("{At:ticks}#{Id}")]
        [GlobalIndexKey("Alerts", PartitionKey = "ALERT#{Level}", Sparse = true)]
        public partial class DeviceEvent
        {
            public required string Device { get; init; }

            public required int Shard { get; init; }

            public required DateTimeOffset At { get; init; }

            public required string Id { get; init; }

            public string? Level { get; init; }

            public long Count { get; init; }

            public string? Payload { get; init; }

            public Address? Origin { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string Street)
        {
            public readonly struct Key : IKey<Address>
            {
                public void WriteKey(DynamoDbWriter writer) { }
            }
        }

        [DynamoDbEntity]
        [Discriminator("CUSTOMER")]
        public partial class Customer
        {
            [PartitionKey]
            public required string Id { get; init; }

            [SortKey]
            public required int Region { get; init; }

            public string? At { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TAGGED")]
        public partial class Tagged
        {
            [PartitionKey, DynamoDbConverter(typeof(TagsAsOneString))]
            public required HashSet<string> Tags { get; init; }
        }

        public readonly struct TagsAsOneString : IDynamoDbConverter<HashSet<string>>
        {
            public static void Write(DynamoDbWriter writer, HashSet<string> value) => writer.WriteString(string.Join(",", value));

            public static HashSet<string> Read(ref DynamoDbReader reader, AttributeType type) => [];
        }

        [DynamoDbEntity]
        [Discriminator("LOCKED")]
        [PartitionKey("LOCK#{Id}")]
        [GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}")]
        public partial record Locked
        {
            public required string Id { get; init; }

            public required string Owner { get; init; }

            [Version]
            public long Revision { get; init; }

            public string? Tag;
        }

        [DynamoDbDocument]
        public partial record Note(DateTimeOffset At);

        [DynamoDbEntity]
        [Discriminator("VISIT")]
        [PartitionKey("VISIT#{Id}")]
        [GlobalIndexKey("ByArrival", PartitionKey = "A#{Arrival}", SortKey = "{Slot}")]
        [GlobalIndexKey("ByLoad", PartitionKey = "{Load}", PartitionKeyAttributeName = "load")]
        public partial class Visit
        {
            public required string Id { get; init; }

            public required Note Message { get; init; }

            public required string Slot { get; init; }

            public int Guests { get; init; }

            public HashSet<string> Tags { get; init; } = [];

            public string? Remark { get; init; }

            [ComputedFrom(nameof(Message))]
            public DateTimeOffset Arrival => Message.At;

            [DynamoDbAttribute("load"), ComputedFrom(nameof(Guests), nameof(Tags))]
            public int Load => Guests + Tags.Count;
        }

        [DynamoDbEntity]
        [Discriminator("SHIFT")]
        [PartitionKey("SHIFT#{Code}")]
        public partial class Shift
        {
            public required string Name { get; init; }

            public Note? Message { get; init; }

            [ComputedFrom(nameof(Name), nameof(Message))]
            public string Code => Name;
        }

        [DynamoDbEntity]
        [Discriminator("BASE")]
        [PartitionKey("BASE#{Id}")]
        public partial class BaseItem
        {
            public required string Id { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("DERIVED")]
        [PartitionKey("DERIVED#{Id}")]
        public partial class DerivedItem : BaseItem
        {
            public string? Note { get; init; }
        }

        public sealed class Hand : IDynamoDbEntity<Hand>
        {
            static DynamoDbEntityInfo IDynamoDbEntity<Hand>.Info => null!;

            static void IDynamoDbEntity<Hand>.WriteKey(DynamoDbWriter writer, Hand entity) { }

            static void IDynamoDbDocument<Hand>.WriteAttributes(DynamoDbWriter writer, Hand entity) { }

            static Hand IDynamoDbDocument<Hand>.ReadAttributes(ref DynamoDbReader reader) => new();

            public readonly struct Key : IKey<Hand>
            {
                public void WriteKey(DynamoDbWriter writer) { }
            }

            public static class Attrs
            {
                public static Attr<string, StringConverter> X => new("x");
            }
        }
        """;

    private const string Fields = """
        private static readonly Table<DeviceEvent> events = null!;
        private static readonly Table<Customer> customers = null!;
        private static readonly Table<Tagged> taggeds = null!;
        private static readonly Table<Locked> locks = null!;
        private static readonly Table<DerivedItem> derived = null!;
        private static readonly Index<DeviceEvent> alerts = null!;
        private static readonly DynamoDbClient client = null!;
        private static readonly DateTimeOffset now = DateTimeOffset.UnixEpoch;
        private static readonly Locked held = null!;
        private static Locked Make() => held;
        private static PutCommand<Locked> Start() => locks.Put(held);
        private static readonly Table<Address> addresses = null!;
        private static readonly Table<Hand> hands = null!;
        private static readonly Table<Visit> visits = null!;
        private static readonly Table<Shift> shifts = null!;
        private static readonly Visit.Keys.PrimaryKey visitKey = default;
        private static readonly Shift.Keys.PrimaryKey shiftKey = default;
        private static readonly Note note = null!;
        private static readonly DeviceEvent.Keys.PrimaryKey key = default;
        private static readonly Customer.Keys.PrimaryKey customerKey = default;
        private static readonly Locked.Keys.PrimaryKey lockKey = default;
        private static Table<Locked> Locks() => locks;
        private static void Keep(BatchWriteCommand<Locked> batch) { }
        private static void Give(out BatchWriteCommand<Locked> batch) => batch = locks.BatchWrite().Put(held);

        """;

    private const string Outside = """

        public static class Steps
        {
            public static UpdateStep Checked(this UpdateStep step) => step.IfVersion(1);

            public static string Render(UpdateStep step) => step.IfVersion(1).ToRequestJson();
        }

        public readonly ref struct Holder
        {
            public Holder(UpdateStep step) { }
        }

        """;

    private const string BatchMessage = "DYNA236 Warning: 'Locked' has a [Version], and a batch cannot carry the condition of a version, so this batch throws InvalidOperationException when it is sent. Call IgnoreVersion() on the batch to write the items without the check, each with the next version, or write them one by one with Put.";

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""_ = events.Update(key).Set([|DeviceEvent.Attrs.At|], now);""", "At", "DeviceEvent")]
    [Arguments("""_ = events.Update(key).Set([|DeviceEvent.Attrs.Device|], "d");""", "Device", "DeviceEvent")]
    [Arguments("""_ = events.Update(key).Add([|DeviceEvent.Attrs.Shard|], 1);""", "Shard", "DeviceEvent")]
    [Arguments("""_ = events.Update(key).Remove([|DeviceEvent.Attrs.Id|]);""", "Id", "DeviceEvent")]
    [Arguments("""_ = events.Update(key).SetIfNotExists([|DeviceEvent.Attrs.Id|], "i");""", "Id", "DeviceEvent")]
    [Arguments("""_ = events.Update(key).SetIfNotNull([|DeviceEvent.Attrs.Id|], "i");""", "Id", "DeviceEvent")]
    [Arguments("""_ = client.TransactWrite().Update(events, key).SetIfNotNull([|DeviceEvent.Attrs.At|], now);""", "At", "DeviceEvent")]
    [Arguments("""_ = customers.Update(customerKey).Set([|Customer.Attrs.Id|], "c");""", "Id", "Customer")]
    [Arguments("""_ = customers.Update(customerKey).Add([|Customer.Attrs.Region|], 1);""", "Region", "Customer")]
    [Arguments("""_ = taggeds.Update(default(Tagged.Keys.PrimaryKey)).Delete([|Tagged.Attrs.Tags|], []);""", "Tags", "Tagged")]
    [Arguments("""_ = events.Update(key).Set(DeviceEvent.Attrs.Count, 1).Set([|DeviceEvent.Attrs.At|], now).Add(DeviceEvent.Attrs.Count, 1);""", "At", "DeviceEvent")]
    [Arguments("""_ = client.TransactWrite().Update(events, key).Set([|DeviceEvent.Attrs.At|], now);""", "At", "DeviceEvent")]
    [Arguments("""_ = client.TransactWrite().Update(events, key).If(DeviceEvent.Attrs.Count.Exists()).Remove([|DeviceEvent.Attrs.Id|]);""", "Id", "DeviceEvent")]
    [Arguments("""_ = derived.Update(default(DerivedItem.Keys.PrimaryKey)).Set([|BaseItem.Attrs.Id|], "b");""", "Id", "DerivedItem")]
    public async Task Analyze_WriteOfATableKeyInputByHandle_ReportsTheHandle(string statement, string property, string entity)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA205 Warning: '{property}' is part of the key of the table of '{entity}', and DynamoDB cannot change a key. Delete the item and put a new one."));
    }

    [Test]
    [Arguments("""_ = events.Update(key).Set(DeviceEvent.Attrs.Payload, "p");""")]
    [Arguments("""_ = events.Update(key).Set(DeviceEvent.Attrs.Level, "high");""")]
    [Arguments("""_ = events.Update(key).SetIfNotNull(DeviceEvent.Attrs.Level, "high");""")]
    [Arguments("""_ = events.Update(key).SetIfNotNull(DeviceEvent.Attrs.Payload, null);""")]
    [Arguments("""_ = events.Update(key).Remove(DeviceEvent.Attrs.Level);""")]
    [Arguments("""_ = events.Update(key).Set("At", "raw");""")]
    [Arguments("""_ = events.Update(key).Set($"{DeviceEvent.Attrs.Count} = {DeviceEvent.Attrs.Count} + {1}");""")]
    [Arguments("""_ = events.Update(key).If(DeviceEvent.Attrs.At.Exists());""")]
    [Arguments("""_ = events.Update(key).Set(DeviceEvent.Attrs.Origin.At(Address.Attrs.Street), "s");""")]
    [Arguments("""_ = events.Update(key).Set(Customer.Attrs.At, "not the key of this entity");""")]
    [Arguments("""_ = customers.Update(customerKey).Set(Customer.Attrs.At, "a");""")]
    [Arguments("""_ = shop.Update("a").Set(DeviceEvent.Attrs.At, now);""")]
    [Arguments("""_ = client.TransactWrite().Update(shop, "a").Set(DeviceEvent.Attrs.At, now);""")]
    [Arguments("""var handle = DeviceEvent.Attrs.At; _ = events.Update(key).Set(handle, now);""")]
    [Arguments("""UpdateStep step = default; _ = step.Set(DeviceEvent.Attrs.At, now);""")]
    [Arguments("""_ = addresses.Update(default(Address.Key)).Set(Address.Attrs.Street, "s");""")]
    [Arguments("""_ = hands.Update(default(Hand.Key)).Set(Hand.Attrs.X, "s");""")]
    public async Task Analyze_WriteThatIsNoInputOfTheTableKey_ReportsNothing(string statement)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = visits.Update(visitKey).Set([|Visit.Attrs.Message|], note);""", "Message", "Arrival", "A#{Arrival}", "ByArrival")]
    [Arguments("""_ = visits.Update(visitKey).Set([|Visit.Attrs.Message.At(Note.Attrs.At)|], now);""", "Message", "Arrival", "A#{Arrival}", "ByArrival")]
    [Arguments("""_ = visits.Update(visitKey).Remove([|Visit.Attrs.Message.At(Note.Attrs.At)|]);""", "Message", "Arrival", "A#{Arrival}", "ByArrival")]
    [Arguments("""_ = visits.Update(visitKey).Remove([|Visit.Attrs.Message|]);""", "Message", "Arrival", "A#{Arrival}", "ByArrival")]
    [Arguments("""_ = visits.Update(visitKey).SetIfNotNull([|Visit.Attrs.Message|], note);""", "Message", "Arrival", "A#{Arrival}", "ByArrival")]
    [Arguments("""_ = visits.Update(visitKey).Add([|Visit.Attrs.Guests|], 1);""", "Guests", "Load", "{Load}", "ByLoad")]
    [Arguments("""_ = visits.Update(visitKey).SetIfNotExists([|Visit.Attrs.Guests|], 1);""", "Guests", "Load", "{Load}", "ByLoad")]
    [Arguments("""_ = visits.Update(visitKey).Delete([|Visit.Attrs.Tags|], []);""", "Tags", "Load", "{Load}", "ByLoad")]
    [Arguments("""_ = client.TransactWrite().Update(visits, visitKey).Set([|Visit.Attrs.Message.At(Note.Attrs.At)|], now);""", "Message", "Arrival", "A#{Arrival}", "ByArrival")]
    public async Task Analyze_WriteOfWhatAComputedInputOfAnIndexIsDerivedFrom_ReportsTheHandle(string statement, string property, string computed, string template, string index)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA219 Warning: '{property}' feeds '{computed}', which the key template {template} of the index {index} is computed from, so this update throws when it ends. Put the changed entity instead."));
    }

    [Test]
    [Arguments("""_ = visits.Update(visitKey).Set([|Visit.Attrs.Load|], 1);""")]
    [Arguments("""_ = visits.Update(visitKey).Remove([|Visit.Attrs.Load|]);""")]
    [Arguments("""_ = visits.Update(visitKey).Add([|Visit.Attrs.Load|], 1);""")]
    [Arguments("""_ = client.TransactWrite().Update(visits, visitKey).Set([|Visit.Attrs.Load|], 1);""")]
    public async Task Analyze_WriteOfAComputedInputOfAnIndex_ReportsTheHandle(string statement)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA219 Warning: 'Load' is computed by its getter for the key template {Load} of the index ByLoad, so this update throws when it ends. Put the changed entity instead."));
    }

    [Test]
    [Arguments("""_ = shifts.Update(shiftKey).Set([|Shift.Attrs.Name|], "n");""", "Name")]
    [Arguments("""_ = shifts.Update(shiftKey).Set([|Shift.Attrs.Message.At(Note.Attrs.At)|], now);""", "Message")]
    public async Task Analyze_WriteOfWhatAComputedInputOfTheTableKeyIsDerivedFrom_ReportsDyna205(string statement, string property)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA205 Warning: '{property}' is part of the key of the table of 'Shift', and DynamoDB cannot change a key. Delete the item and put a new one."));
    }

    [Test]
    [Arguments("""_ = visits.Update(visitKey).Set("Message", "raw");""")]
    [Arguments("""_ = visits.Update(visitKey).Set("load", 1);""")]
    [Arguments("""_ = visits.Update(visitKey).Set(Visit.Attrs.Remark, "r");""")]
    [Arguments("""_ = visits.Update(visitKey).SetIfNotNull(Visit.Attrs.Remark, null);""")]
    [Arguments("""_ = visits.Put(new Visit { Id = "a", Message = note, Slot = "s" });""")]
    [Arguments("""_ = customers.Update(customerKey).Set(Visit.Attrs.Message, note);""")]
    [Arguments("""_ = shop.Update("a").Set(Visit.Attrs.Message, note);""")]
    [Arguments("""_ = visits.Update(visitKey).If(Visit.Attrs.Message.Exists());""")]
    [Arguments("""var handle = Visit.Attrs.Message; _ = visits.Update(visitKey).Set(handle, note);""")]
    public async Task Analyze_WriteThatLeavesWhatAComputedInputIsDerivedFromAlone_ReportsNothing(string statement)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_GenericHelper_ReportsNothing()
    {
        // Arrange (the command in a helper can be of any entity)
        var source = AnalyzerRun.Member("""private static TCommand Run<TCommand>(TCommand command) where TCommand : IUpdatable<TCommand>, allows ref struct => command.Set(DeviceEvent.Attrs.At, DateTimeOffset.UnixEpoch);""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Revision = 3 }).[|IfNotExists|]();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Revision = 1 }).ReturnOld().[|IfNotExists|]().MaxAttempts(1);""")]
    [Arguments("""_ = locks.Put(held with { Revision = 3 }).[|IfNotExists|]();""")]
    [Arguments("""_ = locks.Put(held with { Owner = "c", Revision = 1 }).ReturnOld().[|IfNotExists|]();""")]
    [Arguments("""const long Next = 2; _ = locks.Put(held with { Revision = Next }).[|IfNotExists|]();""")]
    [Arguments("""_ = locks.Put(item: new Locked { Id = "a", Owner = "b", Revision = 3 }).[|IfNotExists|]();""")]
    [Arguments("""_ = client.TransactWrite().Put(locks, new Locked { Id = "a", Owner = "b", Revision = 3 }).[|IfNotExists|]();""")]
    [Arguments("""_ = client.TransactWrite().Put(shop, new Item()).Put(locks, held with { Revision = 3 }).ReturnOldOnFailure().[|IfNotExists|]();""")]
    public async Task Analyze_IfNotExistsOnAPutThatShowsAVersionAboveZero_ReportsTheName(string statement)
    {
        // Arrange (the shape of a first write that starts counting at 1, which throws when the command ends)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, VersionMessage));
    }

    [Test]
    [Arguments("""_ = locks.Put(held).IfNotExists();""")]
    [Arguments("""_ = locks.Put(Make()).IfNotExists();""")]
    [Arguments("""_ = locks.Put(held).ReturnOld().IfNotExists().MaxAttempts(1);""")]
    [Arguments("""var put = locks.Put(held with { Revision = 3 }); _ = put.IfNotExists();""")]
    [Arguments("""var put = locks.Put(held); put = put.ReturnOld(); put = put.IfNotExists();""")]
    [Arguments("""var item = new Locked { Id = "a", Owner = "b", Revision = 3 }; _ = locks.Put(item).IfNotExists();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Revision = held.Revision }).IfNotExists();""")]
    [Arguments("""_ = locks.Put(held with { Revision = held.Revision + 1 }).IfNotExists();""")]
    [Arguments("""_ = client.TransactWrite().Put(locks, held).IfNotExists();""")]
    public async Task Analyze_VersionThatIsNotKnownHere_ReportsNothing(string statement)
    {
        // Arrange (at version 0 the two conditions agree, and that is what a first write is, so an unknown version says nothing)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = locks.Put(held with { Revision = 0 }).IfNotExists();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Revision = 0 }).IfNotExists();""")]
    [Arguments("""_ = locks.Put(held with { Owner = "c" }).IfNotExists();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Tag = "t" }).IfNotExists();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Revision = 3 }).IgnoreVersion().IfNotExists();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b", Revision = 3 }).IfNotExists().IgnoreVersion();""")]
    [Arguments("""_ = client.TransactWrite().Put(locks, held with { Revision = 3 }).IgnoreVersion().IfNotExists();""")]
    [Arguments("""_ = locks.Put(held with { Revision = 3 }).IfExists();""")]
    [Arguments("""_ = locks.Put(held).IfExists();""")]
    [Arguments("""_ = locks.Put(new Locked { Id = "a", Owner = "b" }).IfNotExists();""")]
    [Arguments("""_ = locks.Update(lockKey).IfVersion(1).IfNotExists();""")]
    [Arguments("""_ = locks.Delete(lockKey).IgnoreVersion().IfNotExists();""")]
    [Arguments("""_ = customers.Put(null!).IfNotExists();""")]
    [Arguments("""_ = shop.Put(new Item()).IfNotExists();""")]
    [Arguments("""_ = shop.Put().IfNotExists();""")]
    [Arguments("""_ = client.TransactWrite().Put(customers, null!).IfNotExists();""")]
    [Arguments("""_ = client.TransactWrite().Put(shop, new Item()).IfNotExists();""")]
    [Arguments("""_ = client.TransactWrite().Put(locks, held).IgnoreVersion().IfNotExists();""")]
    [Arguments("""_ = client.TransactWrite().Delete(locks, lockKey).IfNotExists();""")]
    [Arguments("""_ = client.TransactWrite().Unique(customers, null!, out var taken);""")]
    [Arguments("""var put = locks.Put(held); put = put.IgnoreVersion(); put = put.IfNotExists();""")]
    [Arguments("""PutStep step = default; _ = step.IfNotExists();""")]
    [Arguments("""_ = shop.CreateTable().IfNotExists();""")]
    [Arguments("""_ = locks.Put(new Locked()).IfNotExists();""")]
    [Arguments("""_ = Start().IfNotExists();""")]
    [Arguments("""_ = addresses.Put(new Address("s")).IfNotExists();""")]
    [Arguments("""_ = hands.Put(new Hand()).IfNotExists();""")]
    public async Task Analyze_ConditionThatCanHold_ReportsNothing(string statement)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_IfNotExistsInAGenericHelper_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member("""private static TStep Run<TStep>(TStep step) where TStep : ITransactWriteStep<TStep>, allows ref struct => step.IfNotExists();""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = events.Index([|"Alertz"|]);""", "DeviceEvent", "Alertz")]
    [Arguments("""_ = events.Index([|"alerts"|]);""", "DeviceEvent", "alerts")]
    [Arguments("""_ = events.Index(name: [|"Nope"|]);""", "DeviceEvent", "Nope")]
    [Arguments("""_ = customers.Index([|"Any"|]);""", "Customer", "Any")]
    [Arguments("""const string Name = "Alertz"; _ = events.Index([|Name|]);""", "DeviceEvent", "Alertz")]
    [Arguments("""_ = shop.Index<DeviceEvent>([|"Alertz"|]);""", "DeviceEvent", "Alertz")]
    [Arguments("""_ = EntityTableExtensions.Index(events, [|"Alertz"|]);""", "DeviceEvent", "Alertz")]
    public async Task Analyze_ConstantIndexThatTheEntityDoesNotDeclare_ReportsTheName(string statement, string entity, string index)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA207 Warning: '{entity}' declares no index '{index}'. Take the name from {entity}.Indexes."));
    }

    [Test]
    [Arguments("""_ = events.Index("Alerts");""")]
    [Arguments("""_ = events.Index(DeviceEvent.Indexes.Alerts);""")]
    [Arguments("""_ = shop.Index<DeviceEvent>(DeviceEvent.Indexes.Alerts);""")]
    [Arguments("""_ = shop.Index("Alertz", "pk");""")]
    [Arguments("""_ = events.Index("Alertz", "pk", "sk");""")]
    [Arguments("""var name = args[0]; _ = events.Index(name);""")]
    [Arguments("""_ = locks.Index(Locked.Indexes.ByOwner);""")]
    [Arguments("""_ = hands.Index("Any");""")]
    public async Task Analyze_IndexThatIsDeclaredOrNotConstant_ReportsNothing(string statement)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_IndexOfATypeParameter_ReportsNothing()
    {
        // Arrange (the type argument is known at the call of the helper, not here)
        var source = AnalyzerRun.Member("""private static Index<T> Run<T>(Table<T> table) where T : IDynamoDbEntity<T> => table.Index("Alerts");""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = events.CreateTable().[|Including<Customer>|]();""", "DeviceEvent", "(pk, sk)", "Customer", "(Id, Region)")]
    [Arguments("""_ = events.CreateTable().[|Including<Locked>|]();""", "DeviceEvent", "(pk, sk)", "Locked", "(pk)")]
    [Arguments("""_ = events.CreateTable().Provisioned(1, 1).Including<DeviceEvent>().[|Including<Customer>|]();""", "DeviceEvent", "(pk, sk)", "Customer", "(Id, Region)")]
    [Arguments("""_ = shop.CreateTable().Including<Locked>().Including<Visit>().[|Including<DeviceEvent>|]();""", "Locked", "(pk)", "DeviceEvent", "(pk, sk)")]
    [Arguments("""_ = shop.CreateTable().Including<Hand>().Including<Locked>().[|Including<Customer>|]();""", "Locked", "(pk)", "Customer", "(Id, Region)")]
    public async Task Analyze_IncludedEntityWithAnotherKey_Warns(string statement, string first, string firstKey, string included, string includedKey)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA225 Warning: '{first}' has the key {firstKey}, and '{included}' has {includedKey}, so they cannot share a table and Including throws ArgumentException"));
    }

    [Test]
    [Arguments("""_ = locks.CreateTable().Including<Visit>().Including<Shift>();""")]
    [Arguments("""_ = shop.CreateTable().Including<Locked>();""")]
    [Arguments("""_ = shop.CreateTable().Including<Hand>().Including<Customer>();""")]
    [Arguments("""_ = events.CreateTable().Including<Hand>();""")]
    [Arguments("""_ = addresses.CreateTable().Including<Customer>();""")]
    [Arguments("""var create = events.CreateTable(); _ = create.Including<Customer>();""")]
    public async Task Analyze_IncludedEntityWithTheSameKeyOrThatCannotBeCompared_ReportsNothing(string statement)
    {
        // Arrange (a type without a model, a table of a document, and a command in a variable say nothing)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_IncludingOfATypeParameter_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member("""private static CreateTableCommand Run<T>(Table<DeviceEvent> table) where T : IDynamoDbEntity<T> => table.CreateTable().Including<T>();""", Entities);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EntityCommandAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CreateTableOfAStaticMethodUnderTheNamespaceOfTheLibrary_HasNoTable()
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
                }

                public static class Helpers
                {
                    public static CreateTableCommand CreateTable() => null!;

                    public static CreateTableCommand Run() => CreateTable().Including<Note>();
                }
            }
            """;

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EntityCommandAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = locks.BatchWrite().[|Put|](held).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().[|PutAll|]([held]).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().[|Delete|](lockKey).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().[|Delete|](Locked.Keys.Of("a")).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().[|DeleteAll|]([lockKey]).ToRequestJson();""")]
    [Arguments("""_ = shop.BatchWrite().Put(new Item()).[|Put|](locks, held).ToRequestJson();""")]
    [Arguments("""_ = shop.BatchWrite().[|Delete|](locks, lockKey).ToRequestJson();""")]
    [Arguments("""_ = shop.BatchWrite().[|Delete|](locks, Locked.Keys.Of("a")).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().[|Put|](held).Put(held).Delete(lockKey).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().ReturnCapacity().[|Put|](held).ToRequestJson();""")]
    [Arguments("""_ = Locks().BatchWrite().[|Put|](held).ToRequestJson();""")]
    [Arguments("""var batch = locks.BatchWrite(); batch = batch.[|Put|](held); _ = batch.ToRequestJson();""")]
    [Arguments("""if (args.Length == 0) { return; } _ = locks.BatchWrite().[|Put|](held).ToRequestJson();""")]
    public async Task Analyze_BatchWriteOfAnEntityWithAVersion_ReportsTheFirstWrite(string statement)
    {
        // Arrange (BatchWriteOperation marks each write of such a table, and the terminal throws for it)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, BatchMessage));
    }

    [Test]
    [Arguments("""_ = locks.BatchWrite().IgnoreVersion().Put(held).ToRequestJson();""")]
    [Arguments("""_ = locks.BatchWrite().Put(held).Delete(lockKey).IgnoreVersion().ToRequestJson();""")]
    [Arguments("""var batch = locks.BatchWrite().Put(held); _ = batch.IgnoreVersion().ToRequestJson();""")]
    [Arguments("""_ = shop.BatchWrite().IgnoreVersion().Put(locks, held).ToRequestJson();""")]
    [Arguments("""_ = customers.BatchWrite().Put(null!).Delete(customerKey).ToRequestJson();""")]
    [Arguments("""_ = shop.BatchWrite().Put(new Item()).Delete("a").Put(customers, null!).ToRequestJson();""")]
    [Arguments("""_ = addresses.BatchWrite().Put(new Address("s")).ToRequestJson();""")]
    [Arguments("""_ = hands.BatchWrite().Put(new Hand()).ToRequestJson();""")]
    [Arguments("""_ = locks.Put(held).ToRequestJson();""")]
    [Arguments("""_ = locks.Delete(lockKey).IfVersion(1).ToRequestJson();""")]
    [Arguments("""Keep(locks.BatchWrite().Put(held));""")]
    [Arguments("""Give(out var batch);""")]
    public async Task Analyze_BatchWriteThatSendsNoVersionOrLeavesTheMember_ReportsNothing(string statement)
    {
        // Arrange (IgnoreVersion anywhere in the member, an entity without a version, and a batch that a caller can still give IgnoreVersion to)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("private static BatchWriteCommand<Locked> Run() => locks.BatchWrite().Put(held);")]
    [Arguments("private static string Run<T>(Table<T> table, T item) where T : IDynamoDbEntity<T> => table.BatchWrite().Put(item).ToRequestJson();")]
    [Arguments("private static string Run<T>(BatchWriteCommand<Locked> batch, Table<T> table, T item) where T : IDynamoDbEntity<T> => batch.Put(table, item).ToRequestJson();")]
    public async Task Analyze_BatchWriteInAHelper_ReportsNothing(string member)
    {
        // Arrange (a batch that is returned, and a write of a type parameter, which can be of any entity)
        var source = AnalyzerRun.Member(Fields + member);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = locks.[|Update|](lockKey).Set(Locked.Attrs.Owner, "o").ToRequestJson();""", "update")]
    [Arguments("""_ = locks.[|Delete|](lockKey).ToRequestJson();""", "delete")]
    [Arguments("""var update = locks.[|Update|](lockKey); _ = update.ToRequestJson();""", "update")]
    [Arguments("""_ = client.TransactWrite().[|Update|](locks, lockKey).Set(Locked.Attrs.Owner, "o").ToRequestJson();""", "update")]
    [Arguments("""_ = client.TransactWrite().[|Update|](locks, lockKey, out _).ToRequestJson();""", "update")]
    [Arguments("""_ = client.TransactWrite().[|Delete|](locks, lockKey).ToRequestJson();""", "delete")]
    [Arguments("""_ = client.TransactWrite().[|Update|](locks, lockKey).Update(locks, lockKey).IfVersion(1).ToRequestJson();""", "update")]
    [Arguments("""_ = client.TransactWrite().[|Delete|](locks, lockKey).Put(locks, held).ToRequestJson();""", "delete")]
    [Arguments("""_ = client.TransactWrite().[|Delete|](locks, lockKey).IfExists().ToRequestJson();""", "delete")]
    public async Task Analyze_WriteByKeyOfAVersionedEntityWithoutAVersionClause_ReportsTheVerb(string statement, string write)
    {
        // Arrange (a key carries no version, so the command throws when it is sent)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA239 Error: 'Locked' has a [Version], and this {write} by key does not say how it treats it, so it throws InvalidOperationException when it is sent. Pass the version last read with IfVersion(n), or write without the check with IgnoreVersion()."));
    }

    [Test]
    [Arguments("""_ = locks.Update(lockKey).IfVersion(1).ToRequestJson();""")]
    [Arguments("""_ = locks.Update(lockKey).Set(Locked.Attrs.Owner, "o").IgnoreVersion().ToRequestJson();""")]
    [Arguments("""_ = locks.Delete(lockKey).IgnoreVersion().ToRequestJson();""")]
    [Arguments("""var update = locks.Update(lockKey); _ = update.IfVersion(2).ToRequestJson();""")]
    [Arguments("""_ = locks.Delete(held).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(customerKey).ToRequestJson();""")]
    [Arguments("""_ = locks.Update(lockKey);""")]
    [Arguments("""_ = client.TransactWrite().Update(locks, lockKey).IfVersion(1).ToRequestJson();""")]
    [Arguments("""_ = client.TransactWrite().Delete(locks, lockKey).IgnoreVersion().ToRequestJson();""")]
    [Arguments("""_ = client.TransactWrite().Update(events, key).ToRequestJson();""")]
    [Arguments("""_ = client.TransactWrite().Update(shop, "a").ToRequestJson();""")]
    [Arguments("""UpdateStep step = client.TransactWrite().Update(locks, lockKey); _ = step.IfVersion(1).ToRequestJson();""")]
    [Arguments("""_ = client.TransactWrite().Update(locks, lockKey).Checked().ToRequestJson();""")]
    [Arguments("""_ = Steps.Render(client.TransactWrite().Update(locks, lockKey));""")]
    [Arguments("""_ = new Holder(client.TransactWrite().Update(locks, lockKey));""")]
    public async Task Analyze_WriteByKeyThatSaysHowItTreatsTheVersionOrCannotBeFollowed_ReportsNothing(string statement)
    {
        // Arrange (a clause on the write itself, an entity or no version, and a step that goes where its clauses cannot be seen)
        var source = AnalyzerRun.Member(Fields + "public static void Run(string[] args)\n{\n" + statement + "\n}\n", Outside);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_WriteByKeyOfATypeParameter_ReportsNothing()
    {
        // Arrange (it can be of any entity)
        var source = AnalyzerRun.Member(Fields + "private static string Run<T, TKey>(Table<T> table, TKey key) where T : IDynamoDbEntity<T> where TKey : IKey<T> => table.Delete(key).ToRequestJson();");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CodeWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var source = "public static class Code { public static string Run(string a) => a.Trim(); }";

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EntityCommandAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Run(string statement) => AnalyzerRun.Member(Fields + "public static void Run(string[] args)\n{\n" + statement + "\n}\n");

    private static Task<string> ReportAsync(string source) => Shared.ReportWithAsync(source, new EntityCommandAnalyzer());
}
