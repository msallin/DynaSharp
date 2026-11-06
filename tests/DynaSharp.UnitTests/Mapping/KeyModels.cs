using System;
using System.Collections.Generic;
using System.Net;
using DynaSharp;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

public enum Plan
{
    Free,
    Pro,
    Enterprise,
}

public enum Tier : byte
{
    Low = 1,
    High = 200,
}

[Flags]
public enum Features
{
    None = 0,
    Reports = 1,
    Export = 2,
}

[DynamoDbDocument]
public partial record Street(string Name, int Number);

[DynamoDbDocument]
public partial record PostalAddress
{
    public required string City { get; init; }

    public Street? Street { get; init; }
}

[DynamoDbDocument]
public partial record ForeignAddress : PostalAddress
{
    public string? Country { get; init; }
}

/// <summary>The realistic shape: a composite primary key, a global index, a sparse one and a local one.</summary>
[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("USER#{UserId}")]
[SortKey("ORDER#{CreatedAt}#{OrderId}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Total:n10.2}")]
[GlobalIndexKey("GSI2", PartitionKey = "TRACK#{TrackingId}", SortKey = "{ShippedAt}", Sparse = true)]
[LocalIndexKey("LSI1", "TOTAL#{Total:n10.2}")]
public partial record KeyedOrder
{
    public required Guid UserId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid OrderId { get; init; }

    public required Plan Status { get; init; }

    public required decimal Total { get; init; }

    public Guid? TrackingId { get; init; }

    public DateTimeOffset? ShippedAt { get; init; }

    public PostalAddress? ShipTo { get; init; }

    public int Quantity { get; init; }

    public HashSet<string> Tags { get; init; } = new();
}

/// <summary>Three indexes over one partition key attribute, two of them behind gates of their own: an item belongs to each index whose inputs it has.</summary>
[DynamoDbEntity]
[Discriminator("GATED")]
[PartitionKey("{Id}")]
[GlobalIndexKey("BySeller", PartitionKey = "TENANT#{Tenant}", PartitionKeyAttributeName = "shared", SortKey = "SELLER#{Seller}", Sparse = true)]
[GlobalIndexKey("ByBuyer", PartitionKey = "TENANT#{Tenant}", PartitionKeyAttributeName = "shared", SortKey = "BUYER#{Buyer}", Sparse = true)]
public partial record GatedSale
{
    public required string Id { get; init; }

    public required string Tenant { get; init; }

    public string? Seller { get; init; }

    public string? Buyer { get; init; }
}

/// <summary>
/// Sparse indexes gated by an empty string as by null: each input is a string with an [OmitEmpty] of its own that feeds no key but those of sparse indexes. ByCode has a gate of its own, and the
/// shared attribute of BySeller and ByBuyer has its own input and the inputs of the two sort keys as alternatives.
/// </summary>
[DynamoDbEntity]
[Discriminator("LISTING")]
[PartitionKey("{Id}")]
[GlobalIndexKey("ByCode", PartitionKey = "CODE#{Code}", Sparse = true)]
[GlobalIndexKey("BySeller", PartitionKey = "TENANT#{Tenant}", PartitionKeyAttributeName = "shared", SortKey = "SELLER#{Seller}", Sparse = true)]
[GlobalIndexKey("ByBuyer", PartitionKey = "TENANT#{Tenant}", PartitionKeyAttributeName = "shared", SortKey = "BUYER#{Buyer}", Sparse = true)]
public partial record BlankListing
{
    public required string Id { get; init; }

    [OmitEmpty]
    public string Code { get; init; } = string.Empty;

    [OmitEmpty]
    public string? Tenant { get; init; }

    [OmitEmpty]
    public string Seller { get; init; } = string.Empty;

    public string? Buyer { get; init; }
}

/// <summary>A shared attribute whose own input is nullable, and an index that needs no more than that input.</summary>
[DynamoDbEntity]
[Discriminator("NICK")]
[PartitionKey("{Id}")]
[GlobalIndexKey("ByNickAndAge", PartitionKey = "NICK#{Nick}", PartitionKeyAttributeName = "shared", SortKey = "{Age:d3}", Sparse = true)]
[GlobalIndexKey("ByNick", PartitionKey = "NICK#{Nick}", PartitionKeyAttributeName = "shared", Sparse = true)]
public partial record NickName
{
    public required string Id { get; init; }

    public string? Nick { get; init; }

