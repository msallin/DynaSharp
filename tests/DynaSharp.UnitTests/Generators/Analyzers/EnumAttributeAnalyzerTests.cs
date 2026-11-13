using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// DYNA033: <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> on an enum that the generated converter does not read by name. DYNA044 to DYNA046: the stored names of
/// <c>[DynamoDbEnumName]</c>.
/// </summary>
public sealed class EnumAttributeAnalyzerTests
{
    private const string Lenient = "DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)";

    private const string StoredAsNumber = "it is [Flags], which is stored as a number and has no name to miss. Take the attribute off.";

    private const string HasConverter = "its [DynamoDbConverter] reads it in place of the generated converter. Take the attribute off, or answer CountsAsMissing in that converter.";

    private const string Converter = """

        public readonly struct WindConverter : IDynamoDbConverter<Wind>
        {
            public static void Write(DynamoDbWriter writer, Wind value) => writer.WriteNumber((int)value);

            public static Wind Read(ref DynamoDbReader reader, AttributeType type) => (Wind)reader.GetNumber<int>();
        }
        """;

    [Test]
    public async Task Analyze_FlagsEnum_ReportsThatANumberHasNoName()
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty, $"[Flags, [|{Lenient}|]] public enum Wind {{ None = 0, Calm = 1, Gale = 2 }}\n");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(StoredAsNumber)));
    }

    [Test]
    [Arguments("")]
    [Arguments("Flags, ")]
    public async Task Analyze_EnumWithAConverterOfItsOwn_ReportsThatTheConverterReadsIt(string flags)
    {
        // Arrange (the converter wins over the generated one, so it is the reason, [Flags] or not)
        var source = AnalyzerRun.Member(string.Empty, $"[{flags}DynamoDbConverter(typeof(WindConverter)), [|{Lenient}|]] public enum Wind {{ Calm = 1, Gale = 2 }}\n" + Converter);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(HasConverter)));
    }

    [Test]
    [Arguments($"[{Lenient}]")]
    [Arguments("[DynamoDbEnum]")]
    [Arguments("[Flags, DynamoDbEnum(UnknownName = UnknownEnumName.Throw)]")]
    [Arguments("[Flags, DynamoDbEnum]")]
    [Arguments("[DynamoDbConverter(typeof(WindConverter)), DynamoDbEnum(UnknownName = UnknownEnumName.Throw)]")]
    [Arguments("[DynamoDbConverter(typeof(WindConverter))]")]
    [Arguments("[Flags]")]
    [Arguments("")]
    public async Task Analyze_AttributeThatHasAnEffectOrSaysTheDefault_ReportsNothing(string attributes)
    {
        // Arrange (UnknownEnumName.Throw is what happens without the attribute, so it cannot be wrong anywhere)
        var source = AnalyzerRun.Member(string.Empty, attributes + " public enum Wind { Calm = 1, Gale = 2 }\n" + Converter);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilationWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var compilation = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText("""[System.Flags] public enum Wind { None = 0, Calm = 1 }""")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Act
        var diagnostics = await compilation.WithAnalyzers([new EnumAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("[Flags] ", "the enum is [Flags], which is stored as a number. Take the attribute off.")]
    [Arguments("[DynamoDbConverter(typeof(WindConverter))] ", "the [DynamoDbConverter] of the enum reads and writes it in place of the generated converter. Take the attribute off, or store the name in that converter.")]
    [Arguments("[Flags, DynamoDbConverter(typeof(WindConverter))] ", "the [DynamoDbConverter] of the enum reads and writes it in place of the generated converter. Take the attribute off, or store the name in that converter.")]
    public async Task Analyze_NameOnAMemberTheGeneratedConverterDoesNotName_ReportsThatItHasNoEffect(string attributes, string reason)
    {
        // Arrange (an empty name is not checked as well: the attribute is to go)
        var source = AnalyzerRun.Member(string.Empty, attributes + "public enum Wind { None = 0, [[|DynamoDbEnumName(\"\")|]] Calm = 1 }\n" + Converter);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA046 Warning: [DynamoDbEnumName] on 'Calm' changes nothing, because {reason}"));
    }

    [Test]
    [Arguments("null", "is null")]
    [Arguments("\"\"", "is empty")]
    [Arguments("\" \\t\"", "is white space alone")]
    [Arguments("\"CALM\\n\"", "holds a control character")]
    public async Task Analyze_NameThatNothingCanHold_ReportsWhy(string name, string reason)
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty, $"public enum Wind {{ [[|DynamoDbEnumName({name})|]] Calm = 1, Gale = 2 }}\n");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA045 Error: [DynamoDbEnumName] on 'Calm' {reason}, which no attribute or key can hold. Give it a name of visible characters."));
    }

    [Test]
    [Arguments("[DynamoDbEnumName(\"WIND\")] Calm = 1, [DynamoDbEnumName(\"WIND\")] Gale = 2", "Calm", "WIND", "the stored name of 'Gale' as well")]
    [Arguments("[DynamoDbEnumName(\"Gale\")] Calm = 1, Gale = 2", "Calm", "Gale", "the stored name of 'Gale' as well")]
    [Arguments("[DynamoDbEnumName(\"Gale\")] Calm = 1, [DynamoDbEnumName(\"GALE\")] Gale = 2", "Calm", "Gale", "the name of the member 'Gale'")]
    [Arguments("[DynamoDbEnumName(\"CALM\")] Calm = 1, [DynamoDbEnumName(\"Calm\")] Gale = 2", "Gale", "Calm", "the name of the member 'Calm'")]
    public async Task Analyze_NameThatReadsAsTwoValues_ReportsBothMembersOnTheEnum(string members, string member, string name, string reason)
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty, $"public enum [|Wind|] {{ {members} }}\n");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA044 Error: Enum 'Wind' stores '{member}' as '{name}', which is {reason}, so a read of '{name}' cannot tell them apart. Give each value a stored name of its own."));
    }

    [Test]
    [Arguments("[DynamoDbEnumName(\"CALM\")] Calm = 1, [DynamoDbEnumName(\"GALE\")] Gale = 2")]
    [Arguments("[DynamoDbEnumName(\"CALM\")] Calm = 1, [DynamoDbEnumName(\"CALM\")] Still = 1, [DynamoDbEnumName(\"Calm\")] Quiet = 1")]
    [Arguments("[DynamoDbEnumName(\"GALE\")] Calm = 1, [DynamoDbEnumName(\"CALM\")] Gale = 2")]
    [Arguments("[DynamoDbEnumName(\"CALM\")] Calm = 1, Gale = 2")]
    public async Task Analyze_NamesOneToOneOrOfAliases_ReportsNothing(string members)
    {
        // Arrange (aliases share a value, so a name of one reads as the other; swapping two names is what another writer may have done)
        var source = AnalyzerRun.Member(string.Empty, $"public enum Wind {{ {members} }}\n");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new EnumAttributeAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Message(string reason) => $"DYNA033 Warning: Enum 'Wind' asks for an unknown name to count as missing, but {reason}";
}
