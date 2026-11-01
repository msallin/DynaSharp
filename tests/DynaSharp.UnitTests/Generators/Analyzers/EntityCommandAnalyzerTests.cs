using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA205 to DYNA207: what a command says about its entity that the model of the entity contradicts.</summary>
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
        public partial record Address(string Street);

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
        private static readonly DeviceEvent.Keys.PrimaryKey key = default;
        private static readonly Customer.Keys.PrimaryKey customerKey = default;
        private static readonly Locked.Keys.PrimaryKey lockKey = default;

        """;

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
    [Arguments("""_ = addresses.Update("a").Set(Address.Attrs.Street, "s");""")]
    [Arguments("""_ = hands.Update("a").Set(Hand.Attrs.X, "s");""")]
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
    public async Task Analyze_GenericHelper_ReportsNothing()
    {
        // Arrange (the command in a helper can be of any entity)
        var source = AnalyzerRun.Member("""private static TCommand Run<TCommand>(TCommand command) where TCommand : IUpdatable<TCommand>, allows ref struct => command.Set(DeviceEvent.Attrs.At, DateTimeOffset.UnixEpoch);""", Entities);

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
        var source = AnalyzerRun.Member("""private static TStep Run<TStep>(TStep step) where TStep : ITransactWriteStep<TStep>, allows ref struct => step.IfNotExists();""", Entities);

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
    [Arguments("""_ = TableExtensions.Index(events, [|"Alertz"|]);""", "DeviceEvent", "Alertz")]
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
        var source = AnalyzerRun.Member("""private static Index<T> Run<T>(Table<T> table) where T : IDynamoDbEntity<T> => table.Index("Alerts");""", Entities);

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
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Run(string statement) => AnalyzerRun.Member(Fields + "public static void Run(string[] args)\n{\n" + statement + "\n}\n", Entities);

    private static Task<string> ReportAsync(string source) => AnalyzerRun.ReportWithAsync(source, new EntityCommandAnalyzer());
}