    public int? Age { get; init; }
}

/// <summary>A sparse index and one that is not over one attribute: the attribute has no gate at all, and it is the sort key that is shared.</summary>
[DynamoDbEntity]
[Discriminator("LOOSE")]
[PartitionKey("{Id}")]
[GlobalIndexKey("Open", PartitionKey = "O#{Id}", SortKey = "T#{Tenant}", SortKeyAttributeName = "shared")]
[GlobalIndexKey("Gated", PartitionKey = "G#{Note}", SortKey = "T#{Tenant}", SortKeyAttributeName = "shared", Sparse = true)]
public partial record LooseGate
{
    public required string Id { get; init; }

    public required string Tenant { get; init; }

    public string? Note { get; init; }
}

/// <summary>Several strings in one template, for delimiters, prefixes and partial prefixes.</summary>
[DynamoDbEntity]
[Discriminator("MEMBER")]
[PartitionKey("TENANT#{Tenant}#USER#{Id}")]
[SortKey("REGION#{Region}#CITY#{City}")]
public partial record Member
{
    public required string Tenant { get; init; }

    public required string Id { get; init; }

    public required string Region { get; init; }

    public required string City { get; init; }
}

/// <summary>Every row of the format table, each as a partition key of its own, where no ordering warning applies.</summary>
[DynamoDbEntity]
[Discriminator("FORMATS")]
[PartitionKey("{Text}")]
[GlobalIndexKey("guid", PartitionKey = "G#{Id}")]
[GlobalIndexKey("name", PartitionKey = "E#{Plan}")]
[GlobalIndexKey("value", PartitionKey = "E#{Plan:d3}")]
[GlobalIndexKey("byteEnum", PartitionKey = "E#{Tier:d3}")]
[GlobalIndexKey("sbyte", PartitionKey = "I#{SByte}")]
[GlobalIndexKey("byte", PartitionKey = "I#{Byte}")]
[GlobalIndexKey("short", PartitionKey = "I#{Short}")]
[GlobalIndexKey("ushort", PartitionKey = "I#{UShort}")]
[GlobalIndexKey("int", PartitionKey = "I#{Int}")]
[GlobalIndexKey("uint", PartitionKey = "I#{UInt}")]
[GlobalIndexKey("long", PartitionKey = "I#{Long}")]
[GlobalIndexKey("ulong", PartitionKey = "I#{ULong}")]
[GlobalIndexKey("plain", PartitionKey = "I#{Int:plain}")]
[GlobalIndexKey("digitsInt", PartitionKey = "D#{Int:d12}")]
[GlobalIndexKey("digitsByte", PartitionKey = "D#{Byte:d4}")]
[GlobalIndexKey("digitsULong", PartitionKey = "D#{ULong:d20}")]
[GlobalIndexKey("fixedInt", PartitionKey = "F#{Int:n12}")]
[GlobalIndexKey("fixedULong", PartitionKey = "F#{ULong:n20}")]
[GlobalIndexKey("decimal", PartitionKey = "M#{Money}")]
[GlobalIndexKey("fixedDecimal", PartitionKey = "M#{Money:n4.2}")]
[GlobalIndexKey("year", PartitionKey = "T#{At:yyyy}")]
[GlobalIndexKey("month", PartitionKey = "T#{At:yyyy-MM}")]
[GlobalIndexKey("day", PartitionKey = "T#{At:yyyy-MM-dd}")]
[GlobalIndexKey("hour", PartitionKey = "T#{At:yyyy-MM-ddTHH}")]
[GlobalIndexKey("minute", PartitionKey = "T#{At:yyyy-MM-ddTHH:mm}")]
[GlobalIndexKey("second", PartitionKey = "T#{At:s}")]
[GlobalIndexKey("milli", PartitionKey = "T#{At:ms}")]
[GlobalIndexKey("micro", PartitionKey = "T#{At:us}")]
[GlobalIndexKey("ticks", PartitionKey = "T#{At:ticks}")]
[GlobalIndexKey("dateTime", PartitionKey = "T#{Utc}")]
[GlobalIndexKey("dateTimeTicks", PartitionKey = "T#{Utc:ticks}")]
[GlobalIndexKey("date", PartitionKey = "T#{Date}")]
[GlobalIndexKey("custom", PartitionKey = "C#{Address}")]
public partial record KeyFormats
{
    public required string Text { get; init; }

    public Guid Id { get; init; }

