using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA032, and DYNA120 and DYNA121 through <c>[ComputedFrom]</c>: what the getter of a computed input says that its attribute does not.</summary>
public sealed class ComputedGetterAnalyzerTests
{
    private const string Raw = "A#{Key:raw}";

    [Test]
    [Arguments("First + [|Last|]")]
    [Arguments("First + [|this.Last|]")]
    [Arguments("[|Last|] + First + Last")]
    [Arguments("string.Concat(First, [|Last|])")]
    [Arguments("new[] { 1 }.Select(_ => First + [|Last|]).Single()")]
    public async Task Analyze_GetterReadsAStoredPropertyThatIsNotNamed_ReportsDyna032AtTheFirstRead(string getter)
    {
        // Arrange (a full name in an index, which an update of the last name alone would leave as it was)
        var source = Person(getter);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA032 Warning: The getter of 'Full' reads 'Last', which [ComputedFrom] does not name, so an update of 'Last' leaves a key built from 'Full' behind. Name it in [ComputedFrom]."));
    }

    [Test]
    public async Task Analyze_GetterReadsTwoPropertiesThatAreNotNamed_ReportsEach()
    {
        // Arrange
        var source = Person("First + [|Last|] + [|Nick|]");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(
            source,
            "DYNA032 Warning: The getter of 'Full' reads 'Last', which [ComputedFrom] does not name, so an update of 'Last' leaves a key built from 'Full' behind. Name it in [ComputedFrom].",
            "DYNA032 Warning: The getter of 'Full' reads 'Nick', which [ComputedFrom] does not name, so an update of 'Nick' leaves a key built from 'Full' behind. Name it in [ComputedFrom]."));
    }

    [Test]
    [Arguments("First.ToUpperInvariant()")]
    [Arguments("First + Revision")]
    [Arguments("First + Code")]
    [Arguments("First + Initial")]
    [Arguments("First + Initials")]
    [Arguments("First + Hidden")]
    [Arguments("First + Prefix")]
    [Arguments("First + Format()")]
    [Arguments("First + nameof(Last)")]
    [Arguments("new Person { Id = First, Code = First, First = First, Last = First }.Last")]
    public async Task Analyze_GetterReadsNothingThatComputedFromCouldName_ReportsNothing(string getter)
    {
        // Arrange (the version, a [KeyOnly] property, a computed or ignored one cannot be named, a static one is no input, a call is not followed, and another instance is not this one)
        var source = Person(getter);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_GetterWithoutComputedFrom_ReportsNothing()
    {
        // Arrange (a computed key of the table needs no [ComputedFrom], and without one nothing says what the getter reads)
        var source = Source("""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Full}")]
            public partial class E
            {
                public required string First { get; init; }

                public required string Last { get; init; }

                public string Full => First + Last;
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("public partial class E", "public string Full => First + Last;")]
    [Arguments("[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{First}\")] public partial class E", "public static string Full => \"x\";")]
    [Arguments("[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{First}\")] public partial class E", "public string Full => First + Last;")]
    public async Task Analyze_ComputedFromWithoutAComputedInput_ReportsNothing(string type, string member)
    {
        // Arrange (a type that is not mapped, a static property, and a [ComputedFrom] that no key uses, which is DYNA119 and leaves no model)
        var source = Source($$"""
            {{type}}
            {
                public required string First { get; init; }

                public required string Last { get; init; }

                [ComputedFrom(nameof(First))]
                {{member}}
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("Key")]
    [Arguments("this.Key")]
    [Arguments("Key.ToUpperInvariant()")]
    [Arguments("Key.ToLowerInvariant()")]
    [Arguments("Key.ToUpperInvariant().ToLowerInvariant()")]
    [Arguments("Other ?? Key")]
    [Arguments("Other ?? Key ?? throw new InvalidOperationException()")]
    public async Task Analyze_PassThroughOfARawInputIntoAStrictTemplate_ReportsDyna120AtTheComputedInput(string getter)
    {
        // Arrange (each of these keeps every character of the key, so the # that the table takes reaches the index)
        var source = Coded("B#{Upper}", getter);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Through("B#{Upper}", "#")));
    }

    [Test]
    public async Task Analyze_EveryReturnPassesARawInputOn_ReportsDyna120()
    {
        // Arrange
        var source = Source($$"""
            [DynamoDbEntity, Discriminator("CODED"), PartitionKey("{{Raw}}"), GlobalIndexKey("ByUpper", PartitionKey = "B#{Upper}")]
            public partial class Coded
            {
                public required string Key { get; init; }

                [ComputedFrom(nameof(Key))]
                public string [|Upper|]
                {
                    get
                    {
                        if (Key.Length > 3)
                        {
                            return Key;
                        }

                        return Key.ToUpperInvariant();
                    }
                }
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Through("B#{Upper}", "#")));
    }

    [Test]
    public async Task Analyze_TwoStrictTemplates_ReportsEachOnce()
    {
        // Arrange (two indexes that write one template are one template)
        var source = Source($$"""
            [DynamoDbEntity, Discriminator("CODED"), PartitionKey("{{Raw}}")]
            [GlobalIndexKey("One", PartitionKey = "B#{Upper}")]
            [GlobalIndexKey("Two", PartitionKey = "C:{Upper}")]
            [GlobalIndexKey("Three", PartitionKey = "B#{Upper}")]
            public partial class Coded
            {
                public required string Key { get; init; }

                [ComputedFrom(nameof(Key))]
                public string [|Upper|] => Key.ToUpperInvariant();
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        var upper = AnalyzerRun.Marks(source)[0];
        await Assert.That(report).IsEqualTo(Analysis.OnSpans([upper, upper], [Through("B#{Upper}", "#"), Through("C:{Upper}", ":")]));
    }

    [Test]
    public async Task Analyze_ComputedInputThatIsNoString_ReportsNothing()
    {
        // Arrange (a day has no delimiter to carry, and a timestamp is no raw string)
        var source = Source("""
            [DynamoDbEntity, Discriminator("VISIT"), PartitionKey("VISIT#{At:ticks}"), GlobalIndexKey("ByDay", PartitionKey = "DAY#{Day}")]
            public partial class Visit
            {
                public required DateTimeOffset At { get; init; }

                [ComputedFrom(nameof(At))]
                public DateOnly Day => DateOnly.FromDateTime(At.UtcDateTime);
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("Key.Replace('#', '-')")]
    [Arguments("Key.Trim()")]
    [Arguments("Key + \"x\"")]
    [Arguments("Key.Length > 3 ? Key : \"short\"")]
    [Arguments("Texts.ToUpperInvariant(Key)")]
    [Arguments("Other ?? Key.Trim()")]
    [Arguments("Other?.Trim() ?? Key")]
    [Arguments("Texts.Fixed")]
    public async Task Analyze_OtherGetterOfARawInputIntoAStrictTemplate_ReportsDyna121(string getter)
    {
        // Arrange (a Replace takes the delimiter out and a concatenation keeps it, which only the getter knows)
        var source = Coded("B#{Upper}", getter);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA121 Info: 'Key' is raw in 'A#{Key:raw}', and 'Upper', computed from it, is strict in 'B#{Upper}', so a value with a delimiter of the second ('#') throws KeyFormatException there, unless the getter of 'Upper' takes the delimiter out"));
    }

    [Test]
    [Arguments("B#{Upper:raw}", "Key.ToUpperInvariant()")]
    [Arguments("{Upper}", "Key.ToUpperInvariant()")]
    [Arguments("B#{Upper}", "Other!.ToUpperInvariant()")]
    [Arguments("B#{Upper}", "throw new InvalidOperationException()")]
    public async Task Analyze_RawInputThatTheStrictTemplateCannotRefuse_ReportsNothing(string index, string getter)
    {
        // Arrange (a raw template, one without a delimiter, and a getter that hands on the other input or nothing)
        var source = Coded(index, getter).Replace("[|", string.Empty, StringComparison.Ordinal).Replace("|]", string.Empty, StringComparison.Ordinal);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_InputThatIsStrictEverywhere_ReportsNothing()
    {
        // Arrange
        var source = Source("""
            [DynamoDbEntity, Discriminator("CODED"), PartitionKey("A#{Key}"), GlobalIndexKey("ByUpper", PartitionKey = "B#{Upper}")]
            public partial class Coded
            {
                public required string Key { get; init; }

                [ComputedFrom(nameof(Key))]
                public string Upper => Key.ToUpperInvariant();
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CodeWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        const string Source = "public static class Code { public static string Run(string a) => a.Trim(); }";

        // Act
        var report = await AnalyzerRun.ReportWithAsync(Source, new ComputedGetterAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Through(string strict, string delimiters) =>
        $"DYNA120 Warning: 'Key' is raw in '{Raw}' and strict in '{strict}' through 'Upper', so a value with a delimiter of the second ('{delimiters}') fits the first and throws KeyFormatException at the second. Use raw in both, at the end of each template, or in neither.";

    private static string Person(string getter) => Source($$"""
        [DynamoDbEntity, Discriminator("PERSON"), PartitionKey("PERSON#{Id}#{Code}"), GlobalIndexKey("ByName", PartitionKey = "NAME#{Full}")]
        public partial class Person
        {
            public required string Id { get; init; }

            [KeyOnly]
            public required string Code { get; init; }

            public required string First { get; init; }

            public string Last { get; init; } = "";

            public string? Nick { get; init; }

            [Version]
            public long Revision { get; init; }

            [DynamoDbIgnore]
            public string Hidden { get; set; } = "";

            public static string Prefix => "p";

            public string Initial => First.Substring(0, 1);

            [DynamoDbAttribute("initials")]
            public string Initials => First.Substring(0, 1);

            [ComputedFrom(nameof(First))]
            public string Full => {{getter}};

            public string Format() => Last;
        }
        """);

    private static string Coded(string index, string getter) => Source($$"""
        [DynamoDbEntity, Discriminator("CODED"), PartitionKey("{{Raw}}"), GlobalIndexKey("ByUpper", PartitionKey = "{{index}}")]
        public partial class Coded
        {
            public required string Key { get; init; }

            public string? Other { get; init; }

            [ComputedFrom(nameof(Key), nameof(Other))]
            public string [|Upper|] => {{getter}};
        }

        public static class Texts
        {
            public static string Fixed => "fixed";

            public static string ToUpperInvariant(this string text) => text;
        }
        """);

    private static string Source(string entities) => AnalyzerRun.Member(string.Empty, entities);

    private static Task<string> ReportAsync(string source) => AnalyzerRun.ReportWithAsync(source, new ComputedGetterAnalyzer());
}
