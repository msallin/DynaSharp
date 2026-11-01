using System.Net;
using DynaSharp;
using DynaSharp.AotSmoke;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

/// <summary>An entity outside every namespace.</summary>
[DynamoDbEntity]
[Discriminator("GLOBAL")]
public partial class GlobalThing
{
    [PartitionKey]
    public string Id { get; set; } = string.Empty;

    public Color Color { get; set; }
}

#pragma warning disable SA1403 // One file holds the shapes, and this one is about namespaces.
namespace DynaSharp.AotSmoke
{
    public enum Plan
    {
        Free,
        Pro,
        Enterprise,
    }

    public enum Grade : byte
    {
        Low = 1,
        High = 200,
    }

    public enum Balance
    {
        Minus = -1,
        Plus = 1,
    }

    [Flags]
    public enum Rights : byte
    {
        None = 0,
        Read = 1,
        Write = 2,
        All = Read | Write,
    }

    [Flags]
    public enum Wide : long
    {
        Low = 1,
        High = 1L << 62,
    }

    public enum Color
    {
        Red,
        Crimson = Red,
        Green,
    }

    public enum Nothing
    {
    }

    [DynamoDbDocument]
    public partial record Street(string Name, int Number);

    [DynamoDbDocument]
    public partial record PostalAddress
    {
        public required string City { get; init; }

        public Street? Street { get; init; }
    }

    /// <summary>A document that inherits one.</summary>
    [DynamoDbDocument]
    public partial record ForeignAddress : PostalAddress
    {
        public string? Country { get; init; }
    }

    /// <summary>Composite keys, a global index, a sparse one and a local one, with a nested document, set and handles of each kind.</summary>
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

        public HashSet<string> Tags { get; init; } = [];

        public HashSet<Plan> Plans { get; init; } = [];

        [DynamoDbConverter(typeof(EpochSecondsConverter))]
        [DynamoDbAttribute("ttl")]
        public DateTimeOffset? ExpiresAt { get; init; }