    public Plan Plan { get; init; }

    public Tier Tier { get; init; }

    public sbyte SByte { get; init; }

    public byte Byte { get; init; }

    public short Short { get; init; }

    public ushort UShort { get; init; }

    public int Int { get; init; }

    public uint UInt { get; init; }

    public long Long { get; init; }

    public ulong ULong { get; init; }

    public decimal Money { get; init; }

    public DateTimeOffset At { get; init; }

    public DateTime Utc { get; init; }

    public DateOnly Date { get; init; }

    [KeyOnly]
    public required IPAddress Address { get; init; }
}

/// <summary>A key of literals only, which is legal: one item per partition, one partition per table.</summary>
[DynamoDbEntity]
[Discriminator("SETTINGS")]
[PartitionKey("CONFIG")]
[SortKey("GLOBAL")]
public partial class SiteSettings
{
    public string Theme { get; set; } = string.Empty;
}

/// <summary>A literal sort key under a composite partition.</summary>
[DynamoDbEntity]
[Discriminator("PROFILE")]
[PartitionKey("USER#{Id}")]
[SortKey("PROFILE")]
public partial class UserProfile
{
    public string Id { get; set; } = "none";
}

/// <summary>Plain keys: the attributes of the properties are the keys, and the sort key is a number.</summary>
[DynamoDbEntity]
[Discriminator("DEVICE")]
public partial class Device
{
    [PartitionKey]
    public string Serial { get; set; } = "none";

    [SortKey]
    public int Slot { get; set; }

    public Plan Plan { get; set; }
}

/// <summary>A plain enum key, whose converter only the file can name.</summary>
[DynamoDbEntity]
[Discriminator("QUOTA")]
public partial class Quota
{
    [PartitionKey]
    public Plan Plan { get; set; }
}

/// <summary>One property in both keys is one parameter of <c>Keys.Of</c>, whichever key is plain.</summary>
[DynamoDbEntity]
[Discriminator("ACCOUNT")]
[SortKey("ACCOUNT#{Id}#V{Version:d3}")]
public partial class Account
{
    [PartitionKey]
    public string Id { get; set; } = "none";

    public int Version { get; set; }
}

/// <summary>The same property in two templates with different formats.</summary>
[DynamoDbEntity]
[Discriminator("LEDGER")]
[PartitionKey("LEDGER#{At:yyyy-MM}")]
[SortKey("AT#{At:ticks}#{Id}")]
public partial class Ledger
{
    public DateTimeOffset At { get; set; }

    public Guid Id { get; set; }
}

/// <summary>An inverted index: its keys are the attributes of the table's, with the same templates, and they still have builders.</summary>
[DynamoDbEntity]
[Discriminator("EDGE")]
[PartitionKey("FROM#{Source}")]
[SortKey("TO#{Target}")]
[GlobalIndexKey("Inverted", PartitionKey = "TO#{Target}", PartitionKeyAttributeName = "sk", SortKey = "FROM#{Source}", SortKeyAttributeName = "pk")]
public partial class Edge
{
    public string Source { get; set; } = "none";

    public string Target { get; set; } = "none";
}

/// <summary>A literal after the last placeholder, of which a prefix keeps the delimiter.</summary>
[DynamoDbEntity]
[Discriminator("SUFFIXED")]
[PartitionKey("P")]
[SortKey("A-{Name}#B")]
public partial class Suffixed
{
    public string Name { get; set; } = "none";
}

/// <summary>Fixed before variable with nothing in between, which parses because the width is known.</summary>
[DynamoDbEntity]
[Discriminator("ADJACENT")]
[PartitionKey("P")]
[SortKey("{Code:d3}{Name}")]
public partial class Adjacent
{
    public int Code { get; set; }

    public string Name { get; set; } = "none";
}

/// <summary>A template with everything that needs escaping, in code and in documentation.</summary>
[DynamoDbEntity]
[Discriminator("AWKWARD")]
[PartitionKey("Q\"\\<&{{{Id}}}")]
[SortKey("L{{I}}T")]
public partial class Awkward
{
    public string Id { get; set; } = "none";
}

/// <summary>Names that are keywords, as properties and therefore as parameters and members of the parts.</summary>
[DynamoDbEntity]
[Discriminator("KEYWORDS")]
[PartitionKey("C#{class}")]
[SortKey("E#{event}")]
public partial class Keywords
{
    public string @class { get; set; } = "none";

