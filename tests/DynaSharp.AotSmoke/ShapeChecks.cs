using System.Net;
using System.Text;
using DynaSharp.Mapping;
using DynaSharp.Testing;
using Formats = DynaSharp.AotSmoke.KeyFormats.Keys;

namespace DynaSharp.AotSmoke;

/// <summary>
/// Runs what the generator emits for each shape of <c>Shapes.cs</c> in the Native AOT binary: key builders, the attributes of an item in both directions, and
/// the typed commands over handles and generated keys. Generic code over struct converters and static abstract members has to be compiled ahead of time
/// here, so what a shape needs and AOT does not see fails the publish or this run.
/// </summary>
internal static class ShapeChecks
{
    private const string Table = "Shop";

    private static readonly Guid Id = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid User = new("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTime NoonUtc = Noon.UtcDateTime;

    public static void CheckKeys(Action<bool, string> check)
    {
        var sk = KeyedOrder.Keys.Sk.Format(Noon, Id);
        var prefix = KeyedOrder.Keys.Sk.Prefix(Noon);
        check(sk == "ORDER#2026-10-31T12:00:00.000Z#11111111-1111-1111-1111-111111111111", "Format encodes a composite key");
        check(KeyedOrder.Keys.Sk.Parse(sk) == new KeyedOrder.Keys.SkParts(Noon, Id), "Parse reads a composite key");
        check(KeyedOrder.Keys.Sk.Parse(Encoding.UTF8.GetBytes(sk)).OrderId == Id, "Parse reads UTF-8");
        check(!KeyedOrder.Keys.Sk.TryParse("ORDER#nonsense", out _) && !KeyedOrder.Keys.Sk.TryParse((string?)null, out _), "TryParse refuses what does not match");
        check(KeyedOrder.Keys.Sk.Prefix() == "ORDER#" && prefix == "ORDER#2026-10-31T12:00:00.000Z#", "Prefix stops at a field");
        check(string.CompareOrdinal(KeyedOrder.Keys.Sk.UpperBound(Noon), sk) > 0 && KeyedOrder.Keys.Sk.UpperBound(Noon).StartsWith(prefix, StringComparison.Ordinal), "UpperBound is above every key of the field");
        check(KeyedOrder.Keys.Gsi1Sk.Format(-12.5m) == "N9999999987.49" && KeyedOrder.Keys.Gsi1Sk.Parse("N9999999987.49").Total == -12.5m, "a fixed point key is signed");
        check(KeyedOrder.Keys.Lsi1Sk.Parse("TOTAL#P0000000012.50").Total == 12.5m, "a local index has builders");
        check(KeyedOrder.Keys.Gsi2Sk.Parse(KeyedOrder.Keys.Gsi2Sk.Format(Noon)).ShippedAt == Noon, "a sparse index has builders");

        check(KeyedMember.Keys.Sk.Prefix("eu") == "REGION#eu#CITY#" && KeyedMember.Keys.Sk.PrefixPartial("eu", "zu") == "REGION#eu#CITY#zu", "strings have partial prefixes");
        check(Throws<KeyFormatException>(() => KeyedMember.Keys.Pk.Format("a#b", "c")), "a delimiter in a value throws");
        check(Suffixed.Keys.Sk.Format("x") == "A-x#B" && Suffixed.Keys.Sk.Parse("A-x#B").Name == "x", "a literal may follow the last placeholder");
        check(Adjacent.Keys.Sk.Parse("007abc") == new Adjacent.Keys.SkParts(7, "abc"), "a fixed width needs no delimiter");
        check(Ledger.Keys.Pk.Format(Noon) == "LEDGER#2026-10" && Ledger.Keys.Sk.Parse(Ledger.Keys.Sk.Format(Noon, Id)).At == Noon, "one property has two formats");
        check(Edge.Keys.InvertedPk.Format("b") == "TO#b" && Edge.Keys.InvertedSk.Format("a") == "FROM#a", "an inverted index has builders");
        check(Article.Keys.Pk.Format("a-b") == "SLUG#a-b", "a computed property feeds a key");
        check(KeyedAccount.Keys.Sk.Parse(KeyedAccount.Keys.Sk.Format("a", 7)).Version == 7, "a plain key and a template share a property");
        check(@class.Keys.Pk.Parse(@class.Keys.Pk.Format("x")).@string == "x", "a template with escapes and keywords parses");
        check(Outer.Middle.Point.Keys.Pk.Parse("POINT#-3").X == -3, "a nested struct has builders");

        var formats = new KeyFormats
        {
            Text = "t",
            Id = Id,
            Plan = Plan.Pro,
            Grade = Grade.High,
            SByte = -5,
            Byte = 250,
            Short = -300,
            UShort = 60000,
            Int = 70000,
            UInt = 4_000_000_000,
            Long = long.MinValue,
            ULong = ulong.MaxValue,
            Money = 12.5m,
            At = Noon.AddTicks(1234),
            Utc = NoonUtc.AddTicks(1234),
            Date = new DateOnly(2026, 10, 31),
            Address = IPAddress.Parse("10.0.0.1"),
        };
        var stored = Item.From(formats);
        check(Through(formats) == formats, "the key formats round trip, the parsable value through its key");
        check(
            Formats.GuidPk.Parse(Key("guid")).Id == formats.Id && Formats.NamePk.Parse(Key("name")).Plan == formats.Plan && Formats.ValuePk.Parse(Key("value")).Plan == formats.Plan
            && Formats.ByteEnumPk.Parse(Key("byteenum")).Grade == formats.Grade && Formats.CustomPk.Parse(Key("custom")).Address.Equals(formats.Address),
            "guids, enums and parsable values parse");
        check(
            Formats.SbytePk.Parse(Key("sbyte")).SByte == formats.SByte && Formats.BytePk.Parse(Key("byte")).Byte == formats.Byte && Formats.ShortPk.Parse(Key("short")).Short == formats.Short
            && Formats.UshortPk.Parse(Key("ushort")).UShort == formats.UShort && Formats.IntPk.Parse(Key("int")).Int == formats.Int && Formats.UintPk.Parse(Key("uint")).UInt == formats.UInt
            && Formats.LongPk.Parse(Key("long")).Long == formats.Long && Formats.UlongPk.Parse(Key("ulong")).ULong == formats.ULong && Formats.PlainPk.Parse(Key("plain")).Int == formats.Int,
            "integers parse");
        check(
            Formats.DigitsIntPk.Parse(Key("digitsint")).Int == formats.Int && Formats.DigitsBytePk.Parse(Key("digitsbyte")).Byte == formats.Byte && Formats.DigitsULongPk.Parse(Key("digitsulong")).ULong == formats.ULong
            && Formats.FixedIntPk.Parse(Key("fixedint")).Int == formats.Int && Formats.FixedULongPk.Parse(Key("fixedulong")).ULong == formats.ULong
            && Formats.DecimalPk.Parse(Key("decimal")).Money == formats.Money && Formats.FixedDecimalPk.Parse(Key("fixeddecimal")).Money == formats.Money,
            "fixed widths parse");
        check(
            Formats.YearPk.Parse(Key("year")).At.Year == 2026 && Formats.MonthPk.Parse(Key("month")).At.Month == 10 && Formats.DayPk.Parse(Key("day")).At.Day == 31
            && Formats.HourPk.Parse(Key("hour")).At.Hour == 12 && Formats.MinutePk.Parse(Key("minute")).At.Minute == 0 && Formats.SecondPk.Parse(Key("second")).At.Second == 0
            && Formats.MilliPk.Parse(Key("milli")).At == Noon && Formats.MicroPk.Parse(Key("micro")).At == Noon.AddTicks(1230) && Formats.TicksPk.Parse(Key("ticks")).At == formats.At
            && Formats.DateTimePk.Parse(Key("datetime")).Utc == NoonUtc && Formats.DateTimeTicksPk.Parse(Key("datetimeticks")).Utc == formats.Utc && Formats.DatePk.Parse(Key("date")).Date == formats.Date,
            "times parse");

        string Key(string index) => stored.GetString(index + "pk")!;
    }

    public static void CheckShapes(Action<bool, string> check)
    {
        var order = NewOrder();
        var readOrder = Through(order);
        check(
            readOrder is { Status: Plan.Pro, Total: 12.5m, TrackingId: not null, ShipTo.Street.Number: 1, Revision: 4 } && readOrder.Tags.Contains("gift") && readOrder.Plans.Contains(Plan.Free) && readOrder.ExpiresAt == Noon,
            "an entity with keys, documents, sets and a version round trips");
        var sparse = Item.From(order with { ShippedAt = null });
        check(!sparse.ContainsKey("gsi2pk") && !sparse.ContainsKey("gsi2sk"), "a sparse index writes no key when an input is missing");
        check(Throws<DynamoDbMappingException>(() => new Item { ["_type"] = "ORDER" }.To<KeyedOrder>()), "a missing required attribute throws");

        var restored = new Restored("u", Plan.Enterprise, 3) { At = NoonUtc.AddTicks(1), Id = Id, Note = "n" };
        var visited = Item.From(restored);
        check(Through(restored) == restored && !visited.ContainsKey("User") && !visited.ContainsKey("At"), "properties that only keys hold are restored");

        var mutable = new Mutable { Id = "m", Plan = "pro", Aliases = ["a"], Opened = new DateOnly(2026, 1, 2), Scratch = "lost" };
        mutable.Upgrade(2);
        var readMutable = Through(mutable);
        check(readMutable is { Version: 2, Plan: "pro", Length: 3, Scratch: "" } && readMutable.Aliases[0] == "a" && Item.From(mutable).GetNumber<int>("len") == 3, "a class with setters of every access round trips");

        var point = new StructPoint { Id = "p", X = 1.5, Y = 2.5, Tag = Id };
        check(Through(point) is { Id: "p", X: 1.5, Y: 2.5 } readPoint && readPoint.Tag == Id, "a struct entity round trips");
        var size = new Flat { Floor = 2, Rent = new Money(100m), Area = new Size(3, 4), Tag = new Tagged(1) { Label = "l" } };
        check(Through(size) == size, "record structs and a read-only struct round trip");

        var positional = Through(new Positional("p", "n") { Home = new Street("s", 1), Scores = [1] });
        check(positional is { Age: 18, Limit: 1.5m, Motto: "hello \"world\"", Balance: Balance.Minus, Nick: null, Badges: null, Home.Number: 1 } && positional.Scores.Contains(1), "constructor defaults survive");

        var inner = Through(new Outer.Middle.Inner { Id = "i", Kind = Outer.Middle.Kind.Two, Deep = new Outer.Middle.IInner.Deep(Outer.Middle.Kind.One) });
        check(inner is { Kind: Outer.Middle.Kind.Two, Deep.Kind: Outer.Middle.Kind.One }, "nested types round trip");
        check(Through(new Outer.Middle.Point(1, 2)) == new Outer.Middle.Point(1, 2), "a nested record struct round trips");
        check(Through(new GlobalThing { Id = "g", Color = Color.Green }).Color == Color.Green, "an entity in the global namespace round trips");

        var keywords = Through(new @class { @string = "s", @event = "e", @base = @enum.@class, @this = @enum.plain, @double = 3, @fixed = new @object(4), name = "n", fields = "f", writer = "w", reader = "r", entity = "t", attribute = "a", exception = "x" });
        check(keywords is { @base: @enum.@class, @this: @enum.plain, @double: 3, @fixed.@int: 4, fields: "f", writer: "w", reader: "r", entity: "t", attribute: "a", name: "n", exception: "x" }, "keywords round trip");
        check(Item.From(keywords).GetString("the \"type\"") == "say \"hi\"\\", "a discriminator needs escaping");

        var grant = new Grant
        {
            Rights = Rights.Read | Rights.Write,
            Wide = Wide.High,
            Each = [Rights.Read],
            Color = Color.Crimson,
            Colors = [Color.Green],
            Others = new HashSet<Color> { Color.Red },
            Sparse = [Color.Green, null],
            Grade = Grade.High,
            Balance = Balance.Plus,
        };
        var readGrant = Through(grant);
        check(
            readGrant is { Rights: Rights.All, Wide: Wide.High, Color: Color.Red, Grade: Grade.High, Balance: Balance.Plus } && readGrant.Each.Contains(Rights.Read) && readGrant.Colors.Contains(Color.Green) && readGrant.Others!.Contains(Color.Red) && readGrant.Sparse[1] is null,
            "enums of each kind round trip");
        check(Throws<DynamoDbMappingException>(() => Item.From(new Grant { Color = (Color)42 })), "an undefined enum value throws");

        var everything = NewEverything();
        var read = Through(everything);
        check(
            read.Text == "t" && read.Flag && read.Tiny == 1 && read.Big == long.MaxValue && read.Single == 1.5f && read.Double == 2.5 && read.Huge == Int128.MaxValue && read.When == NoonUtc && read.Day == everything.Day && read.Clock == everything.Clock && read.Span == everything.Span,
            "every scalar round trips");
        check(read.Bytes.SequenceEqual(everything.Bytes) && read.Memory.Span.SequenceEqual(everything.Memory.Span) && read.Raw.AsString() == "raw" && read.MaybeRaw is null, "binary and raw values round trip");
        check(read.Numbers[1] == 2 && read.Strings[0] == "s" && read.Longs[0] == 3 && read.Leaves[0].Name == "l" && read.Grid[0][1] == 2 && read.Holes[1] is null, "every list round trips");
        check(read.StringSet.Contains("s") && read.NumberSet.Contains(1.5m) && read.BinarySet.Single()[0] == 9 && read.GuidSet.Contains(Id) && read.ReadOnlySet.Contains(7) && read.ArraySet[0] == "a", "every set round trips");
        check(read.Map["m"] == 1 && read.Leaf["k"].Name == "v" && read.Maybe?.Name == "n", "every map and document round trips");
        check(read.Pair == (1, 2) && read.Coded == new Code("c") && read.MaybeCoded == new Code("d") && read.Codes[0] == new Code("e"), "converters on a property and on a type round trip");

        var tree = Through(new Tree { Name = "root", Parent = new Tree { Name = "up" }, Children = [new Tree { Name = "child", Children = [new Tree { Name = "leaf" }] }] });
        check(tree is { Parent.Name: "up" } && tree.Children[0].Children[0].Name == "leaf", "a recursive document round trips");
        check(Through(new Empty()) is not null, "a document without attributes round trips");

        check(Item.From(new Foreign { Id = "f" }).Count == 1 && Through(new Foreign { Id = "f" }).Id == "f", "an entity can opt out of the discriminator");
        check(Item.From(new Own { Id = 1, Revision = 2 }).GetString("kind") == "OWN" && Through(new Own { Id = 1, Revision = 2 }).Revision == 2, "a discriminator and a version go by other names");
        check(Through(new BoundToTheConstructor("n") { Name = "m" }).Name == "m", "a constructor takes a required member");
        check(Through(new InheritedRequired { Author = "a" }).Author == "a", "a required member can be inherited");
        check(Through(new NullableRequired { Maybe = null }).Maybe is null, "a required member can be null");
        check(Through(new AuditEvent { Id = "e", Actor = "a" }).Actor == "a" && AuditEvent.Keys.Pk.Format("e") == "AUDIT#e" && BaseEvent.Keys.Pk.Format("e") == "EVENT#e", "an entity inherits an entity");
        check(Through(new ForeignAddress { City = "c", Country = "CH", Street = new Street("s", 2) }) == new ForeignAddress { City = "c", Country = "CH", Street = new Street("s", 2) }, "a document inherits a document");
        check(Through(new InternalEntity { Kind = InternalKind.Two, Kinds = [InternalKind.One] }).Kinds.Contains(InternalKind.One), "an internal entity round trips");
        check(Through(new Device { Serial = "s", Slot = 1, Plan = Plan.Pro }).Slot == 1 && Through(new Quota { Plan = Plan.Pro }).Plan == Plan.Pro, "plain keys round trip");
        check(Through(new SiteSettings { Theme = "t" }).Theme == "t" && Through(new UserProfile { Id = "u" }).Id == "u", "literal keys round trip");
        check(Through(new Edge { Source = "a", Target = "b" }).Target == "b" && Through(new Ledger { At = Noon, Id = Id }).Id == Id, "shared attributes round trip");
    }

    public static async Task CheckCommandsAsync(StubHandler handler, DynamoDbClient client, Action<bool, string> check)
    {
        var orders = client.Table<KeyedOrder>(Table);
        var points = client.Table<StructPoint>(Table);
        var order = NewOrder();
        var key = KeyedOrder.Keys.Of(User, Noon, Id);
        var point = new StructPoint { Id = "p", X = 1.5 };
        var stored = "{\"Item\":" + Item.From(order) + "}";

        handler.Enqueue(StubResponse.Json("{}"));
        await orders.Put(order with { Revision = 0 }).IfNotExists().SendAsync();
        check(handler.Requests[^1].BodyText.Contains("\"gsi2pk\":{\"S\":\"TRACK#", StringComparison.Ordinal), "Put writes the keys of the indexes");

        handler.Enqueue(StubResponse.Json(stored));
        var got = await orders.Get(key).Consistent().SendAsync();
        check(got.Item?.Status == Plan.Pro && handler.Requests[^1].BodyText.Contains("\"sk\":{\"S\":\"ORDER#2026-10-31T12:00:00.000Z#", StringComparison.Ordinal), "Get takes a generated key");

        handler.Enqueue(StubResponse.Json("{\"Items\":[" + Item.From(order) + "],\"Count\":1,\"ScannedCount\":2}"));
        var page = await orders.Query(KeyedOrder.Keys.Pk.Format(User), SortKey.BeginsWith(KeyedOrder.Keys.Sk.Prefix()))
            .Filter(KeyedOrder.Attrs.Status.Eq(Plan.Pro).And(KeyedOrder.Attrs.Total.Gt(1m)))
            .Project(KeyedOrder.Attrs.Status, KeyedOrder.Attrs.ShipTo.At(PostalAddress.Attrs.City))
            .Descending()
            .PageAsync();
        check(page.Items.Single().OrderId == Id && handler.Requests[^1].BodyText.Contains("\"ProjectionExpression\"", StringComparison.Ordinal), "Query filters and projects by handles");

        handler.Enqueue(StubResponse.Json("{\"Items\":[" + Item.From(order) + "],\"Count\":1,\"ScannedCount\":1}"));
        var byStatus = await orders.Index("GSI1").Query(KeyedOrder.Keys.Gsi1Pk.Format(Plan.Pro), SortKey.Between(KeyedOrder.Keys.Gsi1Sk.Format(-1m), KeyedOrder.Keys.Gsi1Sk.Format(20m))).ItemsAsync().ToListAsync();
        check(byStatus.Count == 1 && handler.Requests[^1].BodyText.Contains("\"IndexName\":\"GSI1\"", StringComparison.Ordinal), "an entity knows its indexes");

        handler.Enqueue(StubResponse.Json("{\"Count\":3,\"ScannedCount\":3}"));
        check(await orders.Index("LSI1").Scan().CountAsync() == 3, "an index can be counted");

        handler.Enqueue(StubResponse.Json("{\"Attributes\":{\"Quantity\":{\"N\":\"3\"}}}"));
        var quantity = await orders.Update(key)
            .Set(KeyedOrder.Attrs.ShipTo.At(PostalAddress.Attrs.City), "Thun")
            .Set(KeyedOrder.Attrs.Status, Plan.Enterprise)
            .SetIfNotExists(KeyedOrder.Attrs.ExpiresAt, Noon)
            .Add(KeyedOrder.Attrs.Quantity, 1)
            .Add(KeyedOrder.Attrs.Tags, ["b"])
            .Delete(KeyedOrder.Attrs.Plans, [Plan.Free])
            .Remove(KeyedOrder.Attrs.TrackingId)
            .If(KeyedOrder.Attrs.Total.Between(0m, 20m))
            .IfVersion(1)
            .Return(KeyedOrder.Attrs.Quantity)
            .SendAsync();
        check(quantity == 3, "Update takes handles of each kind and returns one typed");

        handler.Enqueue(StubResponse.Json("{}"));
        await orders.Delete(key).If(KeyedOrder.Attrs.ShipTo.Exists()).IgnoreVersion().SendAsync();
        check(handler.Requests[^1].Operation == "DeleteItem", "Delete takes a generated key");

        handler.Enqueue(StubResponse.Json("{}"));
        await points.Put(point).SendAsync();
        handler.Enqueue(StubResponse.Json("{\"Item\":" + Item.From(point) + "}"));
        var gotPoint = await points.Get(StructPoint.Keys.Of("p")).SendAsync();
        check(gotPoint is { Found: true, Item.X: 1.5 }, "a table of a struct entity reads and writes");

        handler.Enqueue(StubResponse.Json("{\"Attributes\":" + Item.From(point) + "}"));
        var replaced = await points.Put(point).ReturnOld().SendAsync();
        check(replaced.Attributes is { X: 1.5 }, "a command returns a struct entity");

        handler.Enqueue(StubResponse.Json("{}"));
        var written = await orders.BatchWrite().Put(order).Delete(key).Put(points, point).Delete(points, StructPoint.Keys.Of("q")).IgnoreVersion().SendAsync();
        check(written.Written == 4, "a batch mixes entities and generated keys");

        handler.Enqueue(StubResponse.Json("{\"Responses\":{\"Shop\":[" + Item.From(order) + "]}}"));
        var found = await orders.BatchGet().Key(key).Project(KeyedOrder.Attrs.Status).SendAsync();
        check(found.Items.Single().Status == Plan.Pro, "BatchGet takes a generated key and a handle");

        handler.Enqueue(StubResponse.Json("{\"Responses\":[" + stored + ",{\"Item\":" + Item.From(point) + "}]}"));
        var snapshot = await client.TransactGet().Get(orders, key, out var orderSlot).Get(points, StructPoint.Keys.Of("p"), out var pointSlot).SendAsync();
        check(snapshot.Read(orderSlot).Item?.Status == Plan.Pro && snapshot.Read(pointSlot).Item is { X: 1.5 }, "TransactGet reads entities by slot");

        handler.Enqueue(StubResponse.Json("{}"));
        await client.TransactWrite()
            .Put(orders, order with { Revision = 0 }).IfNotExists()
            .Update(orders, key).Add(KeyedOrder.Attrs.Quantity, 1).If(KeyedOrder.Attrs.Quantity.Lt(10)).IfVersion(1)
            .Check(points, StructPoint.Keys.Of("p")).If(StructPoint.Attrs.X.Gt(0d))
            .Delete(points, StructPoint.Keys.Of("q"))
            .SendAsync();
        check(handler.Requests[^1].Operation == "TransactWriteItems", "a transaction takes entities, handles and generated keys");
    }

    private static T Through<T>(T value)
        where T : IDynamoDbDocument<T> => Item.From(value).To<T>();

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static KeyedOrder NewOrder() => new()
    {
        UserId = User,
        CreatedAt = Noon,
        OrderId = Id,
        Status = Plan.Pro,
        Total = 12.5m,
        TrackingId = Id,
        ShippedAt = Noon,
        ShipTo = new ForeignAddress { City = "Bern", Country = "CH", Street = new Street("Main", 1) },
        Tags = ["gift"],
        Plans = [Plan.Free],
        ExpiresAt = Noon,
        Revision = 4,
    };

    private static Everything NewEverything() => new()
    {
        Text = "t",
        Flag = true,
        Tiny = 1,
        Big = long.MaxValue,
        Single = 1.5f,
        Double = 2.5,
        Huge = Int128.MaxValue,
        When = NoonUtc,
        Day = new DateOnly(2026, 10, 31),
        Clock = new TimeOnly(12, 30, 15),
        Span = TimeSpan.FromMinutes(90),
        Bytes = [1, 2],
        Memory = new byte[] { 3, 4 },
        Raw = "raw",
        Numbers = [1, 2],
        Strings = ["s"],
        Longs = [3],
        Leaves = [new Leaf { Name = "l" }],
        Grid = [[1, 2]],
        Holes = ["a", null],
        StringSet = ["s"],
        NumberSet = [1.5m],
        BinarySet = [[9]],
        GuidSet = [Id],
        ReadOnlySet = new HashSet<int> { 7 },
        ArraySet = ["a"],
        Map = new Dictionary<string, int> { ["m"] = 1 },
        Leaf = new Dictionary<string, Leaf> { ["k"] = new Leaf { Name = "v" } },
        Maybe = new Leaf { Name = "n" },
        Pair = (1, 2),
        Coded = new Code("c"),
        MaybeCoded = new Code("d"),
        Codes = [new Code("e")],
    };
}
