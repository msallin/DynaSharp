using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// What the getter of a computed input with <c>[ComputedFrom]</c> says that the attribute does not. DYNA032: the getter reads a stored property of the same instance that the attribute does not name,
/// so an update of that property goes through and the key built from the getter stays as it was. DYNA120 and DYNA121: a property that the attribute names is raw in one template, and the computed input
/// is strict in another. A getter that hands the value on with every character, such as <c>=&gt; Key.ToUpperInvariant()</c>, keeps the delimiter, which is DYNA120 as for one property in both templates.
/// Any other getter may take it out, a hash or a <c>Replace</c>, which nothing here can tell, so that is DYNA121 at Info.
/// </summary>
/// <remarks>
/// The getter is looked at as written, without the members it calls: what <c>this.Format()</c> reads is not followed, so it is not reported. A getter declared outside the entity, in a base type
/// that is not one, has no model and is not looked at either.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ComputedGetterAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.UnnamedComputedSource, Descriptors.RawBesideStrict, Descriptors.RawThroughGetter];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ComputedFromAttribute) is not null)
            {
                var models = new ModelCatalog(start.Compilation);
                start.RegisterOperationBlockAction(block => Analyze(block, models));
            }
        });
    }

    /// <summary>Whether a property is read from the instance the getter belongs to: not the receiver of an object initializer, which is an instance reference as well.</summary>
    /// <param name="reference">The read.</param>
    /// <returns>True for <c>this.X</c> or <c>X</c>.</returns>
    internal static bool IsOfThisInstance(IPropertyReferenceOperation reference) =>
        reference.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };

    /// <summary>Whether an operation stands inside a <c>nameof</c>, which reads nothing.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>True inside <c>nameof</c>.</returns>
    internal static bool IsInNameOf(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static void Analyze(OperationBlockAnalysisContext context, ModelCatalog models)
    {
        if (context.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertyGet } getter)
        {
            return;
        }

        // A model is built only for a getter that names what it reads, which few do. [ComputedFrom] on a document is DYNA012, which leaves no model, so one that is there is an entity's.
        var property = (IPropertySymbol)getter.AssociatedSymbol!;
        if (AttributeReader.FindOnProperty(property, WellKnownNames.ComputedFromAttribute) is null
            || models.ModelOf(property.ContainingType) is not { } model
            || model.Entity!.ComputedInputs.FirstOrDefault(candidate => candidate.Property == property.Name) is not { } input)
        {
            return;
        }

        var entity = model.Entity;
        var operations = context.OperationBlocks.SelectMany(static block => block.DescendantsAndSelf()).ToList();
        ReportUnnamedReads(context, model, entity, input, operations);
        ReportRawSources(context, entity, input, property, operations);
    }

    // What [ComputedFrom] could name: a stored property that is not computed, not [KeyOnly] and not the version. Each is reported once, where the getter reads it first.
    private static void ReportUnnamedReads(OperationBlockAnalysisContext context, TypeModel model, EntityModel entity, ComputedInput input, List<IOperation> operations)
    {
        var nameable = model.Properties
            .Where(property => !property.IsWriteOnly && property.RestoredFromKey is null && property.Name != entity.VersionProperty && !input.Dependencies.Contains(property.Name))
            .Select(static property => property.Name)
            .ToList();
        var reported = new HashSet<string>();
        foreach (var reference in operations.OfType<IPropertyReferenceOperation>().Where(static reference => IsOfThisInstance(reference) && !IsInNameOf(reference)))
        {
            var name = reference.Property.Name;
            if (nameable.Contains(name) && reported.Add(name))
            {
                var properties = ImmutableDictionary<string, string?>.Empty.Add(DiagnosticInfo.FixProperty, name);
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnnamedComputedSource, reference.Syntax.GetLocation(), properties, input.Property, name));
            }
        }
    }

    // One report for each template that holds the computed input strictly and refuses something, as DYNA120 has one for each strict template of a property.
    private static void ReportRawSources(OperationBlockAnalysisContext context, EntityModel entity, ComputedInput input, IPropertySymbol property, List<IOperation> operations)
    {
        var templates = entity.Keys().Select(static key => key.Template).OfType<KeyTemplate>().Distinct().ToList();
        var passedOn = PassedOn(operations);
        foreach (var strict in templates.Where(template => template.Delimiters.Length > 0 && Holds(template, input.Property, isRaw: false)))
        {
            var sources =
                from dependency in input.Dependencies
                where passedOn is null || passedOn.Contains(dependency)
                from raw in templates.Where(template => Holds(template, dependency, isRaw: true))
                select (Dependency: dependency, Raw: raw);
            if (sources.FirstOrDefault() is { Raw: not null } source)
            {
                var location = property.Locations[0];
                context.ReportDiagnostic(passedOn is null
                    ? Diagnostic.Create(Descriptors.RawThroughGetter, location, source.Dependency, source.Raw.Text, input.Property, strict.Text, strict.Delimiters)
                    : Diagnostic.Create(Descriptors.RawBesideStrict, location, source.Dependency, source.Raw.Text, strict.Text, strict.Delimiters, $" through '{input.Property}'"));
            }
        }
    }

    private static bool Holds(KeyTemplate template, string property, bool isRaw) =>
        template.Segments.OfType<PlaceholderSegment>().Any(placeholder => placeholder.Property == property && placeholder.Encoding is StringEncoding encoding && encoding.IsRaw == isRaw);

    // The properties that every return of the getter hands on with every character, or null when one return does anything else. A return inside a lambda of the getter counts as one of its own.
    private static List<string>? PassedOn(List<IOperation> operations)
    {
        var passed = new List<string>();
        foreach (var value in operations.OfType<IReturnOperation>().Select(static operation => operation.ReturnedValue!))
        {
            if (PassedOn(value) is not { } names)
            {
                return null;
            }

            passed.AddRange(names);
        }

        return passed;
    }

    // => Key, => Key.ToUpperInvariant(), => Key.ToLowerInvariant(), => First ?? Second: each keeps every character of what it reads, a delimiter too. A throw hands nothing on.
    private static List<string>? PassedOn(IOperation value) => value switch
    {
        IConversionOperation { Operand: IThrowOperation } => [],
        IPropertyReferenceOperation reference when IsOfThisInstance(reference) => [reference.Property.Name],
        IInvocationOperation { TargetMethod.Name: "ToUpperInvariant" or "ToLowerInvariant", TargetMethod.ContainingType.SpecialType: SpecialType.System_String } call => PassedOn(call.Instance!),
        ICoalesceOperation coalesce => PassedOn(coalesce.Value) is { } left && PassedOn(coalesce.WhenNull) is { } right ? [.. left, .. right] : null,
        _ => null,
    };
}
