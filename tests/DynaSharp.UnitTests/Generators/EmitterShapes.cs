namespace DynaSharp.UnitTests.Generators;

/// <summary>One input per shape the emitters must handle. Each one is compiled and pinned by a snapshot, see <see cref="EntityEmitterTests"/>.</summary>
internal static class EmitterShapes
{
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using DynaSharp;
        using DynaSharp.Mapping;
        using DynaSharp.Serialization;


        """;

    public const string ClassEntity = Usings + """
        namespace Shapes.Class
        {
            [DynamoDbEntity]
            [Discriminator("ACCOUNT")]
            public partial class Account
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
                public string Scratch { get; set; } = "";

                public static int Created { get; set; }

                public void Upgrade(int version) => Version = version;
            }
        }
        """;

    public const string StructEntity = Usings + """
        namespace Shapes.Struct
        {
            [DynamoDbEntity]
            [Discriminator("POINT")]
            public partial struct Point
            {
                [PartitionKey]
                public string Id { get; set; }

                public double X { get; set; }

                public double? Y { get; init; }

                public Guid Tag { get; set; }
            }

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
        }
        """;

    public const string RecordStruct = Usings + """
        namespace Shapes.RecordStruct
        {
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
            }
        }
        """;

    public const string Positional = Usings + """
        namespace Shapes.Positional
        {
            public enum Tier { Free = -1, Paid = 1 }

            [DynamoDbDocument]
            public partial record Address(string Street, string City, string? Zip);

            [DynamoDbEntity]
            [Discriminator("MEMBER")]
            public partial record Member(
                [property: PartitionKey] string Id,
                string Name,
                int Age = 18,
                decimal Limit = 1.5m,
                string Motto = "hello \"world\"",
                Tier Tier = Tier.Free,
                string? Nick = null,
                HashSet<string>? Badges = null)
            {
                public HashSet<int> Scores { get; init; } = new();

                public Address? Home { get; init; }
            }

            [DynamoDbDocument]
            public partial record Tags(HashSet<string> Names, string[] Unused);
        }
        """;

    public const string Nested = Usings + """
        namespace Shapes.Nested
        {
            public partial class Outer
            {
                public partial record struct Middle
                {
                    public enum Kind { One, Two }

                    public partial interface IInner
                    {
                        [DynamoDbDocument]
                        public partial record Deep(Kind Kind);
                    }

                    [DynamoDbEntity]
                    [Discriminator("INNER")]
                    public partial class Inner
                    {
                        [PartitionKey]
                        public string Id { get; set; } = "";

                        public Kind Kind { get; set; }
                    }
                }
            }
        }
        """;

    public const string GlobalNamespace = Usings + """
        public enum GlobalColor { Red, Green }

        [DynamoDbEntity]
        [Discriminator("GLOBAL")]
        public partial class GlobalThing
        {
            [PartitionKey]
            public string Id { get; set; } = "";

            public GlobalColor Color { get; set; }
        }
        """;

    public const string Inheritance = Usings + """
        namespace Shapes.Inheritance
        {
            public abstract class Audited
            {
                public DateTimeOffset At { get; set; }

                public required string Author { get; init; }

                public string Hidden { get; private set; } = "kept";
            }

            [DynamoDbEntity]
            [Discriminator("BASE")]
            public partial class Base : Audited
            {
                [PartitionKey]
                public string Id { get; set; } = "";
            }

            [DynamoDbEntity]
            [Discriminator("DERIVED")]
            public partial class Derived : Base
            {
                public int Extra { get; set; }
            }
        }
        """;

    public const string Flags = Usings + """
        namespace Shapes.Flags
        {
            [Flags]
            public enum Rights : byte { None = 0, Read = 1, Write = 2, All = Read | Write }

            [Flags]
            public enum Wide : long { Low = 1, High = 1L << 62 }

            public enum Color { Red, Crimson = Red, Green, Blue }

            public enum Nothing { }

            [DynamoDbDocument]
            public partial class Grant
            {
                public Rights Rights { get; set; }

                public Wide? Wide { get; set; }

                public HashSet<Rights> Each { get; set; } = new();

                public Color Color { get; set; }

                public HashSet<Color> Colors { get; set; } = new();

                public IReadOnlySet<Color>? Others { get; set; }

                public List<Color?> Sparse { get; set; } = new();

                public Nothing? None { get; set; }
            }
        }
        """;

    public const string Keywords = Usings + """
        namespace Shapes.@namespace.@event
        {
            public enum @enum { @default, @class, plain }

            [DynamoDbDocument]
            public partial record @object(int @int);

            [DynamoDbEntity]
            [Discriminator("say \"hi\"\\", AttributeName = "the \"type\"")]
            public partial class @class
            {
                [PartitionKey(AttributeName = "the\tkey")]
                public string @string { get; set; } = "";

                public @enum @base { get; set; }

                public @enum? @this { get; set; }

                [DynamoDbAttribute("größe")]
                public int @double { get; set; }

                public @object? @fixed { get; set; }

                public string fields { get; set; } = "";

                public string writer { get; set; } = "";

                public string reader { get; set; } = "";

                public string entity { get; set; } = "";

                public string attribute { get; set; } = "";

                public string name { get; set; } = "";

                public string exception { get; set; } = "";
            }
        }
        """;

    public const string TwoEnums = Usings + """
        namespace Shapes.First
        {
            public enum Status { Open, Closed }
        }

        namespace Shapes.Second
        {
            public enum Status { Active, Retired }
        }

        namespace Shapes.Both
        {
            [DynamoDbDocument]
            public partial class Ticket
            {
                public Shapes.First.Status First { get; set; }

                public Shapes.Second.Status Second { get; set; }
            }

            [DynamoDbDocument]
            public partial class Other
            {
                public Shapes.First.Status Status { get; set; }
            }
        }
        """;

    public const string Collections = Usings + """
        namespace Shapes.Collections
        {
            [DynamoDbDocument]
            public partial class Leaf
            {
                public string Name { get; set; } = "";
            }

            public readonly struct PairConverter : IDynamoDbConverter<(int, int)>
            {
                public static void Write(DynamoDbWriter writer, (int, int) value) => writer.WriteString(value.Item1 + ":" + value.Item2);

                public static (int, int) Read(ref DynamoDbReader reader, AttributeType type) => (0, 0);
            }

            [DynamoDbConverter(typeof(CodeConverter))]
            public readonly record struct Code(string Value);

            public readonly struct CodeConverter : IDynamoDbConverter<Code>
            {
                public static void Write(DynamoDbWriter writer, Code value) => writer.WriteString(value.Value);

                public static Code Read(ref DynamoDbReader reader, AttributeType type) => new(reader.GetString());
            }

            [DynamoDbDocument]
            public partial class Everything
            {
                public string Text { get; set; } = "";
                public bool Flag { get; set; }
                public byte Tiny { get; set; }
                public long Big { get; set; }
                public float Single { get; set; }
                public double Double { get; set; }
                public Int128 Huge { get; set; }
                public DateTime When { get; set; }
                public TimeOnly Clock { get; set; }
                public TimeSpan Span { get; set; }
                public byte[] Bytes { get; set; } = Array.Empty<byte>();
                public ReadOnlyMemory<byte> Memory { get; set; }
                public AttributeValue Raw { get; set; }
                public AttributeValue? MaybeRaw { get; set; }
                public int[] Numbers { get; set; } = Array.Empty<int>();
                public List<string> Strings { get; set; } = new();
                public IList<long> Longs { get; set; } = new List<long>();
                public IReadOnlyList<Leaf> Leaves { get; set; } = new List<Leaf>();
                public List<List<int>> Grid { get; set; } = new();
                public List<string?> Holes { get; set; } = new();
                public HashSet<string> StringSet { get; set; } = new();
                public HashSet<decimal> NumberSet { get; set; } = new();
                public HashSet<byte[]> BinarySet { get; set; } = new();
                public HashSet<Guid> GuidSet { get; set; } = new();
                public IReadOnlySet<int> ReadOnlySet { get; set; } = new HashSet<int>();
                [DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))]
                public string[] ArraySet { get; set; } = Array.Empty<string>();
                public Dictionary<string, int> Map { get; set; } = new();
                public IReadOnlyDictionary<string, Leaf> Leaf { get; set; } = new Dictionary<string, Leaf>();
                public Leaf? Maybe { get; set; }
                [DynamoDbConverter(typeof(PairConverter))]
                public (int, int) Pair { get; set; }
                public Code Coded { get; set; }
                public Code? MaybeCoded { get; set; }
                public List<Code> Codes { get; set; } = new();
            }

            [DynamoDbDocument]
            public partial class Tree
            {
                public string Name { get; set; } = "";
                public List<Tree> Children { get; set; } = new();
                public Tree? Parent { get; set; }
            }

            [DynamoDbDocument]
            public partial class Empty
            {
            }
        }
        """;

    public const string Discriminators = Usings + """
        namespace Shapes.Discriminators
        {
            [DynamoDbEntity]
            [Discriminator(OptOut = true)]
            public partial class Foreign
            {
                [PartitionKey(AttributeName = "id")]
                public string Id { get; set; } = "";
            }

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
        }
        """;

    public const string Keys = Usings + """
        namespace Shapes.Keys
        {
            public enum Status { Open, Closed }

            [DynamoDbEntity]
            [Discriminator("FIXED")]
            [PartitionKey("TENANT#{Tenant}")]
            [SortKey("PROFILE")]
            public partial class Fixed
            {
                public string Tenant { get; set; } = "";

                public string @class { get; set; } = "";
            }

            [DynamoDbEntity]
            [Discriminator("MIXED")]
            [PartitionKey("USER#{User}")]
            [LocalIndexKey("LSI1", "{Status}#{Created}")]
            [GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Code}", SortKeyAttributeName = "sk")]
            [GlobalIndexKey("GSI2", PartitionKey = "NICK#{Nick}", SortKey = "{Parent:d4}", Sparse = true)]
            [GlobalIndexKey("GSI3", PartitionKey = "NICK#{Nick}", PartitionKeyAttributeName = "gsi2pk", Sparse = true)]
            public partial class Mixed
            {
                [KeyOnly]
                public string User { get; set; } = "";

                [SortKey]
                public string Code { get; set; } = "";

                public Status Status { get; set; }

                public DateOnly Created { get; set; }

                public string? Nick { get; set; }

                public int? Parent { get; set; }

                [DynamoDbAttribute("age")]
                public int Computed => Code.Length;
            }

            [DynamoDbEntity]
            [Discriminator("RESTORED")]
            [PartitionKey("USER#{User}")]
            [SortKey("{At:ticks}#{Id}")]
            [GlobalIndexKey("GSI1", PartitionKey = "STATE#{State}", SortKey = "{Id}")]
            public partial record Restored(
                [property: KeyOnly] string User,
                [property: KeyOnly] Status State,
                int Size = 1)
            {
                [KeyOnly]
                public required DateTime At { get; init; }

                [KeyOnly]
                public Guid Id { get; init; }

                public string? Note { get; init; }
            }
        }
        """;

    public const string Required = Usings + """
        namespace Shapes.Required
        {
            public class Audited
            {
                public required string Author { get; init; }

                public required int Revision;
            }

            [DynamoDbDocument]
            public partial class BoundToTheConstructor
            {
                public BoundToTheConstructor(string name)
                {
                    Name = name;
                }

                public required string Name { get; init; }
            }

            [DynamoDbDocument]
            public partial class Ignored
            {
                [DynamoDbIgnore]
                public required string Note { get; init; }
            }

            [DynamoDbDocument]
            public partial class Inherited : Audited
            {
            }

            [DynamoDbDocument]
            public partial record Nullable
            {
                public required string? Maybe { get; init; }
            }
        }
        """;

    public const string ExplicitConverters = Usings + """
        namespace Shapes.ExplicitConverters
        {
            public readonly record struct Cents(int Value);

            public readonly struct CentsConverter : IDynamoDbConverter<Cents>
            {
                static void IDynamoDbConverter<Cents>.Write(DynamoDbWriter writer, Cents value) => writer.WriteNumber(value.Value);

                static Cents IDynamoDbConverter<Cents>.Read(ref DynamoDbReader reader, AttributeType type)
                {
                    ConverterChecks.Expect(type, AttributeType.Number);
                    return new Cents(reader.GetNumber<int>());
                }
            }

            [DynamoDbConverter(typeof(TagConverter))]
            public readonly record struct Tag(string Text);

            public readonly struct TagConverter : IDynamoDbConverter<Tag>
            {
                static void IDynamoDbConverter<Tag>.Write(DynamoDbWriter writer, Tag value) => writer.WriteString(value.Text);

                static Tag IDynamoDbConverter<Tag>.Read(ref DynamoDbReader reader, AttributeType type)
                {
                    ConverterChecks.Expect(type, AttributeType.String);
                    return new Tag(reader.GetString());
                }
            }

            public readonly struct OnlyWriteIsExplicit : IDynamoDbConverter<Cents>
            {
                static void IDynamoDbConverter<Cents>.Write(DynamoDbWriter writer, Cents value) => writer.WriteNumber(value.Value);

                public static void Write(DynamoDbWriter writer, string text) => writer.WriteString(text);

                public static Cents Read(ref DynamoDbReader reader, AttributeType type) => new Cents(reader.GetNumber<int>());
            }

            [DynamoDbEntity]
            [Discriminator("PRICE")]
            public partial class Price
            {
                [PartitionKey]
                public Tag Id { get; set; }

                [SortKey]
                [DynamoDbConverter(typeof(CentsConverter))]
                public Cents Net { get; set; }

                [DynamoDbConverter(typeof(CentsConverter))]
                public Cents? Gross { get; set; }

                [DynamoDbConverter(typeof(OnlyWriteIsExplicit))]
                public Cents Fee { get; set; }

                public Tag? Label { get; set; }

                public List<Tag> Labels { get; set; } = new();
            }
        }
        """;

    public const string Versioned = Usings + """
        namespace Shapes.Versioned
        {
            [DynamoDbEntity]
            [Discriminator("NOTE")]
            [PartitionKey("NOTE#{Id}")]
            [GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "{Id}", Sparse = true)]
            public partial record Note
            {
                public required string Id { get; init; }

                public string? Owner { get; init; }

                [Version]
                public short Revision { get; init; }
            }
        }
        """;

    public const string OmitEmpty = Usings + """
        namespace Shapes.OmitEmpty
        {
            [DynamoDbEntity]
            [Discriminator("MEMBER")]
            [PartitionKey("MEMBER#{Id}")]
            [GlobalIndexKey("ByLabel", PartitionKey = "LABEL#{Label}", Sparse = true)]
            [OmitEmpty]
            public partial class Member
            {
                public string Id { get; set; } = string.Empty;

                public string? Nick { get; set; }

                public string? Label { get; set; }

                public string Motto { get; set; } = string.Empty;

                public Home? Home { get; set; }
            }

            [DynamoDbDocument]
            public partial class Home
            {
                [OmitEmpty]
                public string? City { get; set; }

                public string? Street { get; set; }
            }
        }
        """;

    /// <summary>Gets the shapes by name, which is also how the names of their snapshots start.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        ["SectionFour"] = TestSources.SectionFourOrder,
        ["ClassEntity"] = ClassEntity,
        ["StructEntity"] = StructEntity,
        ["RecordStruct"] = RecordStruct,
        ["Positional"] = Positional,
        ["Nested"] = Nested,
        ["GlobalNamespace"] = GlobalNamespace,
        ["Inheritance"] = Inheritance,
        ["Flags"] = Flags,
        ["Keywords"] = Keywords,
        ["TwoEnums"] = TwoEnums,
        ["Collections"] = Collections,
        ["Discriminators"] = Discriminators,
        ["Keys"] = Keys,
        ["Required"] = Required,
        ["ExplicitConverters"] = ExplicitConverters,
        ["Versioned"] = Versioned,
        ["OmitEmpty"] = OmitEmpty,
    };

    public static IEnumerable<string> Names() => All.Keys;
}
