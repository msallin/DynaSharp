using System.Globalization;
using System.Reflection;
using System.Text;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The table of diagnostics as shipped, and the rule that every descriptor is registered.</summary>
public sealed class DescriptorTests
{
    private const string DiagnosticsPage = "https://msallin.github.io/DynaSharp/diagnostics.html";

    [Test]
    public void All_EveryDescriptor_MatchesTheSnapshot()
    {
        // Arrange
        var text = new StringBuilder();

        // Act
        foreach (var descriptor in Descriptors.All)
        {
            text.Append(CultureInfo.InvariantCulture, $"{descriptor.Id} | {descriptor.HelpLinkUri}\n");
        }

        foreach (var descriptor in Descriptors.All)
        {
            text.Append(CultureInfo.InvariantCulture, $"{descriptor.Id} | {descriptor.DefaultSeverity} | {descriptor.Category} | {descriptor.Title} | {descriptor.MessageFormat}\n");
        }

        // Assert
        SnapshotComparer.Verify("Descriptors", text.ToString());
    }

    [Test]
    public async Task All_EveryDeclaredField_IsListedOnce()
    {
        // Arrange
        var declared = typeof(Descriptors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => ((DiagnosticDescriptor)field.GetValue(null)!).Id)
            .Order(StringComparer.Ordinal);

        // Act
        var listed = Descriptors.All.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(listed.SequenceEqual(declared)).IsTrue();
    }

    [Test]
    public async Task ReleaseFile_EveryDescriptor_IsListedWithItsSeverityAndCategory()
    {
        // Arrange
        var directory = typeof(DescriptorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ProjectDirectory").Value!;
        var generators = Path.Combine(directory, "..", "..", "src", "DynaSharp.Generators");
        var shipped = await File.ReadAllTextAsync(Path.Combine(generators, "AnalyzerReleases.Shipped.md"));
        var unshipped = await File.ReadAllTextAsync(Path.Combine(generators, "AnalyzerReleases.Unshipped.md"));

        // Act
        var missing = Descriptors.All.Where(d => !(shipped + unshipped).Contains($"{d.Id} | {d.Category} | {d.DefaultSeverity} | {d.Title}", StringComparison.Ordinal));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task HelpLink_EveryRule_PointsAtASectionOfTheDiagnosticsPageThatNamesTheRule()
    {
        // Arrange
        var directory = typeof(DescriptorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ProjectDirectory").Value!;
        var sections = SectionsOf(await File.ReadAllLinesAsync(Path.Combine(directory, "..", "..", "docs", "diagnostics.md")));

        // Act
        var broken = Descriptors.All.Where(descriptor => !IsCoveredBy(sections, descriptor)).Select(static descriptor => descriptor.Id);

        // Assert
        await Assert.That(broken).IsEmpty();
    }

    private static bool IsCoveredBy(Dictionary<string, string> sections, DiagnosticDescriptor descriptor)
    {
        var parts = descriptor.HelpLinkUri.Split('#');
        return parts[0] == DiagnosticsPage && sections.TryGetValue(parts[1], out var text) && text.Contains(descriptor.Id, StringComparison.Ordinal);
    }

    // The sections of the page by the anchor GitHub gives their heading: "## Two words, one" has the anchor "two-words-one".
    private static Dictionary<string, string> SectionsOf(string[] lines)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var anchor = string.Empty;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                anchor = string.Concat(line[3..].Where(static c => char.IsLetterOrDigit(c) || c is ' ' or '-').Select(char.ToLowerInvariant)).Replace(' ', '-');
                sections[anchor] = string.Empty;
            }
            else if (anchor.Length > 0)
            {
                sections[anchor] += line + "\n";
            }
        }

        return sections;
    }
}
