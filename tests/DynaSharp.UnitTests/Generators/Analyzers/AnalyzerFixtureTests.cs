using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>An analyzer that a shared fixture cannot run has to fail the test: it would report nothing, and a test that expects nothing would pass.</summary>
public sealed class AnalyzerFixtureTests
{
    [Test]
    public async Task ReportWithAsync_AnalyzerThatReportsWhenACompilationEnds_Throws()
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty);

        // Act
        var thrown = await TestClient.CaptureAsync<InvalidOperationException>(() => AnalyzerFixture.NoModel.ReportWithAsync(source, new DiscriminatorAnalyzer()));

        // Assert
        await Assert.That(thrown.Message).Contains(nameof(DiscriminatorAnalyzer));
    }

    [Test]
    [Arguments(nameof(AnalysisContext.RegisterCompilationAction))]
    [Arguments(nameof(AnalysisContext.RegisterSymbolStartAction))]
    [Arguments(nameof(AnalysisContext.RegisterSyntaxTreeAction))]
    public async Task ReportWithAsync_AnalyzerWithAnActionThatNoTreeRuns_Throws(string action)
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty);

        // Act
        var thrown = await TestClient.CaptureAsync<InvalidOperationException>(() => AnalyzerFixture.NoModel.ReportWithAsync(source, new Unrunnable(action)));

        // Assert
        await Assert.That(thrown.Message).Contains(nameof(Unrunnable));
    }

    [Test]
    public async Task ReportWithAsync_AnalyzerThatThrows_ShowsItInTheReport()
    {
        // Arrange (a row that expects nothing has to fail when the analyzer breaks on its snippet)
        var source = AnalyzerRun.Member("private static int Value => 1;");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportWithAsync(source, new Throwing());

        // Assert
        await Assert.That(report).Contains("AD0001");
    }

#pragma warning disable RS1001 // A test hands these to the fixture itself and no compiler loads them, so they need no registration.
    private sealed class Throwing : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptors.CommandOutlivesExpression];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(static _ => throw new InvalidOperationException("Broken."), OperationKind.Literal);
        }
    }

    private sealed class Unrunnable(string action) : DiagnosticAnalyzer
#pragma warning restore RS1001
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptors.CommandOutlivesExpression];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            switch (action)
            {
                case nameof(AnalysisContext.RegisterCompilationAction):
                    context.RegisterCompilationAction(static _ => { });
                    break;
                case nameof(AnalysisContext.RegisterSymbolStartAction):
                    context.RegisterSymbolStartAction(static start => start.RegisterSymbolEndAction(static _ => { }), SymbolKind.NamedType);
                    break;
                default:
                    context.RegisterSyntaxTreeAction(static _ => { });
                    break;
            }
        }
    }
}