    public string @event { get; set; } = "none";
}

/// <summary>A computed property feeds a key of the table.</summary>
[DynamoDbEntity]
[Discriminator("ARTICLE")]
[PartitionKey("SLUG#{Slug}")]
public partial class Article
{
    public string Title { get; set; } = string.Empty;

    public string Slug => Title.Replace(' ', '-');
}

/// <summary>A computed property feeds a key of an index, and says what it is derived from.</summary>
[DynamoDbEntity]
[Discriminator("LETTER")]
[PartitionKey("LETTER#{Id}")]
[GlobalIndexKey("ByDay", PartitionKey = "DAY#{Day}", SortKey = "{Received:ticks}")]
public partial class Letter
{
    public string Id { get; set; } = "none";

    [DynamoDbAttribute("at")]
    public DateTimeOffset Received { get; set; }

    [ComputedFrom(nameof(Received))]
    public DateOnly Day => DateOnly.FromDateTime(Received.UtcDateTime);
}

[DynamoDbEntity]
[Discriminator("EVENT")]
[PartitionKey("EVENT#{Id}")]
public partial class BaseEvent
{
    public string Id { get; set; } = "none";
}

/// <summary>Both generated classes hide the ones of the base type.</summary>
[DynamoDbEntity]
[Discriminator("AUDIT")]
[PartitionKey("AUDIT#{Id}")]
public partial class AuditEvent : BaseEvent
{
    public string Actor { get; set; } = string.Empty;
}

public partial class Outer
{
    public partial record struct Inner
    {
        /// <summary>An entity inside other types, as a struct.</summary>
        [DynamoDbEntity]
        [Discriminator("POINT")]
        [PartitionKey("POINT#{X:plain}")]
        public partial record struct Point(int X, int Y);
    }
}

/// <summary>Every kind of handle.</summary>
[DynamoDbEntity]
[Discriminator("HANDLES")]
[PartitionKey("HANDLES#{Id}")]
[SortKey("SLOT#{Slot:d3}")]
public partial record Handles
{
    public required string Id { get; init; }

    public int Count { get; init; }

    public long? Total { get; init; }

    public string? Nickname { get; init; }

    public Plan Plan { get; init; }

    public Plan? PreviousPlan { get; init; }

    public IReadOnlyList<Plan> History { get; init; } = Array.Empty<Plan>();

    public HashSet<Plan> Plans { get; init; } = new();

    public Features Features { get; init; }

    public HashSet<string> Tags { get; init; } = new();

    public IReadOnlySet<int> Scores { get; init; } = new HashSet<int>();

    public PostalAddress? Home { get; init; }

    [DynamoDbAttribute("renamed")]
    public string Original { get; init; } = string.Empty;

    [DynamoDbAttribute("dotted.name")]
    public string Dotted { get; init; } = string.Empty;

    [DynamoDbAttribute("list[0]")]
    public string Bracketed { get; init; } = string.Empty;

    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }

    [DynamoDbAttribute("line\nbreak\"")]
    public string Awkward { get; init; } = string.Empty;

    [DynamoDbAttribute("hidden")]
    internal string Hidden { get; init; } = string.Empty;

    [DynamoDbConverter(typeof(SecretConverter))]
    public string Secret { get; init; } = string.Empty;

    [KeyOnly]
    public int Slot { get; init; }
}

/// <summary>A converter that a public handle cannot name.</summary>
internal readonly struct SecretConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => StringConverter.Write(writer, value);

    public static string Read(ref DynamoDbReader reader, AttributeType type) => StringConverter.Read(ref reader, type);
}

/// <summary>An internal entity may use an internal enum in its handles and its keys.</summary>
[DynamoDbEntity]
[Discriminator("INTERNAL")]
[PartitionKey("I#{Kind}")]
internal sealed partial class InternalEntity
{
    public InternalKind Kind { get; set; }

    public HashSet<InternalKind> Kinds { get; set; } = new();
}

internal enum InternalKind
{
    One,
    Two,
}

/// <summary>An enum that only a key holds, so no handle needs its converter.</summary>
[DynamoDbEntity]
[Discriminator("KEYONLY")]
[PartitionKey("PLAN#{Plan}")]
public partial record KeyOnlyPlan
{
    [KeyOnly]
    public Plan Plan { get; init; }
}

