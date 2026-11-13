using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using DynaSharp.UnitTests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.UnitTests.Fluent;

// What the types of the API rule out: each statement is compiled against the library as a consumer would write it, and must fail with the
// error pinned here, so that a mistake in this source cannot pass for the guard. An error that disappears means a misuse has come back that
// only DynamoDB would report. The extension methods of ItemTableExtensions and EntityTableExtensions decide some of the codes: on an Index, Get and Delete find them and
// fail on their constraint, with no member of the Index to bind to. The rows of a test share a compilation, see Batch.
public sealed class CompileGuardTests
{
    private const string Declarations = """
        using System.Threading.Tasks;
        using DynaSharp;
        using DynaSharp.Mapping;
        using DynaSharp.Serialization;
        using DynaSharp.UnitTests.Mapping.KeyEntities;
        using static DynaSharp.Expr;

        public sealed record Order(string Id) : IDynamoDbEntity<Order>
        {
            public static DynamoDbEntityInfo Info { get; } = new("ORDER", "pk", null, []);
            public static void WriteKey(DynamoDbWriter writer, Order entity) => writer.WriteAttributeName("pk"u8);
            public static void WriteAttributes(DynamoDbWriter writer, Order entity) => writer.WriteAttributeName("pk"u8);
            public static Order ReadAttributes(ref DynamoDbReader reader) => new("a");
            public readonly struct Key : IKey<Order> { public void WriteKey(DynamoDbWriter writer) { } }
        }

        public sealed record Invoice(string Id) : IDynamoDbDiscriminated<Invoice>
        {
            public static DynamoDbEntityInfo Info { get; } = new("INVOICE", "pk", null, []);
            public static void WriteKey(DynamoDbWriter writer, Invoice entity) => writer.WriteAttributeName("pk"u8);
            public static void WriteAttributes(DynamoDbWriter writer, Invoice entity) => writer.WriteAttributeName("pk"u8);
            public static Invoice ReadAttributes(ref DynamoDbReader reader) => new("a");
        }

        public sealed record Payment(string Id) : IDynamoDbDiscriminated<Payment>
        {
            public static DynamoDbEntityInfo Info { get; } = new("PAYMENT", "pk", null, []);
            public static void WriteKey(DynamoDbWriter writer, Payment entity) => writer.WriteAttributeName("pk"u8);
            public static void WriteAttributes(DynamoDbWriter writer, Payment entity) => writer.WriteAttributeName("pk"u8);
            public static Payment ReadAttributes(ref DynamoDbReader reader) => new("a");
        }

        public sealed record Address(string City) : IDynamoDbDocument<Address>
        {
            public static void WriteAttributes(DynamoDbWriter writer, Address entity) => writer.WriteAttributeName("city"u8);
            public static Address ReadAttributes(ref DynamoDbReader reader) => new("a");
            public readonly struct Key : IKey<Address> { public void WriteKey(DynamoDbWriter writer) { } }
        }

        public static class Use
        {

        """;

    private const string Case = """
            public static async Task RunAsync(DynamoDbClient client)
            {
                var shop = client.Table("Shop", "pk", "sk");
                var orders = shop.Of<Order>();
                var index = shop.Index("ByStatus", "status");
                var invoices = shop.Of<Invoice>();
                var indexOfInvoices = invoices.Index("ByStatus", "status");
                AttributeValue[] shards = ["a", "b"];
                await Task.Yield();
                STATEMENT
            }

        """;

    private const string End = "}\n";

    private static readonly ConcurrentDictionary<string, Lazy<Batch>> Batches = new(StringComparer.Ordinal);