        [Version]
        public long Revision { get; init; }
    }

    /// <summary>Several strings in one template, for delimiters, prefixes and partial prefixes.</summary>
    [DynamoDbEntity]
    [Discriminator("MEMBER")]
    [PartitionKey("TENANT#{Tenant}#USER#{Id}")]
    [SortKey("REGION#{Region}#CITY#{City}")]
    public partial record KeyedMember
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
    [GlobalIndexKey("byteEnum", PartitionKey = "E#{Grade:d3}")]
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

        public Grade Grade { get; init; }

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

    /// <summary>Keys of literals only.</summary>
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
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>Plain keys, of which the sort key is a number.</summary>
    [DynamoDbEntity]
    [Discriminator("DEVICE")]
    public partial class Device
    {
        [PartitionKey]
        public string Serial { get; set; } = string.Empty;

        [SortKey]
        public int Slot { get; set; }

        public Plan Plan { get; set; }
    }

    /// <summary>A plain enum key.</summary>
    [DynamoDbEntity]
    [Discriminator("QUOTA")]
    public partial class Quota
    {
        [PartitionKey]
        public Plan Plan { get; set; }
    }

    /// <summary>One property in both keys, a plain one and a template.</summary>
    [DynamoDbEntity]
    [Discriminator("ACCOUNT")]
    [SortKey("ACCOUNT#{Id}#V{Version:d3}")]
    public partial class KeyedAccount
    {
        [PartitionKey]
        public string Id { get; set; } = string.Empty;

        public int Version { get; set; }
    }

    /// <summary>One property in two templates with different formats.</summary>
    [DynamoDbEntity]
    [Discriminator("LEDGER")]
    [PartitionKey("LEDGER#{At:yyyy-MM}")]
    [SortKey("AT#{At:ticks}#{Id}")]
    public partial class Ledger
    {
        public DateTimeOffset At { get; set; }

        public Guid Id { get; set; }
    }

    /// <summary>An inverted index over the attributes of the table's keys.</summary>
    [DynamoDbEntity]
    [Discriminator("EDGE")]
    [PartitionKey("FROM#{Source}")]
    [SortKey("TO#{Target}")]
    [GlobalIndexKey("Inverted", PartitionKey = "TO#{Target}", PartitionKeyAttributeName = "sk", SortKey = "FROM#{Source}", SortKeyAttributeName = "pk")]
    public partial class Edge
    {
        public string Source { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;
    }

    /// <summary>A literal after the last placeholder.</summary>
    [DynamoDbEntity]
    [Discriminator("SUFFIXED")]
    [PartitionKey("P")]
    [SortKey("A-{Name}#B")]
    public partial class Suffixed
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Fixed before variable with nothing in between.</summary>
    [DynamoDbEntity]
    [Discriminator("ADJACENT")]
    [PartitionKey("P")]
    [SortKey("{Code:d3}{Name}")]
    public partial class Adjacent
    {
        public int Code { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A computed property that feeds a key.</summary>
    [DynamoDbEntity]
    [Discriminator("ARTICLE")]
    [PartitionKey("SLUG#{Slug}")]
    public partial class Article
    {
        public string Title { get; set; } = string.Empty;

        public string Slug => Title.Replace(' ', '-');
    }

    [DynamoDbEntity]
    [Discriminator("EVENT")]
    [PartitionKey("EVENT#{Id}")]
    public partial class BaseEvent
    {
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>An entity that inherits an entity, whose generated classes hide those of the base.</summary>
    [DynamoDbEntity]
    [Discriminator("AUDIT")]
    [PartitionKey("AUDIT#{Id}")]
    public partial class AuditEvent : BaseEvent
    {
        public string Actor { get; set; } = string.Empty;
    }

    /// <summary>Everything lives in the keys but one attribute, in a positional record.</summary>
    [DynamoDbEntity]
    [Discriminator("RESTORED")]
    [PartitionKey("USER#{User}")]
    [SortKey("{At:ticks}#{Id}")]
    [GlobalIndexKey("GSI1", PartitionKey = "STATE#{State}", SortKey = "{Id}")]
    public partial record Restored(
        [property: KeyOnly] string User,
        [property: KeyOnly] Plan State,
        int Size = 1)
    {
        [KeyOnly]
        public required DateTime At { get; init; }

        [KeyOnly]
        public Guid Id { get; init; }

        public string? Note { get; init; }
    }

    /// <summary>A class with setters of every access, a stored computed property and what stays out.</summary>
    [DynamoDbEntity]
    [Discriminator("MUTABLE")]
    public partial class Mutable
    {
        [PartitionKey]
        public required string Id { get; init; }

        [SortKey(AttributeName = "sk")]
        public int Version { get; private set; }

        public string Plan { get; set; } = "free";

        public string? Note { get; protected set; }

        public IList<string> Aliases { get; set; } = new List<string>();

        public DateOnly Opened { get; init; }

        [DynamoDbAttribute("len")]
        public int Length => Plan.Length;

        [DynamoDbIgnore]
        public string Scratch { get; set; } = string.Empty;

        public void Upgrade(int version) => Version = version;
    }

    /// <summary>A struct entity.</summary>
    [DynamoDbEntity]
    [Discriminator("POINT")]
    public partial struct StructPoint
    {
        [PartitionKey]
        public string Id { get; set; }

        public double X { get; set; }

        public double? Y { get; init; }

        public Guid Tag { get; set; }
    }

    /// <summary>A read-only struct bound through its constructor.</summary>
    [DynamoDbDocument]
    public readonly partial struct Size
    {
        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }
    }

    [DynamoDbDocument]
    public partial record struct Money(decimal Amount, string Currency = "CHF");

    [DynamoDbDocument]
    public partial record struct Tagged(int Id)
    {
        public string? Label { get; set; }
    }

    [DynamoDbDocument]
    public readonly partial record struct Flat
    {
        public required int Floor { get; init; }

        public Money? Rent { get; init; }

        public Size? Area { get; init; }

        public Tagged Tag { get; init; }
    }

    /// <summary>A positional record with a default for every kind of value.</summary>
    [DynamoDbEntity]
    [Discriminator("POSITIONAL")]
    public partial record Positional(
        [property: PartitionKey] string Id,
        string Name,
        int Age = 18,
        decimal Limit = 1.5m,
        string Motto = "hello \"world\"",
        Balance Balance = Balance.Minus,
        string? Nick = null,
        HashSet<string>? Badges = null)
    {
        public HashSet<int> Scores { get; init; } = [];

        public Street? Home { get; init; }
    }

    public partial class Outer
    {
        public partial record struct Middle
        {
            public enum Kind
            {
                One,
                Two,
            }

            public partial interface IInner
            {
                [DynamoDbDocument]
                public partial record Deep(Kind Kind);
            }

            /// <summary>An entity inside a class, a record struct and with a document inside an interface.</summary>
            [DynamoDbEntity]
            [Discriminator("INNER")]
            public partial class Inner
            {
                [PartitionKey]
                public string Id { get; set; } = string.Empty;

                public Kind Kind { get; set; }

                public IInner.Deep? Deep { get; set; }
            }

            /// <summary>A record struct entity inside other types, with a template key.</summary>
            [DynamoDbEntity]
            [Discriminator("NESTEDPOINT")]
            [PartitionKey("POINT#{X:plain}")]
            public partial record struct Point(int X, int Y);
        }
    }

    public enum @enum
    {
        @default,
        @class,
        plain,
    }

    [DynamoDbDocument]
    public partial record @object(int @int);

    /// <summary>Names that are keywords, and text that needs escaping in code.</summary>
    [DynamoDbEntity]
    [Discriminator("say \"hi\"\\", AttributeName = "the \"type\"")]
    [PartitionKey("Q\"\\<&{{{string}}}")]
    [SortKey("E#{event}")]
    public partial class @class
    {
        public string @string { get; set; } = string.Empty;

        public string @event { get; set; } = string.Empty;

        public @enum @base { get; set; }

        public @enum? @this { get; set; }

        [DynamoDbAttribute("größe")]
        public int @double { get; set; }

        public @object? @fixed { get; set; }

        public string fields { get; set; } = string.Empty;

        public string writer { get; set; } = string.Empty;

        public string reader { get; set; } = string.Empty;

        public string entity { get; set; } = string.Empty;

        public string attribute { get; set; } = string.Empty;

        public string name { get; set; } = string.Empty;

        public string exception { get; set; } = string.Empty;
    }

    /// <summary>Enums stored by name, by number and with aliases, alone, nullable and in collections.</summary>
    [DynamoDbDocument]
    public partial class Grant
    {
        public Rights Rights { get; set; }

        public Wide? Wide { get; set; }

        public HashSet<Rights> Each { get; set; } = [];

        public Color Color { get; set; }

        public HashSet<Color> Colors { get; set; } = [];

        public IReadOnlySet<Color>? Others { get; set; }

        public List<Color?> Sparse { get; set; } = [];

        public Nothing? None { get; set; }

        public Grade Grade { get; set; }

        public Balance Balance { get; set; }
    }

    /// <summary>A key of an entity that an internal enum feeds.</summary>
    [DynamoDbEntity]
    [Discriminator("INTERNAL")]
    [PartitionKey("I#{Kind}")]
    internal sealed partial class InternalEntity
    {
        public InternalKind Kind { get; set; }

        public HashSet<InternalKind> Kinds { get; set; } = [];
    }

    internal enum InternalKind
    {
        One,
        Two,
    }

    [DynamoDbDocument]
    public partial class Leaf
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A converter given on a property.</summary>
    public readonly struct PairConverter : IDynamoDbConverter<(int Left, int Right)>
    {
        public static void Write(DynamoDbWriter writer, (int Left, int Right) value) =>
            writer.WriteString(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value.Left}:{value.Right}"));

        public static (int Left, int Right) Read(ref DynamoDbReader reader, AttributeType type)
        {
            var text = reader.GetString();
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            return (int.Parse(text.AsSpan(0, colon), provider: System.Globalization.CultureInfo.InvariantCulture), int.Parse(text.AsSpan(colon + 1), provider: System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A type with its converter on the type, which applies everywhere it is used.</summary>
    /// <param name="Value">The text.</param>
    [DynamoDbConverter(typeof(CodeConverter))]
    public readonly record struct Code(string Value);

    public readonly struct CodeConverter : IDynamoDbConverter<Code>
    {
        public static void Write(DynamoDbWriter writer, Code value) => writer.WriteString(value.Value);

        public static Code Read(ref DynamoDbReader reader, AttributeType type) => new(reader.GetString());
    }

    /// <summary>A property of every type the converters take.</summary>
    [DynamoDbDocument]
    public partial class Everything
    {
        public string Text { get; set; } = string.Empty;

        public bool Flag { get; set; }

        public byte Tiny { get; set; }

        public long Big { get; set; }

        public float Single { get; set; }

        public double Double { get; set; }

        public Int128 Huge { get; set; }

        public DateTime When { get; set; }

        public DateOnly Day { get; set; }

        public TimeOnly Clock { get; set; }

        public TimeSpan Span { get; set; }

        public byte[] Bytes { get; set; } = [];

        public ReadOnlyMemory<byte> Memory { get; set; }

        public AttributeValue Raw { get; set; }

        public AttributeValue? MaybeRaw { get; set; }

        public int[] Numbers { get; set; } = [];

        public List<string> Strings { get; set; } = [];

        public IList<long> Longs { get; set; } = new List<long>();

        public IReadOnlyList<Leaf> Leaves { get; set; } = new List<Leaf>();

        public List<List<int>> Grid { get; set; } = [];

        public List<string?> Holes { get; set; } = [];

        public HashSet<string> StringSet { get; set; } = [];

        public HashSet<decimal> NumberSet { get; set; } = [];

        public HashSet<byte[]> BinarySet { get; set; } = [];

        public HashSet<Guid> GuidSet { get; set; } = [];

        public IReadOnlySet<int> ReadOnlySet { get; set; } = new HashSet<int>();

        [DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))]
        public string[] ArraySet { get; set; } = [];

        public Dictionary<string, int> Map { get; set; } = [];

        public IReadOnlyDictionary<string, Leaf> Leaf { get; set; } = new Dictionary<string, Leaf>();

        public Leaf? Maybe { get; set; }

        [DynamoDbConverter(typeof(PairConverter))]
        public (int Left, int Right) Pair { get; set; }

        public Code Coded { get; set; }

        public Code? MaybeCoded { get; set; }

        public List<Code> Codes { get; set; } = [];
    }

    [DynamoDbDocument]
    public partial class Tree
    {
        public string Name { get; set; } = string.Empty;

        public List<Tree> Children { get; set; } = [];

        public Tree? Parent { get; set; }
    }

    [DynamoDbDocument]
    public partial class Empty
    {
    }

    /// <summary>An entity without a discriminator, for a table that holds one type written by something else.</summary>
    [DynamoDbEntity]
    [Discriminator(OptOut = true)]
    public partial class Foreign
    {
        [PartitionKey(AttributeName = "id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>A discriminator under another name, and a version under another name.</summary>
    [DynamoDbEntity]
    [Discriminator("OWN", AttributeName = "kind")]
    public partial class Own
    {
        [PartitionKey]
        public int Id { get; set; }

        [Version]
        [DynamoDbAttribute("rev")]
        public long Revision { get; set; }
    }

    public class Audited
    {
        public required string Author { get; init; }
    }

    /// <summary>A required member that the constructor sets.</summary>
    [DynamoDbDocument]
    public partial class BoundToTheConstructor
    {
        public BoundToTheConstructor(string name)
        {
            Name = name;
        }

        public required string Name { get; init; }
    }

    /// <summary>A required member in the base type.</summary>
    [DynamoDbDocument]
    public partial class InheritedRequired : Audited
    {
    }

    /// <summary>A required member that may be null.</summary>
    [DynamoDbDocument]
    public partial record NullableRequired
    {
        public required string? Maybe { get; init; }
    }
}
#pragma warning restore SA1403
