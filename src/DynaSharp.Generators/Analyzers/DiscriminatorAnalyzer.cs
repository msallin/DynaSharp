using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA026: two entities of a compilation with the same discriminator, which their items cannot be told apart by. It is a warning and no error, because the two can sit on different tables,
/// which nothing here knows. It needs every type of the compilation, so it runs at its end, and a type in another assembly is out of its sight.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class DiscriminatorAnalyzer : DiagnosticAnalyzer
{
    private const string DefaultAttributeName = "_type";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.DuplicateDiscriminator];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var claims = new ConcurrentBag<Claim>();
            start.RegisterSymbolAction(symbol => Collect((INamedTypeSymbol)symbol.Symbol, claims), SymbolKind.NamedType);
            start.RegisterCompilationEndAction(end => Report(end, claims));
        });
    }

    private static void Collect(INamedTypeSymbol type, ConcurrentBag<Claim> claims)
    {
        if (AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null
            && AttributeReader.Find(type, WellKnownNames.DiscriminatorAttribute) is { } attribute
            && AttributeReader.Text(attribute, 0) is { Length: > 0 } value)
        {
            var attributeName = AttributeReader.Named(attribute, WellKnownNames.AttributeNameArgument) ?? DefaultAttributeName;
            claims.Add(new Claim(type.Name, attributeName, value, attribute.ApplicationSyntaxReference!.GetSyntax().GetLocation()));
        }
    }

    private static void Report(CompilationAnalysisContext context, ConcurrentBag<Claim> claims)
    {
        foreach (var group in claims.GroupBy(static claim => (claim.AttributeName, claim.Value)).Where(static group => group.Skip(1).Any()))
        {
            // The order of the bag depends on the threads, and a build must report the same thing each time.
            var ordered = group.OrderBy(static claim => claim.Location.SourceTree!.FilePath, StringComparer.Ordinal).ThenBy(static claim => claim.Location.SourceSpan.Start).ToList();
            foreach (var claim in ordered.Skip(1))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.DuplicateDiscriminator, claim.Location, claim.TypeName, claim.Value, claim.AttributeName, ordered[0].TypeName));
            }
        }
    }

    /// <summary>What an entity says about itself in its items.</summary>
    /// <param name="TypeName">The entity.</param>
    /// <param name="AttributeName">The attribute of the discriminator.</param>
    /// <param name="Value">The discriminator.</param>
    /// <param name="Location">Where it says so.</param>
    private sealed record Claim(string TypeName, string AttributeName, string Value, Location Location);
}
