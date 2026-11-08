using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using static DynaSharp.UnitTests.Generators.Analyzers.CodeFixRun;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix-all of the fixes that write text edits, over what <see cref="CodeFixRun"/> does not give it: a key no diagnostic offers, and a diagnostic without a place.</summary>
public sealed class TextEditCodeFixTests
{
    private static readonly (string, string)[] Files =
        One("[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey(\"O#{Id}\"), SortKey(\"P#{Price}\")]\npublic partial class Order\n{\n    public required string Id { get; set; }\n    public decimal Price { get; set; }\n}\n");

    [Test]
    public async Task GetFixAsync_KeyNoDiagnosticOffers_GivesNoAction()
    {
        // Arrange
        var context = await ContextAsync("DYNA108.d");

        // Act
        var action = await context.CodeFixProvider.GetFixAllProvider()!.GetFixAsync(context);

        // Assert
        await Assert.That(action).IsNull();
    }

    [Test]
    public async Task GetFixAsync_DiagnosticWithoutAPlace_FixesTheOthers()
    {
        // Arrange
        var context = await ContextAsync("DYNA108.plain");

        // Act
        var action = await context.CodeFixProvider.GetFixAllProvider()!.GetFixAsync(context);

        // Assert
        await Assert.That(action!.Title).IsEqualTo("Change to {Price:plain}");
    }

    // A fix-all of the project over its diagnostics and one on a base type in another project of the workspace, which has no place here.
    private static async Task<FixAllContext> ContextAsync(string key)
    {
        var fix = new KeyPlaceholderCodeFix();
        var (solution, diagnostics) = await new CodeFixRun(fix, "DYNA108").RunAsync(Files);
        var document = solution.GetDocument(diagnostics[0].Location.SourceTree)!;
        ImmutableArray<Diagnostic> all = [.. diagnostics, Diagnostic.Create(Descriptors.NumberWithoutFixedFormat, Location.None, "Price")];
        return new FixAllContext(document, fix, FixAllScope.Project, key, ["DYNA108"], new Provider(all), CancellationToken.None);
    }

    private sealed class Provider(ImmutableArray<Diagnostic> all) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(all);
    }
}
