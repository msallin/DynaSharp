using System.Text;
using DynaSharp.Expressions;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// The promise of the engine: once the pool of the client is warm, building a command allocates nothing, whatever it holds. The measurement
// is of this thread between two reads of its counter, so nothing that runs in parallel touches it. Each test warms the state, the array
// pool and the code paths first, and takes the smallest of a few rounds.
public sealed class CommandAllocationTests
{
    private const string PartitionKey = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19";
    private const string SortKey = "ORDER#2026-10-31T12:34:56.789Z";
    private const int Rounds = 2000;

    [Test]
    public async Task Build_UpdateWithInterpolatedCondition_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(
            table.Update(PartitionKey, SortKey).Add("views", 1).Set("status", "paid").If($"{Attr("status")} = {"open"} AND size({Attr("tags")}) < {10}")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_GetWithAGeneratedCompositeKey_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var orders = client.Table<KeyedOrder>("Shop");

        // Act (the key goes from its values into the request without a string)
        var allocated = Measure(orders, static table => CommandHelpers.FinishAndRelease(
            table.Get(KeyedOrder.Keys.Of(Guid.Empty, DateTimeOffset.UnixEpoch, Guid.Empty)).Consistent().Handle));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_UpdateWithStructCondition_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(
            table.Update(PartitionKey, SortKey).Set("city", "Bern").Remove("legacy").If(Attr("version").Eq(3).And(Attr("status").Ne("closed"))).ReturnNew().ReturnCapacity().MaxAttempts(1)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedUpdateThroughHandles_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var allocated = Measure(customers, static table => CommandHelpers.FinishAndRelease(
            table.Update(default(StaticKey)).Add(Customer.Attrs.OrderCount, 1).Set(Customer.Attrs.Email, "a@b.ch").If(Customer.Attrs.OrderCount.Lt(100))));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedUpdateWithANullableValue_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var allocated = Measure(customers, static table => CommandHelpers.FinishAndRelease(
            table.Update(default(StaticKey)).Set(Customer.Attrs.OrderCount, (int?)7).Set(Customer.Attrs.Email, "a@b.ch")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedUpdateWithANullableValueThatIsNull_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var allocated = Measure(customers, static table => CommandHelpers.FinishAndRelease(
            table.Update(default(StaticKey)).Set(Customer.Attrs.OrderCount, (int?)null)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedUpdateOfAnEntityWithAnIndexOfLiterals_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var notices = client.Table<Notice>("Shop");

        // Act
        var allocated = Measure(notices, static table => CommandHelpers.FinishAndRelease(
            table.Update(Notice.Keys.Of("n1")).Set(Notice.Attrs.Note, "x")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_QueryOfAnIndexByItsPartitionKey_AllocatesNothing()
    {
        // Arrange (the key is made before: its string is what Format costs)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<TenantInvoice>("Shop").Index(TenantInvoice.Indexes.ByStatus);
        var key = TenantInvoice.Keys.ByStatusPk.Of("acme", InvoiceState.Paid);

        // Act
        var allocated = Measure((index, key), static input => CommandHelpers.FinishAndRelease(QueryOf(input.index, input.key)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedUpdateBesideAnIndexKeyOfATableKeyInput_AllocatesNothing()
    {
        // Arrange (the key would only be looked at for an update of what the index key is made of)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Shop");

        // Act
        var allocated = Measure(invoices, static table => CommandHelpers.FinishAndRelease(
            table.Update(TenantInvoice.Keys.Of("acme", "2026-09", 2)).Set(TenantInvoice.Attrs.Note, "x")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedUpdateWithAnActionOnTheDiscriminator_AllocatesNothing()
    {
        // Arrange (this is the walk over the clauses to the end, and the one that finds it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var allocated = Measure(customers, static table => CommandHelpers.FinishAndRelease(
            table.Update(default(StaticKey)).Set("a", 1).Add("b", 2).If(Attr("_type").Exists()).Set("_type", "CUSTOMER")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_PutAttributeByAttribute_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(
            table.Put().With("pk", PartitionKey).With("sk", SortKey).With("orderId", 9182736455).With("total", 1249.95m).With("shipped", true).IfNotExists().ReturnOldOnFailure()));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_PutOfAnEntity_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var sessions = client.Table<Session>("Sessions");

        // Act
        var allocated = Measure(sessions, static table => CommandHelpers.FinishAndRelease(table.Put(new Session("s")).IfNotExists()));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_GetWithProjection_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(table.Get(PartitionKey, SortKey).Consistent().Project("fullName", "address.city").Handle));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_DeleteWithCondition_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(table.Delete(PartitionKey, SortKey).If(Attr("count").Eq(0)).IfExists().ReturnOld()));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_UpdateThatReturnsAnAttribute_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(table.Update(PartitionKey, SortKey).Add("count", 1).Return(Attr<int>("count")).Handle));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_RepeatedNamesAndValues_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(
            table.Update(PartitionKey, SortKey).Set("a", 1).Set("b", 1).Set("c", 1).If($"{Attr("a")} = {1} OR {Attr("b")} = {1}")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_QueryWithEverything_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(
            table.Query(PartitionKey, global::DynaSharp.SortKey.BeginsWith("ORDER#")).Filter($"{Attr("total")} > {100}").Filter(Attr("status").Eq("open")).Project("total", "status").Limit(25).Descending().Consistent().ReturnCapacity().MaxAttempts(1)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TypedQueryWithProjection_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<Customer>("Shop").Index("ByName");

        // Act
        var allocated = Measure(index, static table => CommandHelpers.FinishAndRelease(table.Query(PartitionKey, global::DynaSharp.SortKey.Gt("a")).Filter(Customer.Attrs.OrderCount.Gt(1)).Project("email")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_ScanOfASegment_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(table.Scan().Filter(Attr("status").Eq("open")).Project("total").Limit(25).Segment(2, of: 8)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_PutOfAnEntityWithAVersion_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var ticket = new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

        // Act
        var allocated = Measure((tickets, ticket), static input => CommandHelpers.FinishAndRelease(input.tickets.Put(input.ticket)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Send_PutOfAClassEntityWithAVersion_SettlesWithoutAllocating()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var ticket = new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

        // Act
        var allocated = Measure((tickets, ticket), static input =>
        {
            var state = DynaSharp.Fluent.WriteRules.Settle(((CommandHandle)input.tickets.Put(input.ticket)).State!);
            var version = state.Version;
            var length = state.Finish().Count;
            state.Release(version);
            return length;
        });

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Send_PutIgnoreVersionOfAClassEntity_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var ticket = new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

        // Act
        var allocated = Measure((tickets, ticket), static input =>
        {
            var state = DynaSharp.Fluent.WriteRules.Settle(((CommandHandle)input.tickets.Put(input.ticket).IgnoreVersion()).State!);
            var version = state.Version;
            var length = state.Finish().Count;
            state.Release(version);
            return length;
        });

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_UpdateIfVersion_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act (a handle of an entity with templates that feeds no key)
        var allocated = Measure(tickets, static table => CommandHelpers.FinishAndRelease(table.Update(PartitionKey, SortKey).IfVersion(3).Set(Ticket.Attrs.Note, "n").Add(Ticket.Attrs.Votes, 1)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Send_UpdateIfVersion_SettlesWithoutAllocating()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act (what is settled at the terminal, for a version and for a key)
        var allocated = Measure(tickets, static table =>
        {
            var state = DynaSharp.Fluent.WriteRules.Settle(((CommandHandle)table.Update(PartitionKey, SortKey).IfVersion(3)).State!);
            var version = state.Version;
            var length = state.Finish().Count;
            state.Release(version);
            return length;
        });

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_DeleteOfAnEntityWithoutAVersion_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var profiles = client.Table<UserProfile>("Shop");
        var profile = new UserProfile { Id = "alice" };

        // Act
        var allocated = Measure((profiles, profile), static input => CommandHelpers.FinishAndRelease(input.profiles.Delete(input.profile)));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Send_DeleteOfAClassEntityWithAVersion_SettlesWithoutAllocating()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var ticket = new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

        // Act
        var allocated = Measure((tickets, ticket), static input =>
        {
            var state = DynaSharp.Fluent.WriteRules.Settle(((CommandHandle)input.tickets.Delete(input.ticket)).State!);
            var version = state.Version;
            var length = state.Finish().Count;
            state.Release(version);
            return length;
        });

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Send_DeleteOfAStructEntityWithAVersion_SettlesWithoutABox()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Shop");
        var gauge = new Gauge { Id = "g", Revision = 3 };

        // Act
        var allocated = Measure((gauges, gauge), static input =>
        {
            var state = DynaSharp.Fluent.WriteRules.Settle(((CommandHandle)input.gauges.Delete(input.gauge)).State!);
            var version = state.Version;
            var length = state.Finish().Count;
            state.Release(version);
            return length;
        });

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    // The promise of paging: the next page costs what the body takes, and that is a copy into arrays that are there. The state is warm, so nothing here may allocate.
    [Test]
    public async Task WritePageBody_ManyPages_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var state = client.Table("Shop", "pk", "sk").Query(PartitionKey, global::DynaSharp.SortKey.BeginsWith("ORDER#")).Filter(Attr("status").Eq("open")).Project("total").Handle.State!;
        state.PageKey.Append(Encoding.UTF8.GetBytes("""{"pk":{"S":"USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19"},"sk":{"S":"ORDER#2026-10-31T12:34:56.789Z"}}"""));

        // Act
        var allocated = Measure(state, static read => read.WritePageBody().Count);

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_BatchWriteOfEntities_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var sessions = client.Table<Session>("Sessions");
        var session = new Session("s");

        // Act
        var allocated = Measure((sessions, session), static input => CommandHelpers.FinishAndRelease(input.sessions.BatchWrite().Put(input.session).Put(input.session).Delete(input.sessions, default(SessionKey)).ReturnCapacity().Handle));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_BatchGetOfKeys_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure(shop, static table => CommandHelpers.FinishAndRelease(table.BatchGet().Key(PartitionKey, SortKey).Key(PartitionKey, "other").Consistent().Project("a", "b").Handle));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TransactionOfSeveralActions_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var sessions = client.Table<Session>("Sessions");
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure((client, sessions, shop), static input => CommandHelpers.FinishAndRelease(input.client.TransactWrite()
            .Put(input.sessions, new Session("s")).IfNotExists()
            .Update(input.shop, PartitionKey, SortKey).Add("n", 1).If($"{Attr("n")} < {10}").ReturnOldOnFailure()
            .Check(input.shop, PartitionKey, "other").If(Attr("a").Exists())
            .Token("token")));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TransactionHeldInACommandOverALoop_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act (the conversion of a step costs a handle on the stack)
        var allocated = Measure((client, shop), static input =>
        {
            var command = input.client.TransactWrite();
            for (var i = 0; i < 3; i++)
            {
                command = command.Update(input.shop, PartitionKey, SortKey).Add("n", 1);
            }

            return CommandHelpers.FinishAndRelease(command.Delete(input.shop, PartitionKey, "other"));
        });

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Build_TransactGet_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var allocated = Measure((client, shop), static input => CommandHelpers.FinishAndRelease(input.client.TransactGet().Get(input.shop, PartitionKey, SortKey, out _).Get(input.shop, PartitionKey, "other", out _).Handle));

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    // Warm, then measure. A background tier-up can allocate on this thread once, so the smallest of the last rounds is what the code costs.
    // dotnet format runs without the generator, which reports a named key as an error it could fix.
    private static QueryCommand<T> QueryOf<T>(Index<T> index, PartitionKey<T> key)
        where T : DynaSharp.Mapping.IDynamoDbEntity<T> => index.Query(key);

    private static long Measure<TState>(TState state, Func<TState, int> build)
    {
        for (var i = 0; i < Rounds; i++)
        {
            build(state);
        }

        var smallest = long.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            build(state);
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return smallest;
    }

    private readonly struct SessionKey : DynaSharp.Mapping.IKey<Session>
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName("id"u8);
            writer.WriteString(PartitionKey);
        }
    }

    private readonly struct StaticKey : DynaSharp.Mapping.IKey<Customer>
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(PartitionKey);
            writer.WriteAttributeName("sk"u8);
            writer.WriteString(SortKey);
        }
    }
}