/// <summary>The composite templates of <c>key_vectors.json</c>, so that the Python oracle checks generated code.</summary>
[DynamoDbEntity]
[Discriminator("VECTORS")]
[PartitionKey("USER#{Id}")]
[GlobalIndexKey("V02", PartitionKey = "PROFILE")]
[GlobalIndexKey("V03", PartitionKey = "{Id}")]
[GlobalIndexKey("V04", PartitionKey = "TENANT#{Tenant}#USER#{Id}")]
[GlobalIndexKey("V05", PartitionKey = "COUNTRY#{Country}#CANTON#{Canton}")]
[GlobalIndexKey("V06", PartitionKey = "ORDER#{CreatedAt}#{OrderId}")]
[GlobalIndexKey("V07", PartitionKey = "{Total:n10.2}")]
[GlobalIndexKey("V08", PartitionKey = "TOTAL#{Total:n10.2}")]
[GlobalIndexKey("V09", PartitionKey = "A-{X}#B")]
[GlobalIndexKey("V10", PartitionKey = "v{X}")]
[GlobalIndexKey("V11", PartitionKey = "N#{Number}")]
[GlobalIndexKey("V12", PartitionKey = "N-{Number}")]
[GlobalIndexKey("V13", PartitionKey = "SEQ#{Sequence:d6}#{Name}")]
[GlobalIndexKey("V14", PartitionKey = "SEQ#{Sequence:d2}#{Name}")]
[GlobalIndexKey("V15", PartitionKey = "{A}#{B}#{C}")]
[GlobalIndexKey("V16", PartitionKey = "{A}#{B}")]
[GlobalIndexKey("V17", PartitionKey = "L#{City}#{Name}")]
[GlobalIndexKey("V18", PartitionKey = "ORDER#{CreatedAt:ms}#{OrderId}")]
public partial record VectorKeys
{
    public required string Id { get; init; }

    public required string Tenant { get; init; }

    public required string Country { get; init; }

    public required string Canton { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Total { get; init; }

    public required string X { get; init; }

    public required long Number { get; init; }

    public required long Sequence { get; init; }

    public required string Name { get; init; }

    public required string A { get; init; }

    public required string B { get; init; }

    public required string C { get; init; }

    public required string City { get; init; }
}

/// <summary>Stored inputs of every kind that a read can or cannot take from a key: required and optional, in a table key and in a key of an index, lossless and lossy, nullable.</summary>
[DynamoDbEntity]
[Discriminator("LOGLINE")]
[PartitionKey("LOG#{Stream}")]
[SortKey("AT#{Day}#{At:ms}#{Sequence:d4}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "TAG#{Tag}", Sparse = true)]
[GlobalIndexKey("ByWhen", PartitionKey = "W#{When}", SortKey = "{When:ms}")]
public partial record LogLine
{
    public required string Stream { get; init; }

    public required DateOnly Day { get; init; }

    public required DateTimeOffset At { get; init; }

    public int Sequence { get; init; } = 7;

    public string? Owner { get; init; }

    public string Tag { get; init; } = "none";

    public DateTime When { get; init; }

    public string? Text { get; init; }
}

/// <summary>Inputs that are parameters of the constructor, one with a default.</summary>
[DynamoDbEntity]
[Discriminator("GRID")]
[PartitionKey("GRID#{Name}")]
[SortKey("CELL#{Column}#{Row:d3}")]
public partial record GridCell(string Name, string Column, int Row = 5);

/// <summary>An input beside a <c>[KeyOnly]</c> one of the same key.</summary>
[DynamoDbEntity]
[Discriminator("HALFKEPT")]
[PartitionKey("KEPT#{Name}#{Id}")]
public partial record HalfKept
{
    public required string Name { get; init; }

    [KeyOnly]
    public required string Id { get; init; }
}

/// <summary>A key of literals in a sparse index, which an update writes and removes, and one in an index that is not sparse, which an update writes with the key beside it.</summary>
[DynamoDbEntity]
[Discriminator("POST")]
[PartitionKey("POST#{Id}")]
[GlobalIndexKey("Featured", PartitionKey = "FEATURED", SortKey = "ON#{FeaturedOn}", Sparse = true)]
[GlobalIndexKey("Everything", PartitionKey = "ALL", SortKey = "{Title}")]
public partial record FeaturedPost
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public DateOnly? FeaturedOn { get; init; }
}

/// <summary>Indexes that hold every item, because all their keys are literals, with and without a sort key, beside one that has an input.</summary>
[DynamoDbEntity]
[Discriminator("NOTICE")]
[PartitionKey("NOTICE#{Id}")]
[GlobalIndexKey("Board", PartitionKey = "NOTICES", SortKey = "ANY")]
[GlobalIndexKey("Everyone", PartitionKey = "EVERYONE")]
[GlobalIndexKey("ByText", PartitionKey = "TEXT", SortKey = "{Text}")]
public partial record Notice
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public string? Note { get; init; }
}

/// <summary>Plain keys that are not strings: a number and a sort key, each with the converter of its property.</summary>
[DynamoDbEntity]
[Discriminator("NUMBERED")]
public partial record NumberKeyed
{
    [PartitionKey]
    public required int Id { get; init; }

