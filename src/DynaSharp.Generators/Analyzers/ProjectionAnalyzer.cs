using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// What a read of part of an item cannot get from it. DYNA208: <c>orders.Query(pk).Project("Total").ItemsAsync()</c> where the entity has a member that a read throws without, which is what the generator's
/// <see cref="MissingBehavior.Throw"/> says. A read adds its discriminator and its keys, so a member that one of those restores is not missing. DYNA209: <c>Select(handle)</c>, or <c>And(handle)</c> after it, of an attribute that an item can
/// lack where a read of the entity goes on: a stored property that a key of the table holds, which an item created by an update does not have and a read restores from the key,
/// a property with <c>[OmitEmpty]</c> that is not nullable, which an empty value leaves out and a read keeps at its initial value, and a property
/// that is not nullable or <c>required</c> whose converter can count a stored value as missing, which a read keeps at its initial value too. A converter that says what a missing attribute reads as gives <c>Select</c> a value for all of these.
/// </summary>
/// <remarks>
/// A chain says what it reads when it is followed from the table to its terminal, so a chain that came from a variable, goes on in one, or passes a method of the consumer says nothing, and neither does a
/// path that is not constant. A read of an index adds the keys of that index, and which one it is does not show in the type, so such a read counts as holding every key of an index.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ProjectionAnalyzer : DiagnosticAnalyzer
{
    private const string ProjectVerb = "Project";

    private const string SelectVerb = "Select";

    // What adds an attribute to a Select, on the commands it leaves: GetSelectCommand and the others of that name. A condition has an And too.
    private const string AndVerb = "And";

    private const string SelectCommandSuffix = "SelectCommand";

    private const string HandlesClass = "Attrs";

    private const string NestedPathMethod = "At";

    private const string NameMethod = "Attr";

    private const string IndexType = "Index";

    private const string SelectOfOmitted = "has [OmitEmpty], and an item that held an empty value has no such attribute: a read of the entity keeps the initial value, Select does not and throws. Read the entity, or make the property nullable.";

    private const string SelectOfCounted = "has a converter that can count a stored value as missing: a read of the entity keeps the initial value, Select does not and throws. Read the entity, make the property nullable, or have the converter answer TryReadMissing.";

    // The terminals that read no item: a count has no use for a projection, and it throws for one anyway.
    private static readonly HashSet<string> ReadNoItem = ["ToRequestJson", "CountAsync"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.ProjectionLeavesOutRequired, Descriptors.SelectOfKeyInput];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                var models = new ModelCatalog(start.Compilation);
                start.RegisterOperationAction(operation => Analyze(operation, models), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsOfTheLibrary(invocation))
        {
            return;
        }

        if (invocation.TargetMethod.Name == ProjectVerb)
        {
            AnalyzeProject(context, invocation, models);
        }
        else if (invocation.TargetMethod.Name == SelectVerb
            || (invocation.TargetMethod.Name == AndVerb && invocation.TargetMethod.ContainingType.Name.EndsWith(SelectCommandSuffix, StringComparison.Ordinal)))
        {
            AnalyzeSelect(context, invocation, models);
        }
    }

    private static bool IsOfTheLibrary(IInvocationOperation invocation) => DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType);

    private static void AnalyzeSelect(OperationAnalysisContext context, IInvocationOperation select, ModelCatalog models)
    {
        var argument = select.Arguments[0].Value;
        if (argument is IPropertyReferenceOperation { Property: { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } handle }
            && models.ModelOf(owner) is { Entity: { } entity } model
            && WhySelectThrows(entity, model.Property(handle.Name)) is { } reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.SelectOfKeyInput, argument.Syntax.GetLocation(), handle.Name, owner.Name, reason));
        }
    }

    // What a read of the entity gets past and Select does not. A version is not among it: every write of the library stores one, an item without one comes from code of another kind,
    // and a Select of such an item throws a message that names VersionMissing.Allow, which beats the 0 a read of the entity gives without a word.
    private static string? WhySelectThrows(EntityModel entity, PropertyModel property)
    {
        var converter = property.Converter!;
        if (converter.AnswersMissing)
        {
            return null;
        }

        if (property.FallbackKey is { } key && IsKeyOfTheTable(entity, key))
        {
            return $"is held by the key '{key}' of the table, and an item that an Update created has no attribute of its own for it: a read of the entity restores it from the key, Select does not and throws. Read the entity, or Select only from items a Put wrote.";
        }

        // A set has an answer for a missing attribute, and a nullable property goes through the Select that gives null.
        if (converter.EmptySet is not null || property.ValueType.EndsWith("?", StringComparison.Ordinal))
        {
            return null;
        }

        if (property.OmitEmptyConverter is not null)
        {
            return SelectOfOmitted;
        }

        // A required member throws in a read of the entity as well.
        return converter.CountsAsMissing && property.Missing != MissingBehavior.Throw ? SelectOfCounted : null;
    }

    // An update stores no input of a key of the table unless it asks, which is what leaves an item with the key alone. An input of an index is set like any other attribute, and its key follows.
    private static bool IsKeyOfTheTable(EntityModel entity, string key) => key == entity.PartitionKey.AttributeName || key == entity.SortKey?.AttributeName;

    private static void AnalyzeProject(OperationAnalysisContext context, IInvocationOperation project, ModelCatalog models)
    {
        if (CommandThatReads(project) is not { } command || Projected(project, models, out var source) is not { Count: > 0 } names)
        {
            return;
        }

        var read = DynaSharpTypes.TypesReadBy(command).ToList();
        var types = read.OfType<INamedTypeSymbol>().Select(models.ModelOf).ToList();
        if (types.Count != read.Count || types.Contains(null))
        {
            return;
        }

        var keys = KeysOf(types!, source.Type!.Name == IndexType);
        foreach (var type in types)
        {
            if (type!.Properties.FirstOrDefault(property => IsNeeded(property) && !Reaches(property, names, keys)) is { } missing)
            {
                var name = ((InvocationExpressionSyntax)project.Syntax).Expression.GetLastToken();
                var select = project.Type!.GetMembers(SelectVerb).IsEmpty ? string.Empty : ", or use Select(handle).And(handle) to read the attributes alone";
                var edits = WithMissing(project, read.Cast<INamedTypeSymbol>().Zip(types!, static (symbol, model) => (symbol, model!)), names, keys);
                context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.ProjectionLeavesOutRequired, name.GetLocation(), edits, missing.Name, type.Name, select));
                return;
            }
        }
    }

    // Every member left out, of every type, after the last path of this Project, in its form: the attribute as a string, or the handle where it takes handles.
    private static List<SuggestedEdit> WithMissing(IInvocationOperation project, IEnumerable<(INamedTypeSymbol Symbol, TypeModel Model)> types, HashSet<string> names, HashSet<string> keys)
    {
        var byHandle = ((INamedTypeSymbol)project.TargetMethod.Parameters[0].Type).TypeArguments[0].SpecialType != SpecialType.System_String;
        var elements = ElementsOf(project);
        if (elements.IsEmpty)
        {
            return [];
        }

        var model = project.SemanticModel!;
        var last = elements[elements.Length - 1].Syntax;
        var added = new List<string?>();
        foreach (var (symbol, type) in types)
        {
            foreach (var property in type.Properties.Where(property => IsNeeded(property) && !Reaches(property, names, keys)))
            {
                added.Add(byHandle ? HandleOf(symbol, property, model, last.SpanStart) : PathOf(property.RestoredFromKey ?? property.AttributeName));
            }
        }

        if (added.Contains(null))
        {
            return [];
        }

        var list = string.Join(", ", added.Distinct(StringComparer.Ordinal));
        return [new SuggestedEdit("Add " + list + " to Project", new TextSpan(last.Span.End, 0), ", " + list)];
    }

    // "Status". A name with a dot or a bracket would be read as a path into another attribute.
    private static string? PathOf(string attribute) => attribute.IndexOfAny(['.', '[']) < 0 ? SymbolDisplay.FormatLiteral(attribute, quote: true) : null;

    // Order.Attrs.Status, as the code names it there. A member that a key restores has no attribute, so no handle reaches it.
    private static string? HandleOf(INamedTypeSymbol type, PropertyModel property, SemanticModel model, int position) =>
        property.RestoredFromKey is null && type.GetTypeMembers(HandlesClass).Any(handles => !handles.GetMembers(property.Name).IsEmpty)
            ? type.ToMinimalDisplayString(model, position) + "." + HandlesClass + "." + property.Name
            : null;

    // Follows the chain from this call to its end. Another Project says more, and is the one to look at. What comes after Select is no entity.
    private static INamedTypeSymbol? CommandThatReads(IInvocationOperation project)
    {
        IOperation command = project;
        while (NextCall(command) is { } next)
        {
            if (!IsOfTheLibrary(next) || next.TargetMethod.Name is ProjectVerb or SelectVerb || ReadNoItem.Contains(next.TargetMethod.Name))
            {
                return null;
            }

            if (!DynaSharpTypes.IsCommand(next.Type))
            {
                return (INamedTypeSymbol)command.Type!;
            }

            command = next;
        }

        return command.Parent is IAwaitOperation or IExpressionStatementOperation ? (INamedTypeSymbol)command.Type! : null;
    }

    private static IInvocationOperation? NextCall(IOperation command) => command.Parent switch
    {
        IInvocationOperation call when call.Instance == command => call,
        IArgumentOperation { Parent: IInvocationOperation call } argument when call.TargetMethod.IsExtensionMethod && call.Arguments[0] == argument => call,
        _ => null,
    };

    // A method of the library that a chain goes through is an instance or an extension method, so there is a receiver.
    private static IOperation ReceiverOf(IInvocationOperation invocation) => invocation.Instance ?? invocation.Arguments[0].Value;

    // Everything the chain projects, or nothing if that is not known. The source is what the chain started from.
    private static HashSet<string>? Projected(IInvocationOperation last, ModelCatalog models, out IOperation source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        source = last;
        while (source is IInvocationOperation call && IsOfTheLibrary(call))
        {
            source = ReceiverOf(call);
            if (!DynaSharpTypes.IsCommand(source.Type))
            {
                return names;
            }

            if (call.TargetMethod.Name == ProjectVerb && !AddRoots(call, names, models))
            {
                return null;
            }
        }

        return null;
    }

    // Project("a", "b"), Project(["a", "b"]) and Project(new[] { "a", "b" }); default for a span from elsewhere.
    private static ImmutableArray<IOperation> ElementsOf(IInvocationOperation project)
    {
        var value = project.Arguments[0].Value;
        return (value is IConversionOperation conversion ? conversion.Operand : value) switch
        {
            IArrayCreationOperation { Initializer: { } initializer } => initializer.ElementValues,
            ICollectionExpressionOperation collection => collection.Elements,
            _ => default,
        };
    }

    private static bool AddRoots(IInvocationOperation project, HashSet<string> names, ModelCatalog models)
    {
        var elements = ElementsOf(project);
        if (elements.IsDefault)
        {
            return false;
        }

        foreach (var element in elements)
        {
            if (RootOf(element, models) is not { } root)
            {
                return false;
            }

            names.Add(root);
        }

        return true;
    }

    // The attribute a path starts with: Total for "Total", "Total.Net" and Order.Attrs.Total, and ShipTo for Order.Attrs.ShipTo.At(Address.Attrs.City). Null for what cannot be known here.
    private static string? RootOf(IOperation value, ModelCatalog models) => value switch
    {
        { ConstantValue: { HasValue: true, Value: string path } } => PathSyntax.IsValid(path) ? PathSyntax.Root(path) : null,
        IConversionOperation conversion => RootOf(conversion.Operand, models),
        IPropertyReferenceOperation { Property: { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } handle } => models.ModelOf(owner)?.Property(handle.Name).AttributeName,
        IInvocationOperation { TargetMethod.Name: NestedPathMethod or NameMethod } call when IsOfTheLibrary(call) => RootOf(call.Arguments[0].Value, models),
        IObjectCreationOperation { Type: { } type } creation when DynaSharpTypes.IsDeclaredByDynaSharp(type) => RootOf(creation.Arguments[0].Value, models),
        _ => null,
    };

    private static HashSet<string> KeysOf(List<TypeModel> types, bool readsAnIndex)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in types.Select(static type => type.Entity).OfType<EntityModel>())
        {
            keys.Add(entity.PartitionKey.AttributeName);
            if (entity.SortKey is { } sortKey)
            {
                keys.Add(sortKey.AttributeName);
            }

            if (readsAnIndex)
            {
                keys.UnionWith(entity.Keys().Select(static key => key.AttributeName));
            }
        }

        return keys;
    }

    private static bool IsNeeded(PropertyModel property) => property.Missing == MissingBehavior.Throw && !property.IsWriteOnly;

    // A member gets to the read by its attribute, or by the key that restores it. A [KeyOnly] property has no attribute to go by.
    private static bool Reaches(PropertyModel property, HashSet<string> names, HashSet<string> keys)
    {
        var restoredBy = property.RestoredFromKey ?? property.FallbackKey;
        return (restoredBy is not null && (names.Contains(restoredBy) || keys.Contains(restoredBy)))
            || (property.RestoredFromKey is null && (names.Contains(property.AttributeName) || keys.Contains(property.AttributeName)));
    }
}