    [Test]
    [Arguments("""await index.Put(new Item());""", "CS1501")]
    [Arguments("""await index.Update("a");""", "CS1929")]
    [Arguments("""await index.Delete("a");""", "CS0311")]
    [Arguments("""await index.Get("a");""", "CS0311")]
    [Arguments("""await orders.Put();""", "CS7036")]
    [Arguments("""await orders.Put(new Order("a")).With("x", 1);""", "CS1929")]
    [Arguments("""_ = typeof(SetArrayConverter<string, StringConverter>);""", "CS0315")]
    [Arguments("""shop.Of<Address>();""", "CS0311")]
    [Arguments("""shop.Of<Item>();""", "CS0311")]
    [Arguments("""orders.CreateTable().Including<Address>();""", "CS0311")]
    [Arguments("""orders.CreateTable().Including<Item>();""", "CS0311")]
    [Arguments("""index.CreateTable();""", "CS1061")]
    [Arguments("""await index.DeleteTableAsync();""", "CS1061")]
    [Arguments("""client.Table<Address>("Shop");""", "CS0311")]
    [Arguments("""await orders.Get(default(Address.Key));""", "CS0315")]
    [Arguments("""await shop.Update("a").Add("n", 1).Return(Attr<int>("n")).Set("x", 1);""", "CS1061")]
    [Arguments("""await shop.Update("a").Add("n", 1).Return(Attr<int>("n")).If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""await shop.Update("a").Add("n", 1).Return(Attr<int>("n")).MaxAttempts(1);""", "CS1061")]
    [Arguments("""await shop.Update("a").Add("n", 1).Return(Attr<int>("n")).ReturnNew();""", "CS1061")]
    [Arguments("""await shop.Get("a").If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""await shop.Delete("a").ReturnNew();""", "CS1061")]
    [Arguments("""await shop.Update("a").If("attribute_exists(pk)");""", "CS0311")]
    [Arguments("""await shop.Scan().Parallel(8).PageAsync();""", "CS1061")]
    [Arguments("""await shop.Scan().Parallel(8).PageAsync(default, default);""", "CS1061")]
    [Arguments("""shop.Scan().Parallel(8).PagesAsync();""", "CS1061")]
    [Arguments("""shop.Scan().Parallel(8).ToRequestJson();""", "CS1061")]
    [Arguments("""_ = new DynamoDbClientOptions().ToRequestJson();""", "CS1061")]
    [Arguments("""await shop.Scan().Parallel(8).CountAsync();""", "CS1061")]
    [Arguments("""shop.Scan().Parallel(8).Filter(Attr("x").Exists());""", "CS1061")]
    [Arguments("""shop.Scan().Parallel(8).Limit(1);""", "CS1061")]
    [Arguments("""shop.Scan().Parallel(8).Segment(1, of: 2);""", "CS1061")]
    [Arguments("""await index.Scan().Parallel(8).PageAsync();""", "CS1061")]
    [Arguments("""await orders.Scan().Parallel(8).PageAsync();""", "CS1061")]
    [Arguments("""await shop.Query("a");""", "CS0315")]
    [Arguments("""await shop.Scan();""", "CS0315")]
    [Arguments("""await shop.Query("a").SendAsync();""", "CS0315")]
    [Arguments("""shop.Query("a").If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""shop.Query("a").Filter("x = y");""", "CS0311")]
    [Arguments("""shop.Scan().Filter("x = y");""", "CS0311")]
    [Arguments("""shop.Query("a").Parallel(2);""", "CS1061")]
    [Arguments("""shop.Query("a").Segment(0, of: 2);""", "CS1061")]
    [Arguments("""shop.Scan().Descending();""", "CS1061")]
    [Arguments("""shop.Query("a", SortKeyCondition.Ne("b"));""", "CS0117")]
    [Arguments("""client.TransactWrite().If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""client.TransactWrite().IfNotExists();""", "CS0315")]
    [Arguments("""client.TransactWrite().ReturnOldOnFailure();""", "CS0315")]
    [Arguments("""client.TransactWrite().Set("x", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).Add("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).Set("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).Remove("n");""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).SetIfNotExists("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).With("n", 1);""", "CS1929")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).ReturnOld();""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).Return(Attr("n"));""", "CS1061")]
    [Arguments("""await client.TransactWrite().Unique(shop, new Item(), out _).Add("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").Add("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").Set("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").Remove("n");""", "CS1061")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").ReturnOld();""", "CS1061")]
    [Arguments("""await client.TransactWrite().Check(shop, "a").Add("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Check(shop, "a").Set("n", 1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Check(shop, "a").Remove("n");""", "CS1061")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").ReturnOld();""", "CS1061")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").ReturnNew();""", "CS1061")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Return(Attr("n"));""", "CS1061")]
    [Arguments("""await client.TransactWrite().Update(orders, default(Address.Key));""", "CS0315")]
    [Arguments("""await client.TransactWrite().Delete(orders, default(Address.Key));""", "CS0315")]
    [Arguments("""await client.TransactWrite().Check(orders, default(Address.Key)).IfExists();""", "CS0315")]
    [Arguments("""await client.TransactWrite().Put(orders, new Item());""", "CS0411")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).IfNotExists(shop);""", "CS1501")]
    [Arguments("""await client.TransactGet().Get(orders, default(Address.Key), out _);""", "CS0315")]
    [Arguments("""await client.TransactGet().Get(shop, "a", default, out _).Consistent();""", "CS1061")]
    [Arguments("""await client.TransactGet().Get(shop, "a", default, out _).If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""await shop.BatchWrite().Put(new Item()).If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""await shop.BatchWrite().Put(new Item()).IfNotExists();""", "CS0315")]
    [Arguments("""await orders.BatchWrite().Put(new Item());""", "CS1503")]
    [Arguments("""await orders.BatchGet().Get(default(Address.Key));""", "CS0315")]
    [Arguments("""await orders.BatchGet().GetAll(new Address.Key[0]);""", "CS0315")]
    [Arguments("""await shop.BatchGet().Get("a").If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""await index.BatchGet();""", "CS1061")]
    [Arguments("""await index.BatchWrite();""", "CS1061")]
    [Arguments("""TransactMarker taken; try { await client.TransactWrite().Unique(shop, new Item(), out taken); } catch (TransactionCanceledException e) when (e.ReasonFor(taken) == TransactionFailure.None) { }""", "CS0165")]
    [Arguments("""await shop.Of<UserProfile>().Get(Member.Keys.Of("a", "b", "c", "d"));""", "CS0315")]
    [Arguments("""await shop.Of<UserProfile>().Update(Member.Keys.Of("a", "b", "c", "d"));""", "CS0315")]
    [Arguments("""await shop.Of<UserProfile>().Delete(Member.Keys.Of("a", "b", "c", "d"));""", "CS0315")]
    [Arguments("""shop.Of<UserProfile>().Query(Member.Keys.Of("a", "b", "c", "d"));""", "CS0315")]
    [Arguments("""orders.Query(default(Address.Key));""", "CS0315")]
    [Arguments("""shop.Query(UserProfile.Keys.Of("a"));""", "CS0315")]
    [Arguments("""orders.Query(default(Order.Key), SortKeyCondition.Ge("a"));""", "CS1501")]
    [Arguments("""index.Query(default(Order.Key));""", "CS0311")]
    [Arguments("""await shop.Scan().ItemAsync();""", "CS1061")]
    [Arguments("""await shop.Scan().Parallel(2).ItemAsync();""", "CS1061")]
    [Arguments("""await shop.Query(shards).ItemAsync();""", "CS1061")]
    [Arguments("""await shop.Query("a").Of<Invoice, Payment>().ItemAsync();""", "CS1061")]
    [Arguments("""await shop.Scan().Of<Invoice, Payment>().ItemAsync();""", "CS1061")]
    [Arguments("""shop.Of<Handles>().Update(Handles.Keys.Of("a", 1)).Add(Handles.Attrs.Nickname, "x");""", "CS0315")]
    [Arguments("""shop.Of<Handles>().Update(Handles.Keys.Of("a", 1)).Add(Handles.Attrs.Plan, Plan.Pro);""", "CS0315")]
    [Arguments("""shop.Of<Handles>().Update(Handles.Keys.Of("a", 1)).Add(Handles.Attrs.Home, new PostalAddress { City = "x" });""", "CS0315")]
    [Arguments("""shop.Of<Handles>().Update(Handles.Keys.Of("a", 1)).Set(Handles.Attrs.Count, "x");""", "CS1503")]
    [Arguments("""Handles.Attrs.Count.At(Street.Attrs.Name);""", "CS0315")]
    [Arguments("""KeyedOrder.Keys.Gsi2Pk.Format(null);""", "CS1503")]
    [Arguments("""KeyedOrder.Keys.Pk.Prefix();""", "CS0117")]
    [Arguments("""KeyedOrder.Keys.Sk.PrefixPartial(default, default);""", "CS0117")]
    [Arguments("""KeyedOrder.Keys.Lsi1Pk.Format();""", "CS0117")]
    [Arguments("""UserProfile.Keys.Sk.Prefix();""", "CS0117")]
    [Arguments("""client.TransactWrite().Update(shop, "a").Add(Handles.Attrs.Nickname, "x");""", "CS0315")]
    [Arguments("""await shop.Update("a").IfVersion(1);""", "CS0311")]
    [Arguments("""await shop.Delete("a").IfVersion(1);""", "CS0311")]
    [Arguments("""await shop.Update("a").IgnoreVersion();""", "CS0311")]
    [Arguments("""await shop.Update("a").StoreKeyInputs();""", "CS0311")]
    [Arguments("""await orders.Put(new Order("a")).StoreKeyInputs();""", "CS1061")]
    [Arguments("""await shop.Delete("a").IgnoreVersion();""", "CS0311")]
    [Arguments("""await shop.Put(new Item()).IgnoreVersion();""", "CS0311")]
    [Arguments("""await orders.Put(new Order("a")).IfVersion(1);""", "CS1061")]
    [Arguments("""await shop.Get("a").IfVersion(1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Put(orders, new Order("a")).IfVersion(1);""", "CS1061")]
    [Arguments("""await shop.BatchWrite().Put(new Item()).IfVersion(1);""", "CS1061")]
    [Arguments("""await client.TransactWrite().Unique(orders, default(Address.Key), out _);""", "CS0315")]
    [Arguments("""await client.TransactWrite().Unique(orders, default(Order.Key));""", "CS1501")]
    [Arguments("""await shop.Of<KeyedOrder>().Update(KeyedOrder.Keys.Of(default, default, default)).Add(KeyedOrder.Attrs.Quantity, 1).Remove(1);""", "CS1503")]
    [Arguments("""shop.Query("a").Of<Invoice, Order>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Order, Invoice>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Invoice, Payment, Order>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Invoice, Payment, Payment, Order>();""", "CS0311")]
    [Arguments("""shop.Scan().Of<Invoice, Order>();""", "CS0311")]
    [Arguments("""shop.Scan().Of<Invoice, Payment, Order>();""", "CS0311")]
    [Arguments("""shop.Scan().Of<Invoice, Payment, Payment, Order>();""", "CS0311")]
    [Arguments("""index.Query("a").Of<Invoice, Order>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Invoice, Address>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Invoice, Item>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Invoice, DynaSharp.UnitTests.Fluent.Mix.OptedOut>();""", "CS0311")]
    [Arguments("""shop.Scan().Of<Invoice, DynaSharp.UnitTests.Fluent.Mix.OptedOut>();""", "CS0311")]
    [Arguments("""_ = new Item().Is<Order>();""", "CS0311")]
    [Arguments("""_ = new Item().Is<DynaSharp.UnitTests.Fluent.Mix.OptedOut>();""", "CS0311")]
    [Arguments("""_ = new Item().Is<Address>();""", "CS0311")]
    [Arguments("""_ = new Item().Is<Item>();""", "CS0311")]
    [Arguments("""shop.Query("a").Of<Invoice>();""", "CS1061")]
    [Arguments("""shop.Query("a").Of<Invoice, Payment, Payment, Payment, Payment>();""", "CS1061")]
    [Arguments("""invoices.Query(new PartitionKey<Invoice>("a")).Of<Invoice, Payment>();""", "CS1929")]
    [Arguments("""invoices.Scan().Of<Invoice, Payment>();""", "CS1929")]
    [Arguments("""indexOfInvoices.Query(new PartitionKey<Invoice>("a")).Of<Invoice, Payment>();""", "CS1929")]
    [Arguments("""await shop.Query("a").Of<Invoice, Payment>().CountAsync();""", "CS1061")]
    [Arguments("""await shop.Scan().Of<Invoice, Payment>().CountAsync();""", "CS1061")]
    [Arguments("""await shop.Query("a").Of<Invoice, Payment, Payment>().CountAsync();""", "CS1061")]
    [Arguments("""await shop.Query("a").Of<Invoice, Payment>();""", "CS0315")]
    [Arguments("""shop.Query("a").Of<Invoice, Payment>().Parallel(2);""", "CS1061")]
    [Arguments("""shop.Scan().Of<Invoice, Payment>().Descending();""", "CS1061")]
    [Arguments("""shop.Scan().Of<Invoice, Payment>().Of<Invoice, Payment>();""", "CS1929")]
    [Arguments("""shop.Scan().Of<Invoice, Payment>().Parallel(2).PageAsync();""", "CS1061")]
    [Arguments("""shop.Query("a").Of<Invoice, Payment>().Of<Invoice, Payment>();""", "CS1929")]
    [Arguments("""shop.Query(shards).PagesAsync();""", "CS1061")]
    [Arguments("""shop.Query(shards).Parallel(2);""", "CS1061")]
    [Arguments("""shop.Query(shards).Of<Invoice, Payment>();""", "CS1929")]
    [Arguments("""await shop.Query(shards).PageAsync();""", "CS1501")]
    [Arguments("""await shop.Query(shards);""", "CS0315")]
    [Arguments("""index.Query(shards).Segment(0, of: 2);""", "CS1061")]
    [Arguments("""shop.Query(shards).If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""shop.Query(default);""", "CS0121")]
    [Arguments("""orders.Query(default);""", "CS0121")]
    [Arguments("""index.Query(default);""", "CS0121")]
    [Arguments("""indexOfInvoices.Query(default);""", "CS0121")]
    [Arguments("""shop.Query(default, SortKeyCondition.Ge("a"));""", "CS0121")]
    [Arguments("""index.Query(default, default);""", "CS0121")]
    [Arguments("""shop.Query([]);""", "CS0121")]
    [Arguments("""index.Query([]);""", "CS0121")]
    [Arguments("""shop.Query(null);""", "CS0121")]
    [Arguments("""index.Query(null, SortKeyCondition.Ge("a"));""", "CS0121")]
    [Arguments("""shop.Query(new[] { 1, 2 });""", "CS0311")]
    [Arguments("""shop.Scan().Query(shards);""", "CS1929")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Put(shop, new Item()); tx.If(Attr("x").Exists());""", "CS1061")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Put(shop, new Item()); tx.IfNotExists();""", "CS0315")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Put(shop, new Item()); tx.ReturnOldOnFailure();""", "CS0315")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Update(shop, "a"); tx.Add("n", 1);""", "CS1061")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Update(shop, "a"); tx.Set("n", 1);""", "CS1061")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Update(orders, default(Order.Key)); tx.IfVersion(1);""", "CS1061")]
    [Arguments("""PutStep step = client.TransactWrite();""", "CS0029")]
    [Arguments("""UpdateStep step = client.TransactWrite().Put(shop, new Item());""", "CS0029")]
    [Arguments("""ConditionStep step = client.TransactWrite().Update(shop, "a");""", "CS0029")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite(); PutStep step = tx;""", "CS0029")]
    [Arguments("""TransactGetCommand tx = client.TransactWrite().Put(shop, new Item());""", "CS0029")]
    [Arguments("""await orders.BatchWrite().PutAll(new Item[1]);""", "CS1503")]
    [Arguments("""await orders.BatchWrite().DeleteAll(new Address.Key[0]);""", "CS0315")]
    [Arguments("""await orders.BatchWrite().DeleteAll(new UserProfile[0]);""", "CS0311")]
    [Arguments("""_ = UserProfile.Indexes.Gsi1;""", "CS0117")]
    [Arguments("""_ = KeyedOrder.Indexes.Gsi3;""", "CS0117")]
    [Arguments("""await shop.Update("a").ItemAsync();""", "CS1061")]
    [Arguments("""await shop.Delete("a").ItemAsync();""", "CS1061")]
    [Arguments("""Order order = (await orders.Get(default(Order.Key)).ItemAsync()).ToString();""", "CS0029")]
    [Arguments("""orders.Query(UserProfile.Keys.Pk.Of("a"));""", "CS0315")]
    [Arguments("""orders.Query(UserProfile.Keys.Pk.Of("a"), SortKeyCondition.Ge("b"));""", "CS1501")]
    [Arguments("""shop.Of<KeyedOrder>().Query(UserProfile.Keys.Pk.Of("a"));""", "CS0315")]
    [Arguments("""shop.Of<KeyedOrder>().Index(KeyedOrder.Indexes.Gsi1).Query(UserProfile.Keys.Pk.Of("a"));""", "CS1929")]
    [Arguments("""indexOfInvoices.Query(UserProfile.Keys.Pk.Of("a"));""", "CS1929")]
    [Arguments("""orders.Query(new[] { UserProfile.Keys.Pk.Of("a") });""", "CS0311")]
    [Arguments("""orders.Query([UserProfile.Keys.Pk.Of("a")]);""", "CS0411")]
    [Arguments("""indexOfInvoices.Query(new[] { UserProfile.Keys.Pk.Of("a") });""", "CS1929")]
    [Arguments("""AttributeValue key = UserProfile.Keys.Pk.Of("a");""", "CS0029")]
    [Arguments("""string key = UserProfile.Keys.Pk.Of("a");""", "CS0029")]
    [Arguments("""shop.Get(UserProfile.Keys.Pk.Of("a"));""", "CS0315")]
    [Arguments("""KeyedOrder.Keys.Sk.Of(default, default);""", "CS1501")]
    [Arguments("""KeyedOrder.Keys.Gsi1Sk.Of(1m);""", "CS1501")]
    [Arguments("""Handles.Attrs.Count.BeginsWith("1");""", "CS0619")]
    [Arguments("""Handles.Attrs.Total.BeginsWith("1");""", "CS0619")]
    [Arguments("""Attr<int>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<decimal, NumberConverter<decimal>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<bool, BoolConverter>("n").BeginsWith("t");""", "CS0619")]
    [Arguments("""new Attr<System.DateTimeOffset, EpochSecondsConverter>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<byte[], BinaryConverter>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<byte[], NullableReferenceHandleConverter<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<System.ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""Handles.Attrs.Total.Contains(1L);""", "CS1929")]
    [Arguments("""Attr<int>("n").Contains(1);""", "CS1929")]
    [Arguments("""new Attr<bool, BoolConverter>("n").Contains(true);""", "CS1929")]
    [Arguments("""new Attr<System.Guid, GuidConverter>("n").Contains(System.Guid.Empty);""", "CS1929")]
    [Arguments("""new Attr<byte[], BinaryConverter>("n").Contains(new byte[] { 1 });""", "CS1929")]
    [Arguments("""new Attr<System.Collections.Generic.HashSet<string>, HashSetConverter<string, StringSetElement>>("n").Contains(1);""", "CS1929")]
    [Arguments("""new Attr<System.Collections.Generic.List<int>, ListConverter<int, NumberConverter<int>>>("n").Contains("1");""", "CS1929")]
    [Arguments("""shop.Get("a").Select(Attr<int>("n")).Project("x");""", "CS1061")]
    [Arguments("""shop.Get("a").Select(Attr<int>("n")).Select(Attr<int>("m"));""", "CS1061")]
    [Arguments("""shop.Get("a").Select(Attr<int>("n")).Consistent();""", "CS1061")]
    [Arguments("""shop.Get("a").Select(Attr<int>("n")).MaxAttempts(1);""", "CS1061")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).Project("x");""", "CS1061")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).Select(Attr<int>("m"));""", "CS1061")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).Filter(Attr("x").Exists());""", "CS1061")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).Limit(1);""", "CS1061")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).Descending();""", "CS1061")]
    [Arguments("""await shop.Query("a").Select(Attr<int>("n")).CountAsync();""", "CS1061")]
    [Arguments("""await shop.Query("a").Select(Attr<int>("n"));""", "CS0315")]
    [Arguments("""shop.Scan().Select(Attr<int>("n")).Project("x");""", "CS1061")]
    [Arguments("""shop.Scan().Select(Attr<int>("n")).Select(Attr<int>("m"));""", "CS1061")]
    [Arguments("""shop.Scan().Select(Attr<int>("n")).Filter(Attr("x").Exists());""", "CS1061")]
    [Arguments("""shop.Scan().Select(Attr<int>("n")).Parallel(2);""", "CS1061")]
    [Arguments("""await shop.Scan().Select(Attr<int>("n")).ValueAsync();""", "CS1061")]
    [Arguments("""await shop.Query("a").Select(Attr<int>("n")).ItemAsync();""", "CS1061")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).ItemsAsync();""", "CS1061")]
    [Arguments("""shop.Scan().Select(Attr<int>("n")).ItemsAsync();""", "CS1061")]
    [Arguments("""await shop.Scan().Select(Attr<int>("n")).CountAsync();""", "CS1061")]
    [Arguments("""await shop.Scan().Select(Attr<int>("n"));""", "CS0315")]
    [Arguments("""shop.Scan().Parallel(2).Select(Attr<int>("n"));""", "CS1061")]
    [Arguments("""shop.Query(shards).Select(Attr<int>("n"));""", "CS1061")]
    [Arguments("""shop.Query("a").Of<Invoice, Payment>().Select(Attr<int>("n"));""", "CS1061")]
    [Arguments("""shop.Scan().Of<Invoice, Payment>().Select(Attr<int>("n"));""", "CS1061")]
    [Arguments("""shop.BatchGet().Get("a").Select(Attr<int>("n"));""", "CS1061")]
    [Arguments("""shop.Get("a").Select(Attr("n"));""", "CS0411")]
    [Arguments("""shop.Get("a").Select(Attr<int>("a"), Attr<int>("b"));""", "CS1501")]
    [Arguments("""shop.Of<KeyOnlyPlan>().Get(KeyOnlyPlan.Keys.Of(Plan.Pro)).Select(KeyOnlyPlan.Attrs.Plan);""", "CS0117")]
    [Arguments("""new Attr<int, NullableHandleConverter<int, NumberConverter<int>>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<bool, NullableHandleConverter<bool, BoolConverter>>("n").BeginsWith("t");""", "CS0619")]
    [Arguments("""new Attr<System.DateTimeOffset, NullableHandleConverter<System.DateTimeOffset, EpochSecondsConverter>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<byte[], NullableReferenceHandleConverter<byte[], BinaryConverter>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""new Attr<System.ReadOnlyMemory<byte>, NullableHandleConverter<System.ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>>("n").BeginsWith("1");""", "CS0619")]
    [Arguments("""shop.Update("a").Add(Handles.Attrs.Nickname, "x");""", "CS0315")]
    [Arguments("""new Attr<string, StringConverter>("n").BeginsWith(new byte[] { 1 });""", "CS0311")]
    [Arguments("""KeyedOrder.Keys.Pk.BeginsWith();""", "CS1501")]
    [Arguments("""UserProfile.Keys.Sk.BeginsWith();""", "CS1501")]
    [Arguments("""KeyedOrder.Keys.Sk.BeginsWith(default, default);""", "CS1501")]
    [Arguments("""await shop.Update("a").SetIfNotExists(Device.Attrs.Slot, null);""", "CS1503")]
    [Arguments("""await shop.Update("a").Add(Handles.Attrs.Count, null);""", "CS1503")]
    [Arguments("""await shop.Update("a").Delete(Device.Attrs.Slot, null);""", "CS1503")]
    [Arguments("""await shop.Update("a").SetIfNotExists(Device.Attrs.Slot, (int?)1);""", "CS1503")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").SetIfNotExists(Device.Attrs.Slot, null);""", "CS1503")]
    [Arguments("""await shop.Update("a").Set(Device.Attrs.Slot, (long?)1);""", "CS1503")]
    [Arguments("""await shop.Update("a").SetIfNotNull(Device.Attrs.Slot, (long?)1);""", "CS0411")]
    [Arguments("""await shop.Update("a").SetIfNotNull("x", "y");""", "CS0411")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").SetIfNotNull("x", "y");""", "CS0411")]
    [Arguments("""await shop.Put(new Item()).SetIfNotNull(Device.Attrs.Slot, 1);""", "CS1061")]
    [Arguments("""shop.Index<Address>("x");""", "CS0311")]
    [Arguments("""shop.Index<Item>("x");""", "CS0311")]
    [Arguments("""shop.Index<KeyedOrder>();""", "CS1501")]
    [Arguments("""shop.Of<UserProfile>().Index<KeyedOrder>("x");""", "CS1929")]
    [Arguments("""Index<KeyedOrder> byStatus = shop.Index<KeyedOrder>(KeyedOrder.Indexes.Gsi1);""", "CS0029")]
    [Arguments("""shop.Index<KeyedOrder>("GSI1").Put(new Item());""", "CS1501")]
    [Arguments("""await shop.Get("a").MaxStalledRounds(1);""", "CS1061")]
    [Arguments("""await shop.Put(new Item()).MaxStalledRounds(1);""", "CS1061")]
    [Arguments("""await shop.Query("a").MaxStalledRounds(1).ItemsAsync().ToListAsync();""", "CS1061")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").MaxStalledRounds(1);""", "CS1061")]
    [Arguments("""orders.Get("a");""", "CS0311")]
    [Arguments("""orders.Query(new System.Collections.Generic.List<string> { "a" });""", "CS0311")]
    [Arguments("""orders.Query("a");""", "CS0311")]
    [Arguments("""orders.Query(1, default);""", "CS1501")]
    [Arguments("""orders.Query(shards);""", "CS0311")]
    [Arguments("""orders.Query(["a", "b"]);""", "CS0411")]
    [Arguments("""indexOfInvoices.Query("a");""", "CS1503")]
    [Arguments("""client.TransactWrite().Update(orders, "a", "b");""", "CS1503")]
    [Arguments("""orders.Update("a");""", "CS0311")]
    [Arguments("""orders.Delete("a", "b");""", "CS1501")]
    [Arguments("""orders.BatchGet().Get("a");""", "CS0311")]
    [Arguments("""orders.BatchWrite().Delete("a");""", "CS0311")]
    [Arguments("""client.TransactGet().Get(orders, "a", out var order);""", "CS0311")]
    public async Task Compile_WhatTheTypesRuleOut_FailsWithTheExpectedError(string statement, string expected)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var errors = batch.Diagnostics(statement, DiagnosticSeverity.Error);

        // Assert
        await Assert.That(string.Join(",", errors)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""await shop.Put().With("x", 1);""")]
    [Arguments("""await shop.Put(new Item()).With("x", 1);""")]
    [Arguments("""await orders.Put(new Order("a")).IfNotExists();""")]
    [Arguments("""await orders.Put(new Order("a")).IgnoreVersion().IfNotExists().SendAsync();""")]
    [Arguments("""await client.TransactWrite().Put(orders, new Order("a")).IgnoreVersion().IfNotExists().Delete(orders, default(Order.Key)).SendAsync();""")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Put(orders, new Order("a")).IgnoreVersion();""")]
    [Arguments("""shop.Of<Order>();""")]
    [Arguments("""client.Table<Order>("Shop");""")]
    [Arguments("""await orders.Get(default(Order.Key));""")]
    [Arguments("""await orders.Get(new Order("a"));""")]
    [Arguments("""Order? order = await orders.Query(default(Order.Key)).ItemAsync();""")]
    [Arguments("""Order? order = await orders.Query(default(Order.Key)).Filter(Attr("x").Exists()).Consistent().Project("x").ItemAsync(default);""")]
    [Arguments("""await foreach (var order in orders.Query(default(Order.Key)).ItemsAsync()) { }""")]
    [Arguments("""UserProfile? profile = await shop.Of<UserProfile>().Query(UserProfile.Keys.Of("a")).ItemAsync();""")]
    [Arguments("""Item? first = await shop.Query("a").Descending().Limit(1).ItemAsync();""")]
    [Arguments("""Item? first = await index.Query("a").ItemAsync();""")]
    [Arguments("""Invoice? first = await indexOfInvoices.Query(new PartitionKey<Invoice>("a")).ItemAsync();""")]
    [Arguments("""QueryCommand<Order> read = orders.Query(new PartitionKey<Order>("a"));""")]
    [Arguments("""QueryCommand<Item> read = shop.Query("a");""")]
    [Arguments("""QueryCommand<Order> read = orders.Query(new PartitionKey<Order>(1, "pk"));""")]
    [Arguments("""AttributeValue key = "a"; QueryCommand<Order> read = orders.Query(new PartitionKey<Order>(key, "pk"));""")]
    [Arguments("""QueryCommand<UserProfile> read = shop.Of<UserProfile>().Query(UserProfile.Keys.Pk.Of("a"));""")]
    [Arguments("""QueryCommand<UserProfile> read = shop.Of<UserProfile>().Query(UserProfile.Keys.Pk.Of("a"), SortKeyCondition.Ge("b"));""")]
    [Arguments("""QueryCommand<Item> read = shop.Query(UserProfile.Keys.Pk.Of("a"));""")]
    [Arguments("""FanOutQueryCommand<Order> read = orders.Query([new PartitionKey<Order>("a"), new PartitionKey<Order>("b")]);""")]
    [Arguments("""FanOutQueryCommand<Order> read = orders.Query(System.Linq.Enumerable.Select(new[] { "a" }, static id => new PartitionKey<Order>(id)));""")]
    [Arguments("""FanOutQueryCommand<Order> read = orders.Query(new[] { new PartitionKey<Order>("a") });""")]
    [Arguments("""await orders.Delete(new Order("a"));""")]
    [Arguments("""await shop.Update("a").Add("n", 1).Return(Attr<int>("n"));""")]
    [Arguments("""await shop.Update("a").Add("n", 1).MaxAttempts(1).Return(Attr<int>("n")).SendAsync();""")]
    [Arguments("""await foreach (var item in shop.Scan().Parallel(segments: 8).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in index.Scan().Filter(Attr("x").Exists()).Limit(1).Parallel(8).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var order in orders.Query(new PartitionKey<Order>("a"), SortKeyCondition.BeginsWith("b")).Filter(Attr("x").Exists()).Descending().Limit(1).Consistent().ReturnCapacity().MaxAttempts(1).Project("x").ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in index.Query("a", SortKeyCondition.Ge("b")).Project("x").ItemsAsync()) { }""")]
    [Arguments("""await foreach (Page<Order> page in orders.Query(new PartitionKey<Order>("a")).PagesAsync()) { }""")]
    [Arguments("""Page<Item> page = await shop.Query("a", SortKeyCondition.BeginsWith("O")).Limit(25).PageAsync(ContinuationToken.Parse("x"), default);""")]
    [Arguments("""Page<Item> page = await shop.Scan().Segment(2, of: 8).PageAsync(default, default);""")]
    [Arguments("""int total = await orders.Query(new PartitionKey<Order>("a")).CountAsync();""")]
    [Arguments("""int total = await shop.Scan().CountAsync();""")]
    [Arguments("""shop.Query("a").Filter($"{Attr("x")} = {1}");""")]
    [Arguments("""shop.Scan().Filter($"{Attr("x")} = {1}");""")]
    [Arguments("""await orders.WaitUntilActiveAsync();""")]
    [Arguments("""await orders.WaitUntilActiveAsync(System.TimeSpan.FromSeconds(1));""")]
    [Arguments("""var description = await orders.DescribeTableAsync();""")]
    [Arguments("""try { await orders.WaitUntilActiveAsync(); } catch (TableNotActiveException e) when (e.LastStatus == TableStatus.Deleting || e.TableName.Length > 0) { }""")]
    [Arguments("""CreateTableResult result = await orders.CreateTable().Including<Invoice>().Including<Payment>().SendAsync(); DynaSharp.Requests.TableDescription description = result.Table; bool news = result.Created || result.TimeToLiveEnabled;""")]
    [Arguments("""CreateTableResult awaited = await orders.CreateTable().IfNotExists();""")]
    [Arguments("""await orders.CreateTable().Provisioned(read: 5, write: 5).WaitUntilActive().IfNotExists().SendAsync();""")]
    [Arguments("""await orders.CreateTable().WaitUntilActive(System.TimeSpan.FromSeconds(1));""")]
    [Arguments("""DynaSharp.Requests.CreateTableRequest request = orders.CreateTable().ToRequest();""")]
    [Arguments("""CreateTableCommand create = orders.CreateTable().Including<Invoice>(); string json = create.ToRequestJson(); await create.SendAsync();""")]
    [Arguments("""await shop.CreateTable().Including<Order>().SendAsync();""")]
    [Arguments("""CreateTableCommand kept = orders.CreateTable(); await orders.DescribeTableAsync(); await kept.Including<Invoice>().SendAsync();""")]
    [Arguments("""DynaSharp.Requests.TableDescription description = await orders.DeleteTableAsync();""")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).If(Attr("x").Exists());""")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).If($"{Attr("x")} = {1}").IfNotExists().IfExists().ReturnOldOnFailure().MaxAttempts(1).Token("t").ReturnCapacity().ReturnMetrics().SendAsync();""")]
    [Arguments("""await client.TransactWrite().Unique(shop, new Item(), out var taken).ReturnOldOnFailure();""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Add("n", 1).Set("a", "b").Remove("c").SetIfNotExists("d", 2).Delete("e", AttributeValue.FromStringSet(["x"])).If(Attr("x").Exists()).IfExists().IfNotExists().ReturnOldOnFailure().MaxAttempts(1).Token("t").ReturnCapacity().ReturnMetrics().SendAsync();""")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").If(Attr("x").Exists()).IfExists().IfNotExists().ReturnOldOnFailure().MaxAttempts(1).Token("t").ReturnCapacity().ReturnMetrics().SendAsync();""")]
    [Arguments("""await client.TransactWrite().Check(shop, "a").If(Attr("x").Exists()).IfExists().IfNotExists().ReturnOldOnFailure().MaxAttempts(1).Token("t").ReturnCapacity().ReturnMetrics().SendAsync();""")]
    [Arguments("""await client.TransactWrite().Token("t").MaxAttempts(1).ReturnCapacity().ReturnMetrics().Delete(shop, "a");""")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).Put(shop, new Item()).Update(shop, "a").Delete(shop, "a").Check(shop, "a").Unique(shop, new Item(), out _);""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Put(shop, new Item()).Update(shop, "a").Update(shop, "a").Delete(shop, "a").Update(shop, "a").Check(shop, "a").Update(shop, "a").Unique(shop, new Item(), out _);""")]
    [Arguments("""await client.TransactWrite().Delete(shop, "a").Put(shop, new Item()).Delete(shop, "a").Update(shop, "a").Delete(shop, "a").Delete(shop, "a").Check(shop, "a").Delete(shop, "a").Unique(shop, new Item(), out _);""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Delete(shop, "b", "c");""")]
    [Arguments("""await client.TransactWrite().Update(orders, default(Order.Key)).Delete(orders, default(Order.Key)).Check(orders, default(Order.Key));""")]
    [Arguments("""await client.TransactWrite().Update(orders, default(Order.Key), out var m1).Delete(orders, default(Order.Key), out var m2).Check(orders, default(Order.Key), out var m3);""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a", "b", out var m1).Delete(shop, "a", "b", out var m2).Check(shop, "a", "b", out var m3).Put(shop, new Item(), out var m4);""")]
    [Arguments("""TransactMarker taken = default; try { await client.TransactWrite().Unique(shop, new Item(), out taken); } catch (TransactionCanceledException e) when (e.ReasonFor(taken) == TransactionFailure.ConditionalCheckFailed) { Item? holder = e.ItemFor(taken); }""")]
    [Arguments("""await client.TransactGet().Get(orders, default(Order.Key), out TransactSlot<Order> slot).Get(shop, "a", "b", out TransactSlot<Item> other).ReturnCapacity().MaxAttempts(1).SendAsync();""")]
    [Arguments("""await client.TransactGet().Get(shop, "a", out TransactSlot<Item> slot).Get(orders, default(Order.Key), out var keyed).SendAsync();""")]
    [Arguments("""var snapshot = await client.TransactGet().Get(orders, default(Order.Key), out var slot); Order? order = snapshot.Read(slot).Item;""")]
    [Arguments("""await shop.BatchWrite().Put(new Item()).Delete("a", "b").Put(orders, new Order("a")).Delete(orders, default(Order.Key)).Delete(shop, "a", "b").ReturnCapacity().ReturnMetrics().MaxAttempts(1).MaxStalledRounds(1);""")]
    [Arguments("""await orders.BatchWrite().Put(new Order("a")).Delete(default(Order.Key)).Put(shop, new Item()).SendAsync();""")]
    [Arguments("""BatchWriteResult written = await shop.BatchWrite().SendAsync(); int count = written.Written + written.Requests;""")]
    [Arguments("""BatchGetResult<Order> found = await orders.BatchGet().Get(default(Order.Key)).GetAll(new Order.Key[1]).Consistent().Project("x", "y").ReturnCapacity().MaxAttempts(1).MaxStalledRounds(1).SendAsync(); int count = found.Items.Count + found.ConsumedCapacity.Count;""")]
    [Arguments("""await shop.BatchGet().Get("a", "b").Project(new Attr<int, DynaSharp.Serialization.NumberConverter<int>>("n"));""")]
    [Arguments("""await shop.Of<UserProfile>().Get(UserProfile.Keys.Of("a"));""")]
    [Arguments("""shop.Of<Handles>().Update(Handles.Keys.Of("a", 1)).Add(Handles.Attrs.Count, 1).Add(Handles.Attrs.Total, 1L).Add(Handles.Attrs.Tags, new());""")]
    [Arguments("""shop.Of<KeyedOrder>().Get(KeyedOrder.Keys.Of(default, default, default)).Project(KeyedOrder.Attrs.Status, KeyedOrder.Attrs.Total);""")]
    [Arguments("""shop.BatchGet().Get("a").Project(KeyedOrder.Attrs.Status, KeyedOrder.Attrs.Total);""")]
    [Arguments("""client.TransactWrite().Update(shop, "a").Add(Handles.Attrs.Count, 1);""")]
    [Arguments("""await orders.Update(default(Order.Key)).Set("x", 1).IfVersion(3).SendAsync();""")]
    [Arguments("""await orders.Update(default(Order.Key)).IgnoreVersion();""")]
    [Arguments("""await orders.Delete(default(Order.Key)).IfVersion(3).ReturnOld();""")]
    [Arguments("""await orders.Delete(new Order("a")).IgnoreVersion();""")]
    [Arguments("""long? stored = (await orders.Put(new Order("a"))).Version;""")]
    [Arguments("""await client.TransactWrite().Update(orders, default(Order.Key)).IfVersion(1).Delete(orders, default(Order.Key)).IgnoreVersion().Check(orders, default(Order.Key)).IfVersion(2);""")]
    [Arguments("""await client.TransactWrite().Unique(orders, default(Order.Key), out TransactMarker taken).ReturnOldOnFailure();""")]
    [Arguments("""await client.TransactWrite().Put(shop, new Item()).Unique(shop.Of<Order>(), new Order("a"), out var taken);""")]
    [Arguments("""await orders.BatchWrite().Put(new Order("a")).IgnoreVersion().Delete(default(Order.Key));""")]
    [Arguments("""await shop.Of<KeyedOrder>().Update(KeyedOrder.Keys.Of(default, default, default)).Remove(KeyedOrder.Attrs.Quantity).Remove("x").Remove(Attr("y"));""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Remove(KeyedOrder.Attrs.Quantity).Remove("x");""")]
    [Arguments("""await foreach (Mixed<Invoice, Payment> item in shop.Query("a").Of<Invoice, Payment>().ItemsAsync()) { if (item.TryGetValue(out Invoice? invoice)) { var id = invoice.Id; } }""")]
    [Arguments("""await foreach (var item in index.Query("a").Of<Invoice, Payment>().ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in shop.Scan().Of<Invoice, Payment>().ItemsAsync()) { string id = item.Match(i => i.Id, p => p.Id); }""")]
    [Arguments("""await foreach (var item in index.Scan().Of<Invoice, Payment>().Parallel(2).ItemsAsync()) { }""")]
    [Arguments("""await foreach (Page<Mixed<Invoice, Payment>> page in shop.Query("a").Of<Invoice, Payment>().PagesAsync()) { }""")]
    [Arguments("""Page<Mixed<Invoice, Payment>> page = await shop.Query("a").Of<Invoice, Payment>().PageAsync(default, default);""")]
    [Arguments("""Page<Item> page = await shop.Query("a").PageAsync();""")]
    [Arguments("""Page<Item> page = await shop.Query("a").PageAsync(default);""")]
    [Arguments("""Page<Item> page = await shop.Query("a").PageAsync(System.Threading.CancellationToken.None);""")]
    [Arguments("""Page<Item> page = await shop.Query("a").PageAsync(default(ContinuationToken));""")]
    [Arguments("""Page<Item> page = await shop.Query("a").PageAsync(cancellationToken: System.Threading.CancellationToken.None);""")]
    [Arguments("""Page<Item> page = await shop.Query(["a", "b"]).PageAsync(10);""")]
    [Arguments("""Page<Item> page = await shop.Query(["a", "b"]).PageAsync(10, default);""")]
    [Arguments("""Page<Item> page = await shop.Query(["a", "b"]).PageAsync(10, System.Threading.CancellationToken.None);""")]
    [Arguments("""Page<Item> page = await shop.Query(["a", "b"]).PageAsync(10, default, System.Threading.CancellationToken.None);""")]
    [Arguments("""MixedQueryCommand<Mixed<Invoice, Payment, Payment>> three = shop.Query("a").Of<Invoice, Payment, Payment>();""")]
    [Arguments("""MixedQueryCommand<Mixed<Invoice, Payment, Payment, Invoice>> four = shop.Query("a").Of<Invoice, Payment, Payment, Invoice>();""")]
    [Arguments("""MixedScanCommand<Mixed<Invoice, Payment, Payment>> three = shop.Scan().Of<Invoice, Payment, Payment>();""")]
    [Arguments("""MixedScanCommand<Mixed<Invoice, Payment, Payment, Invoice>> four = shop.Scan().Of<Invoice, Payment, Payment, Invoice>();""")]
    [Arguments("""shop.Query("a").Filter(Attr("x").Exists()).Limit(1).Of<Invoice, Payment>().Filter($"{Attr("x")} = {1}").Project("x").Descending().Consistent().ReturnCapacity().MaxAttempts(1);""")]
    [Arguments("""shop.Query("a").Of<Invoice, DynaSharp.UnitTests.Fluent.Mix.Alpha>();""")]
    [Arguments("""await foreach (Item item in shop.Query(shards, SortKeyCondition.Ge("x")).Filter(Attr("x").Exists()).Filter($"{Attr("x")} = {1}").Project("x").Limit(1).Descending().Consistent().MaxConcurrency(2).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var order in orders.Query([new PartitionKey<Order>("a")]).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in index.Query(shards).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var invoice in indexOfInvoices.Query([new PartitionKey<Invoice>("a")], SortKeyCondition.Between("a", "b")).ItemsAsync()) { }""")]
    [Arguments("""Page<Item> page = await shop.Query(shards).PageAsync(10, ContinuationToken.Parse("x"), default); int total = await shop.Query(shards).CountAsync();""")]
    [Arguments("""System.Collections.Generic.List<AttributeValue> list = ["a"]; shop.Query(list);""")]
    [Arguments("""System.Collections.Generic.IEnumerable<AttributeValue> keys = shards; shop.Query(keys, SortKeyCondition.Ge("a"));""")]
    [Arguments("""shop.Query("a"); shop.Query("a", SortKeyCondition.Ge("b")); shop.Query(1); shop.Query(new byte[] { 1 });""")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite(); foreach (var item in new Item[1]) { tx = tx.Put(shop, item).IfNotExists(); } await tx.SendAsync();""")]
    [Arguments("""var tx = client.TransactWrite(); foreach (var item in new Item[1]) { tx = tx.Put(shop, item); } await tx;""")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Put(shop, new Item()); tx = tx.Update(shop, "a").Add("n", 1); tx = tx.Delete(shop, "a").IfExists(); tx = tx.Check(shop, "a").If(Attr("x").Exists()); tx = tx.Unique(shop, new Item(), out var taken); await tx.SendAsync();""")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite().Delete(shop, "a"); string json = tx.Token("t").ReturnCapacity().ReturnMetrics().MaxAttempts(1).ToRequestJson();""")]
    [Arguments("""TransactWriteCommand tx = client.TransactWrite(); await client.TransactWrite().Delete(shop, "a").SendAsync();""")]
    [Arguments("""var tx = client.TransactWrite().Update(shop, "a"); tx = tx.Update(shop, "b").Add("n", 1); await tx.SendAsync();""")]
    [Arguments("""await client.TransactWrite().SendAsync();""")]
    [Arguments("""WriteResult<Item> written = await shop.Put(new Item()).ReturnOld().ReturnNew();""")]
    [Arguments("""await orders.BatchWrite().PutAll(new Order[1]).DeleteAll(new Order.Key[1]).DeleteAll(new IKey<Order>[1]).Delete(default(Order.Key));""")]
    [Arguments("""await orders.BatchWrite().DeleteAll(new Order[1]).DeleteAll(new System.Collections.Generic.List<Order>()).Delete((new Order[1])[0]);""")]
    [Arguments("""const string name = KeyedOrder.Indexes.Gsi2; var byStatus = shop.Of<KeyedOrder>().Index(KeyedOrder.Indexes.Gsi1); _ = KeyedOrder.Indexes.Lsi1;""")]
    [Arguments("""Order? order = await orders.Get(default(Order.Key)).ItemAsync();""")]
    [Arguments("""Item? item = await shop.Get("a").Consistent().Project("x").ReturnCapacity().MaxAttempts(1).ItemAsync(default);""")]
    [Arguments("""index.Query("a"); index.Query("a", SortKeyCondition.Ge("b")); index.Query(1); index.Query(new byte[] { 1 });""")]
    [Arguments("""shop.Query(default(AttributeValue));""")]
    [Arguments("""index.Query(default(AttributeValue), default);""")]
    [Arguments("""FanOutQueryCommand<Item> query = shop.Query(new[] { "a", "b" }); query.Limit(1);""")]
    [Arguments("""FanOutQueryCommand<Item> query = shop.Query(["a", "b"], SortKeyCondition.Ge("x")); query.Limit(1);""")]
    [Arguments("""FanOutQueryCommand<Item> query = index.Query(["a", "b"]); query.Limit(1);""")]
    [Arguments("""FanOutQueryCommand<Order> query = orders.Query([new PartitionKey<Order>("a"), new PartitionKey<Order>("b")]).Descending(); query.Limit(1);""")]
    [Arguments("""FanOutQueryCommand<Item> query = shop.Query([1, 2]); query.Limit(1);""")]
    [Arguments("""FanOutQueryCommand<Item> query = shop.Query(["a", 2]); query.Limit(1);""")]
    [Arguments("""shop.Of<UserProfile>().Query(UserProfile.Keys.Pk.Of("a"));""")]
    [Arguments("""await foreach (var order in shop.Of<KeyedOrder>().Query(KeyedOrder.Keys.Pk.Of(default), KeyedOrder.Keys.Sk.BeginsWith()).ItemsAsync()) { }""")]
    [Arguments("""shop.Of<KeyedOrder>().Query(KeyedOrder.Keys.Pk.Of(default), KeyedOrder.Keys.Sk.BeginsWith(default));""")]
    [Arguments("""Attr("x").BeginsWith("a");""")]
    [Arguments("""new Attr<byte[], BinaryConverter>("b").BeginsWith(new byte[] { 1 });""")]
    [Arguments("""new Attr<System.ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>("b").BeginsWith(new byte[] { 1 });""")]
    [Arguments("""Handles.Attrs.Nickname.BeginsWith("a");""")]
    [Arguments("""Handles.Attrs.Plan.BeginsWith("P");""")]
    [Arguments("""Handles.Attrs.PreviousPlan.BeginsWith("P");""")]
    [Arguments("""new Attr<System.Guid, GuidConverter>("g").BeginsWith("a");""")]
    [Arguments("""new Attr<System.DateOnly, DateOnlyConverter>("d").BeginsWith("2026");""")]
    [Arguments("""new Attr<System.DateTimeOffset, DateTimeOffsetConverter>("d").BeginsWith("2026");""")]
    [Arguments("""shop.Of<KeyedOrder>().Index(KeyedOrder.Indexes.Gsi1).Query(KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro));""")]
    [Arguments("""shop.Query(UserProfile.Keys.Pk.Of("a"));""")]
    [Arguments("""shop.Query(UserProfile.Keys.Pk.Of("a"), SortKeyCondition.Ge("b")).Of<Invoice, Payment>();""")]
    [Arguments("""index.Query(UserProfile.Keys.Pk.Of("a")).Of<Invoice, Payment>();""")]
    [Arguments("""AttributeValue key = UserProfile.Keys.Pk.Of("a").Value;""")]
    [Arguments("""shop.Of<UserProfile>().Query([UserProfile.Keys.Pk.Of("a"), UserProfile.Keys.Pk.Of("b")]);""")]
    [Arguments("""shop.Of<UserProfile>().Query(new[] { UserProfile.Keys.Pk.Of("a") }, SortKeyCondition.Ge("b"));""")]
    [Arguments("""shop.Of<UserProfile>().Query(System.Linq.Enumerable.Select(new[] { "a" }, UserProfile.Keys.Pk.Of));""")]
    [Arguments("""shop.Of<KeyedOrder>().Index(KeyedOrder.Indexes.Gsi1).Query([KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro), KeyedOrder.Keys.Gsi1Pk.Of(Plan.Free)]);""")]
    [Arguments("""shop.Query(new[] { UserProfile.Keys.Pk.Of("a") });""")]
    [Arguments("""index.Query(new[] { UserProfile.Keys.Pk.Of("a") }, SortKeyCondition.Ge("b"));""")]
    [Arguments("""await shop.Update("a").Set(Device.Attrs.Slot, null);""")]
    [Arguments("""int? slot = 1; await shop.Update("a").Set(Device.Attrs.Slot, slot).SendAsync();""")]
    [Arguments("""await shop.Of<KeyedOrder>().Update(KeyedOrder.Keys.Of(default, default, default)).Set(KeyedOrder.Attrs.TrackingId, (System.Guid?)System.Guid.Empty).IgnoreVersion();""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Set(Device.Attrs.Slot, (int?)null).SendAsync();""")]
    [Arguments("""string? serial = null; await shop.Update("a").Set(Device.Attrs.Serial, serial);""")]
    [Arguments("""Index<Item> byStatus = shop.Index<KeyedOrder>(KeyedOrder.Indexes.Gsi1);""")]
    [Arguments("""await foreach (var item in shop.Index<KeyedOrder>(KeyedOrder.Indexes.Gsi1).Query(KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro)).Of<Invoice, Payment>().ItemsAsync()) { }""")]
    [Arguments("""Index<KeyedOrder> byStatus = shop.Of<KeyedOrder>().Index<KeyedOrder>(KeyedOrder.Indexes.Gsi1);""")]
    [Arguments("""System.ReadOnlySpan<string> keys = ["a", "b"]; await foreach (var item in shop.Query(keys).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in shop.Query(shards).MaxAttempts(1).ReturnCapacity().ItemsAsync()) { }""")]
    [Arguments("""Page<Item> page = await shop.Query(shards).ReturnCapacity(ReturnConsumedCapacity.Indexes).PageAsync(1); ConsumedCapacity? capacity = page.ConsumedCapacity;""")]
    [Arguments("""shop.Update("a").Add(Handles.Attrs.Total, 1L);""")]
    [Arguments("""shop.Update("a").Add(new Attr<System.Collections.Generic.HashSet<string>, NullableReferenceHandleConverter<System.Collections.Generic.HashSet<string>, HashSetConverter<string, StringSetElement>>>("n"), []);""")]
    [Arguments("""client.TransactWrite().Update(shop, "a").Add(Handles.Attrs.Total, 1L).Add(new Attr<System.Collections.Generic.HashSet<string>, NullableReferenceHandleConverter<System.Collections.Generic.HashSet<string>, HashSetConverter<string, StringSetElement>>>("n"), []);""")]
    [Arguments("""_ = Handles.Attrs.Nickname.BeginsWith("a");""")]
    [Arguments("""_ = new Attr<byte[], NullableReferenceHandleConverter<byte[], BinaryConverter>>("n").BeginsWith(new System.ReadOnlyMemory<byte>(new byte[1]));""")]
    [Arguments("""_ = new Attr<System.ReadOnlyMemory<byte>, NullableHandleConverter<System.ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>>("n").BeginsWith(new System.ReadOnlyMemory<byte>(new byte[1]));""")]
    [Arguments("""int value = (await shop.Get("a").Consistent().ReturnCapacity().MaxAttempts(1).Select(Attr<int>("n")).SendAsync(default)).Value;""")]
    [Arguments("""SelectResult<long?> total = await shop.Of<Handles>().Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Total);""")]
    [Arguments("""string? name = (await shop.Of<Handles>().Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Nickname).SendAsync()).Value;""")]
    [Arguments("""await foreach (int value in shop.Query("a").Filter(Attr("x").Exists()).Limit(1).Descending().Consistent().ReturnCapacity().MaxAttempts(1).Select(Attr<int>("n")).ValuesAsync(default)) { }""")]
    [Arguments("""SelectResult<int> first = await shop.Query("a").Select(Attr<int>("n")).ValueAsync(default);""")]
    [Arguments("""await foreach (Page<int> page in shop.Query("a").Select(Attr<int>("n")).PagesAsync(default)) { }""")]
    [Arguments("""Page<long?> page = await shop.Of<Handles>().Scan().Segment(0, of: 2).Filter(Attr("x").Exists()).Select(Handles.Attrs.Total).PageAsync(default, default);""")]
    [Arguments("""await foreach (string? value in index.Query("a").Select(Handles.Attrs.Nickname).ValuesAsync()) { }""")]
    public async Task Compile_WhatTheGuardsLeaveOpen_Compiles(string statement)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var errors = batch.Diagnostics(statement, DiagnosticSeverity.Error);

        // Assert
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    [Arguments("""shop.Query("a");""", "AttributeValue")]
    [Arguments("""shop.Query(1);""", "AttributeValue")]
    [Arguments("""shop.Query(default(AttributeValue), SortKeyCondition.Ge("x"));""", "AttributeValue")]
    [Arguments("""shop.Query(["a", "b"]);""", "ReadOnlySpan<string>")]
    [Arguments("""shop.Query(["a", "b"], SortKeyCondition.Ge("x"));""", "ReadOnlySpan<string>")]
    [Arguments("""index.Query(["a", "b"]);""", "ReadOnlySpan<string>")]
    [Arguments("""shop.Query(new[] { "a", "b" });""", "ReadOnlySpan<string>")]
    [Arguments("""string[] names = ["a"]; index.Query(names, SortKeyCondition.Ge("x"));""", "ReadOnlySpan<string>")]
    [Arguments("""System.Span<string> names = new string[1]; shop.Query(names);""", "ReadOnlySpan<string>")]
    [Arguments("""System.ArraySegment<string> names = new string[1]; shop.Query(names);""", "ReadOnlySpan<string>")]
    [Arguments("""string name = "a"; shop.Query([name, .. new[] { "b" }]);""", "ReadOnlySpan<string>")]
    [Arguments("""shop.Query(shards);""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query(shards, SortKeyCondition.Ge("x"));""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query([1, 2]);""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query(["a", 2]);""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query([default(AttributeValue)]);""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query(new System.Collections.Generic.List<AttributeValue>());""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query(System.Linq.Enumerable.Empty<AttributeValue>());""", "IEnumerable<AttributeValue>")]
    [Arguments("""shop.Query(new System.Collections.Generic.List<string> { "a" });""", "IEnumerable<string>")]
    [Arguments("""index.Query(new System.Collections.Generic.List<string> { "a" }, SortKeyCondition.Ge("x"));""", "IEnumerable<string>")]
    [Arguments("""shop.Query(new System.Collections.Generic.HashSet<string> { "a" });""", "IEnumerable<string>")]
    [Arguments("""shop.Query(System.Linq.Enumerable.Select(new[] { 1, 2 }, static shard => "S#" + shard));""", "IEnumerable<string>")]
    [Arguments("""shop.Query((System.Collections.Generic.IEnumerable<string>)null!);""", "IEnumerable<string>")]
    [Arguments("""shop.Query((System.Collections.Generic.IList<string>)new string[1]);""", "IEnumerable<string>")]
    [Arguments("""shop.Query(default(AttributeValue));""", "AttributeValue")]
    [Arguments("""index.Query("a", SortKeyCondition.Ge("x"));""", "AttributeValue")]
    [Arguments("""index.Query(shards, default);""", "IEnumerable<AttributeValue>")]
    public async Task Compile_QueryOfPartitions_BindsToTheOverloadItAlwaysDid(string statement, string expected)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var bound = batch.Bound(statement, "Query");

        // Assert
        await Assert.That(bound?.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""shop.Query(UserProfile.Keys.Pk.Of("a"));""", "PartitionKey<UserProfile>")]
    [Arguments("""index.Query(UserProfile.Keys.Pk.Of("a"), SortKeyCondition.Ge("x"));""", "PartitionKey<UserProfile>")]
    [Arguments("""shop.Of<UserProfile>().Query(UserProfile.Keys.Pk.Of("a"));""", "PartitionKey<UserProfile>")]
    [Arguments("""shop.Of<UserProfile>().Query(new[] { UserProfile.Keys.Pk.Of("a") });""", "IEnumerable<PartitionKey<UserProfile>>")]
    [Arguments("""shop.Of<UserProfile>().Query([UserProfile.Keys.Pk.Of("a")], SortKeyCondition.Ge("x"));""", "IEnumerable<PartitionKey<UserProfile>>")]
    [Arguments("""shop.Query(new System.Collections.Generic.List<PartitionKey<UserProfile>>());""", "IEnumerable<PartitionKey<UserProfile>>")]
    public async Task Compile_QueryOfTypedPartitions_BindsToTheExtensionOnlyWhenNoMemberApplies(string statement, string expected)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var bound = batch.Bound(statement, "Query");

        // Assert
        await Assert.That((bound?.IsExtensionMethod, bound?.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))).IsEqualTo((true, expected));
    }

    [Test]
    [Arguments(LanguageVersion.CSharp12, "CS0121")]
    [Arguments(LanguageVersion.CSharp13, "")]
    [Arguments(LanguageVersion.CSharp14, "")]
    public async Task Compile_ArrayOfStrings_ReliesOnThePriorityOfCSharp13(LanguageVersion language, string expected)
    {
        // Arrange (a compiler below C# 13 ignores the attribute, and an array reaches both overloads of strings)
        var source = Declarations + Case.Replace("STATEMENT", """string[] names = new[] { "a" }; shop.Query(names);""", StringComparison.Ordinal) + End;

        // Act
        var errors = GeneratorRun.CreateCompilation(source, language).GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(static diagnostic => diagnostic.Id);

        // Assert
        await Assert.That(string.Join(",", errors.Distinct())).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""await shop.Update("a").Set(Device.Attrs.Serial, null);""")]
    [Arguments("""string? serial = null; await shop.Update("a").Set(Device.Attrs.Serial, serial);""")]
    [Arguments("""await shop.Update("a").Set(Device.Attrs.Slot, null);""")]
    [Arguments("""await shop.Update("a").SetIfNotNull(Device.Attrs.Serial, null);""")]
    [Arguments("""string? serial = null; await shop.Update("a").SetIfNotNull(Device.Attrs.Serial, serial);""")]
    [Arguments("""await shop.Update("a").SetIfNotNull(Device.Attrs.Slot, null);""")]
    public async Task Compile_NullThroughAHandleOfSet_DoesNotWarn(string statement)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var warnings = batch.Diagnostics(statement, DiagnosticSeverity.Warning);

        // Assert
        await Assert.That(warnings).DoesNotContain("CS8620").And.DoesNotContain("CS8625");
    }

    [Test]
    [Arguments("""await shop.Update("a").SetIfNotExists(Device.Attrs.Serial, null);""")]
    [Arguments("""await shop.Update("a").Delete(Device.Attrs.Serial, null);""")]
    public async Task Compile_NullThroughAHandleOfAnotherAction_WarnsAtTheCall(string statement)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var warnings = batch.Diagnostics(statement, DiagnosticSeverity.Warning);

        // Assert
        await Assert.That(warnings).Contains("CS8620");
    }

    [Test]
    [Arguments("""string name = (await shop.Of<Handles>().Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Nickname)).Value;""")]
    [Arguments("""await foreach (string name in shop.Of<Handles>().Scan().Select(Handles.Attrs.Nickname).ValuesAsync()) { }""")]
    public async Task Compile_ValueOfANullableHandle_IsNullable(string statement)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var warnings = batch.Diagnostics(statement, DiagnosticSeverity.Warning);

        // Assert
        await Assert.That(warnings).Contains("CS8600");
    }

    [Test]
    [Arguments("""string name = (await shop.Of<Handles>().Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Id)).Value;""")]
    [Arguments("""await foreach (string name in shop.Of<Handles>().Scan().Select(Handles.Attrs.Id).ValuesAsync()) { }""")]
    public async Task Compile_ValueOfAHandleThatCannotBeNull_IsNotNullable(string statement)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var warnings = batch.Diagnostics(statement, DiagnosticSeverity.Warning);

        // Assert
        await Assert.That(warnings).DoesNotContain("CS8600");
    }

    [Test]
    [Arguments("""shop.Update("a").Set(Device.Attrs.Slot, 1);""", "Int32")]
    [Arguments("""shop.Update("a").Set(Device.Attrs.Slot, default);""", "Int32")]
    [Arguments("""short small = 1; shop.Update("a").Set(Device.Attrs.Slot, small);""", "Int32")]
    [Arguments("""shop.Update("a").Set(Device.Attrs.Serial, "a");""", "String")]
    [Arguments("""shop.Update("a").Set(Device.Attrs.Serial, default);""", "String")]
    [Arguments("""shop.Update("a").Set(Device.Attrs.Slot, (int?)null);""", "Nullable`1")]
    [Arguments("""shop.Update("a").SetIfNotNull(Device.Attrs.Slot, 1);""", "Int32")]
    [Arguments("""shop.Update("a").SetIfNotNull(Device.Attrs.Serial, "a");""", "String")]
    [Arguments("""shop.Update("a").SetIfNotNull(Device.Attrs.Slot, (int?)null);""", "Nullable`1")]
    public async Task Compile_SetByHandle_TakesTheNullableOverloadOnlyForANullableValue(string statement, string expected)
    {
        // Arrange
        var batch = BatchOf();

        // Act
        var bound = batch.Bound(statement, "Set", "SetIfNotNull");

        // Assert
        await Assert.That(bound?.Parameters[1].Type.MetadataName).IsEqualTo(expected);
    }

    private static Batch BatchOf([CallerMemberName] string testMethod = "") =>
        Batches.GetOrAdd(testMethod, static name => new Lazy<Batch>(() => new Batch(StatementsOf(name)))).Value;

    private static IEnumerable<string> StatementsOf(string testMethod) =>
        typeof(CompileGuardTests).GetMethod(testMethod)!.GetCustomAttributes<ArgumentsAttribute>().Select(static row => (string)row.Values[0]!);

    // The rows of a test, each in a method of its own in one compilation, because a compilation costs far more than a statement. A method body binds apart from the others and flow analysis stops
    // at its end, so the errors of one row cannot reach another. An error outside the rows is one that no row would look at, so it stops the batch.
    private sealed class Batch
    {
        private readonly Compilation _compilation;
        private readonly ImmutableArray<Diagnostic> _diagnostics;
        private readonly Dictionary<string, MethodDeclarationSyntax> _rows;

        public Batch(IEnumerable<string> statements)
        {
            var rows = statements.Distinct(StringComparer.Ordinal).ToList();
            var cases = rows.Select(static (statement, index) => Case.Replace("RunAsync", $"Row{index}", StringComparison.Ordinal).Replace("STATEMENT", statement, StringComparison.Ordinal));
            _compilation = GeneratorRun.CreateCompilation(Declarations + string.Concat(cases) + End, LanguageVersion.Latest);
            _diagnostics = _compilation.GetDiagnostics();
            var methods = _compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(static method => method.Identifier.ValueText.StartsWith("Row", StringComparison.Ordinal));
            _rows = rows.Zip(methods).ToDictionary(static row => row.First, static row => row.Second, StringComparer.Ordinal);

            var strays = _diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && !_rows.Values.Any(row => row.FullSpan.Contains(diagnostic.Location.SourceSpan))).ToList();
            if (strays.Count > 0)
            {
                throw new InvalidOperationException($"Errors outside the rows: {string.Join(", ", strays)}");
            }
        }

        public List<string> Diagnostics(string statement, DiagnosticSeverity severity)
        {
            var row = _rows[statement].FullSpan;
            return [.. _diagnostics.Where(diagnostic => diagnostic.Severity == severity && row.Contains(diagnostic.Location.SourceSpan)).Select(static diagnostic => diagnostic.Id).Distinct()];
        }

        // The one call by one of these names in the statement, as the compiler binds it.
        public IMethodSymbol? Bound(string statement, params string[] names)
        {
            var call = _rows[statement].DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(call => call.Expression is MemberAccessExpressionSyntax member && names.Contains(member.Name.Identifier.ValueText));
            return (IMethodSymbol?)_compilation.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol;
        }
    }
}