    [SortKey]
    public required long Sequence { get; init; }
}

/// <summary>A plain key of binary, and a table key without a builder of a string type, whose <c>Of</c> goes through the converter of the property.</summary>
[DynamoDbEntity]
[Discriminator("BINARY")]
public partial record BinaryKeyed
{
    [PartitionKey]
    public required byte[] Id { get; init; }

    [SortKey]
    public required DateTimeOffset At { get; init; }
}

/// <summary>An index of bare placeholders over the attributes of its properties, nullable because it is sparse: the shape that shares its attribute with a property.</summary>
[DynamoDbEntity]
[Discriminator(OptOut = true)]
[PartitionKey("THING#{Id}")]
[GlobalIndexKey("ByOwner", PartitionKey = "{Owner}", SortKey = "{Rank}", Sparse = true, PartitionKeyAttributeName = "owner", SortKeyAttributeName = "rank")]
public partial record OwnedThing
{
    public required string Id { get; init; }

    [DynamoDbAttribute("owner")]
    public string? Owner { get; init; }

    [DynamoDbAttribute("rank")]
    public int? Rank { get; init; }
}

/// <summary>An index that wants more of a timestamp than the table key holds, so that the generated key has to give the value to an update.</summary>
[DynamoDbEntity]
[Discriminator("HELDSLOT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("DAY#{Start:yyyy-MM-dd}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}#{Rank:d3}#{Room}#AT#{Start:ticks}")]
public partial record HeldSlot
{
    public required string Room { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required string Owner { get; init; }

    public required int Rank { get; init; }
}

/// <summary>
/// What an update can store beside what it cannot, with a key that has to stay at hand: a timestamp the key cuts off and a converter writes as a number, a plain key that a template of the table repeats and an index wants,
/// which is its own attribute, and a property only a key holds.
/// </summary>
[DynamoDbEntity]
[Discriminator("STAMP")]
[SortKey("AT#{At:yyyy-MM-dd}#{Seq:d4}#{Room}")]
[GlobalIndexKey("ByRoom", PartitionKey = "ROOM#{Room}", SortKey = "{Code}")]
public partial record Stamp
{
    [PartitionKey]
    public required string Room { get; init; }

    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset At { get; init; }

    [KeyOnly]
    public required int Seq { get; init; }

    public required string Code { get; init; }
}

/// <summary>A timestamp in both keys of the table, one of which holds all of it, so no generated key has to stay.</summary>
[DynamoDbEntity]
[Discriminator("HOP")]
[PartitionKey("DAY#{At:yyyy-MM-dd}")]
[SortKey("{At:ticks}#{Id}")]
public partial record Hop
{
    public required DateTimeOffset At { get; init; }

    public required string Id { get; init; }
}

/// <summary>An index that repeats a timestamp in the format of the table key, which the key gives back as far as the index wants it.</summary>
[DynamoDbEntity]
[Discriminator("SAMESLOT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("AT#{Start:yyyy-MM-ddTHH:mm}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "{Start:yyyy-MM-ddTHH:mm}")]
public partial record SameSlot
{
    public required string Room { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required string Owner { get; init; }
}

/// <summary>Sort keys whose bytes do not follow their values, so that they have <c>Eq</c> and prefixes and no range: plain digits, a decimal, an enum by name, a custom type.</summary>
[DynamoDbEntity]
[Discriminator("UNRANGED")]
[PartitionKey("{Id}")]
[SortKey("COUNT#{Count:plain}")]
[GlobalIndexKey("ByMoney", PartitionKey = "M", SortKey = "MONEY#{Money:plain}")]
[GlobalIndexKey("ByPlan", PartitionKey = "P", SortKey = "PLAN#{Plan}")]
[GlobalIndexKey("ByAddress", PartitionKey = "A", SortKey = "ADDRESS#{Address}")]
public partial record Unranged
{
    public required string Id { get; init; }

    public int Count { get; init; }

    public decimal Money { get; init; }

    public Plan Plan { get; init; }

    [KeyOnly]
    public required IPAddress Address { get; init; }
}

/// <summary>A string before a space, which nothing sorts below, and placeholders named like the bounds of a range.</summary>
[DynamoDbEntity]
[Discriminator("RANGED")]
[PartitionKey("{Id}")]
[SortKey("{Name} {Seat:d2}")]
[GlobalIndexKey("ByBounds", PartitionKey = "B", SortKey = "{From:d2}#{To:d2}#{Seat:d2}")]
public partial record Ranged
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int Seat { get; init; }

    public int From { get; init; }

    public int To { get; init; }
}

/// <summary>Values that hold the delimiter of their template, at the end of each kind of key. What stands before them stays strict.</summary>
[DynamoDbEntity]
[Discriminator("RAWKEYED")]
[PartitionKey("APPOINTMENT#{Key:raw}", AttributeName = "Id")]
[SortKey("{Site}#{Day}#{Ref:raw}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "REF#{Ref:raw}")]
[GlobalIndexKey("ByNote", PartitionKey = "NOTE#{Note:raw}")]
[LocalIndexKey("BySeat", "{Seat:d3}#{Ref:raw}")]
public partial record RawKeyed
{
    public required string Key { get; init; }

    public required string Site { get; init; }

    public required DateOnly Day { get; init; }

    public required string Ref { get; init; }

    public required string Owner { get; init; }

    public required int Seat { get; init; }

    public required string Note { get; init; }
}

/// <summary>One value, raw in the table key and strict in an index, because a format belongs to its template.</summary>
[DynamoDbEntity]
[Discriminator("RAWTWIN")]
[PartitionKey("A#{Key:raw}")]
#pragma warning disable DYNA120 // What the build warns of is what this type is here to show at run time.
[GlobalIndexKey("Strict", PartitionKey = "A#{Key}")]
#pragma warning restore DYNA120
public partial record RawTwin
{
    public required string Key { get; init; }
}

/// <summary>A raw value that only the key holds.</summary>
[DynamoDbEntity]
[Discriminator("RAWONLY")]
[PartitionKey("T#{Tenant}")]
[SortKey("P#{Path:raw}")]
public partial record RawKeyOnly
{
    public required string Tenant { get; init; }

    [KeyOnly]
    public required string Path { get; init; }
}

/// <summary>A plain sort key that is stored in the order of its values, and plain sort keys of an index of each other kind.</summary>
[DynamoDbEntity]
[Discriminator("PLAINRANGES")]
[GlobalIndexKey("ByDigest", PartitionKey = "D", SortKey = "{Digest}", SortKeyAttributeName = "Digest")]
[GlobalIndexKey("ByWait", PartitionKey = "W", SortKey = "{Wait}", SortKeyAttributeName = "Wait")]
[GlobalIndexKey("ByPlan", PartitionKey = "P", SortKey = "{Plan}", SortKeyAttributeName = "Plan")]
[GlobalIndexKey("ByNote", PartitionKey = "N", SortKey = "{Note}", SortKeyAttributeName = "Note")]
public partial record PlainRanges
{
    [PartitionKey]
    public required string Id { get; init; }

    [SortKey]
    public required Guid Ref { get; init; }

    public required byte[] Digest { get; init; }

    public required TimeSpan Wait { get; init; }

    public required Plan Plan { get; init; }

    [DynamoDbConverter(typeof(SecretConverter))]
    public required string Note { get; init; }
}

/// <summary>A partition key of an index in the attribute of a property, which has no builder, and a local index over a plain key of the table.</summary>
[DynamoDbEntity]
[Discriminator("PLAINKEYS")]
[GlobalIndexKey("ByCode", PartitionKey = "{Code}", PartitionKeyAttributeName = "Code")]
[LocalIndexKey("ByTag", "TAG#{Tag}")]
public partial record PlainKeys
{
    [PartitionKey]
    public required string Id { get; init; }

    [SortKey]
    public required string Sort { get; init; }

    public required string Code { get; init; }

    public required string Tag { get; init; }
}
