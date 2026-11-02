using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// What a command says that the model of its entity contradicts. DYNA205: a write by handle to a property the key of the table is made of, which an update cannot do. The rule is
/// <c>KeyInputChanges</c>, for the property itself and nothing else: what follows from a key of the table, such as a key of an index, is its business at run time. DYNA206: <c>IfNotExists</c> on a
/// <c>Put</c> whose item shows a version above 0 in the chain, which <c>WriteRules.Settle</c> refuses. Version 0 is a first write, where the two conditions agree, so a version that is not a constant says
/// nothing and is not reported. DYNA207: a constant name in the <c>Index(string)</c> that takes the names from the entity, which throws at run time when the entity does not declare it.
/// </summary>
/// <remarks>A command says what it is on by a type argument, and a step of a transaction by the table of the verb it came from. Whatever cannot be followed, such as a generic helper or a step in a variable, is not reported.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class EntityCommandAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The parameter that takes the handle in a write of <see cref="WriteMethods"/>.</summary>
    internal const string HandleParameter = "attribute";

    /// <summary>The members of <c>IUpdatable&lt;TSelf&gt;</c> that take a handle, by name.</summary>
    internal static readonly HashSet<string> WriteMethods = ["Set", "SetIfNotNull", "Add", "Remove", "SetIfNotExists", "Delete"];

    private const string UpdateVerb = "Update";

    private const string PutVerb = "Put";

    private const string IgnoreVersion = "IgnoreVersion";

    private const string ItemParameter = "item";

    private const string HandlesClass = "Attrs";

    private const string StepVerbs = "TransactWriteExtensions";

    private const string EntityIndexes = "TableExtensions";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.WriteOfTableKeyInput, Descriptors.IfNotExistsOnVersionedPut, Descriptors.UndeclaredIndex];

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
        if (!DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType))
        {
            return;
        }

        if (WriteMethods.Contains(invocation.TargetMethod.Name))
        {
            AnalyzeWrite(context, invocation, models);
        }
        else if (invocation.TargetMethod.Name == "IfNotExists")
        {
            AnalyzeIfNotExists(context, invocation, models);
        }
        else if (invocation.TargetMethod is { Name: "Index", ContainingType.Name: EntityIndexes })
        {
            AnalyzeIndex(context, invocation, models);
        }
    }

    // Index<T>(this Table<T> table, string name) and Index<TEntity>(this Table<Item> table, string name): the entity is the one type argument, and a type parameter has no model.
    private static void AnalyzeIndex(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        var name = invocation.Arguments[1].Value;
        if (name.ConstantValue is { HasValue: true, Value: string index }
            && invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol entity
            && models.ModelOf(entity)?.Entity is { } model
            && !model.Indexes.Any(declared => declared.IndexName == index))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.UndeclaredIndex, name.Syntax.GetLocation(), entity.Name, index));
        }
    }

    private static void AnalyzeIfNotExists(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (EntityOf(invocation, PutVerb) is { } entity
            && models.ModelOf(entity)?.Entity?.VersionProperty is { } version
            && !GivesUpTheVersion(invocation)
            && HoldsVersionAboveZero(ItemOfPut(invocation), version))
        {
            var name = ((InvocationExpressionSyntax)invocation.Syntax).Expression.GetLastToken();
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IfNotExistsOnVersionedPut, name.GetLocation(), entity.Name));
        }
    }

    // IgnoreVersion holds wherever it stands in the chain, also after this call, so this looks at all of the member.
    private static bool GivesUpTheVersion(IInvocationOperation invocation)
    {
        IOperation root = invocation;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        return root.Descendants().OfType<IInvocationOperation>()
            .Any(other => other.TargetMethod.Name == IgnoreVersion && SymbolEqualityComparer.Default.Equals(other.Type, invocation.Type));
    }

    private static IOperation? ItemOfPut(IInvocationOperation invocation)
    {
        var call = ReceiverOf(invocation);
        while (call is IInvocationOperation { TargetMethod.Name: not PutVerb } clause)
        {
            call = ReceiverOf(clause);
        }

        return (call as IInvocationOperation)?.Arguments.Where(static argument => argument.Parameter!.Name == ItemParameter).Select(static argument => argument.Value).FirstOrDefault();
    }

    private static bool HoldsVersionAboveZero(IOperation? item, string version) => item switch
    {
        IObjectCreationOperation creation => SetsAboveZero(creation.Initializer, version),
        IWithOperation copy => SetsAboveZero(copy.Initializer, version),
        _ => false,
    };

    private static bool SetsAboveZero(IObjectOrCollectionInitializerOperation? initializer, string property) =>
        initializer is not null
        && initializer.Initializers.OfType<ISimpleAssignmentOperation>().Any(assignment =>
            assignment.Target is IPropertyReferenceOperation target
            && target.Property.Name == property
            && assignment.Value.ConstantValue is { HasValue: true, Value: var value }
            && Convert.ToDecimal(value, CultureInfo.InvariantCulture) > 0);

    private static void AnalyzeWrite(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (invocation.TargetMethod.Parameters[0].Name == HandleParameter
            && invocation.Arguments[0].Value is IPropertyReferenceOperation { Property: { ContainingType.Name: HandlesClass } handle } argument
            && EntityOf(invocation, UpdateVerb) is { } entity
            && Inherits(entity, handle.ContainingType.ContainingType)
            && models.ModelOf(entity)?.Entity is { } model
            && FeedsTableKey(model, handle.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.WriteOfTableKeyInput, argument.Syntax.GetLocation(), handle.Name, entity.Name));
        }
    }

    private static bool Inherits(INamedTypeSymbol entity, INamedTypeSymbol? owner)
    {
        for (var type = entity; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, owner))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FeedsTableKey(EntityModel entity, string property) =>
        new[] { entity.PartitionKey, entity.SortKey }.OfType<KeyModel>().Any(key => key.PlainProperty == property || HoldsPlaceholder(key, property));

    private static bool HoldsPlaceholder(KeyModel key, string property) =>
        (key.Template?.Segments ?? default).OfType<PlaceholderSegment>().Any(placeholder => placeholder.Property == property);

    private static IOperation? ReceiverOf(IInvocationOperation invocation) =>
        invocation.Instance ?? (invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments[0].Value : null);

    // A method of the library with one of these names is an instance or an extension method, so there is a receiver.
    private static INamedTypeSymbol? EntityOf(IInvocationOperation invocation, string verb)
    {
        var receiver = ReceiverOf(invocation)!;
        if (receiver.Type is not INamedTypeSymbol type)
        {
            return null;
        }

        return type.Name == verb + "Command" ? type.TypeArguments[0] as INamedTypeSymbol : type.Name == verb + "Step" ? EntityOfStep(receiver, verb) : null;
    }

    // The verb has the table as its second type argument: Update<TChain, T>(chain, table, ...).
    private static INamedTypeSymbol? EntityOfStep(IOperation step, string verb)
    {
        var type = step.Type;
        while (step is IInvocationOperation invocation && SymbolEqualityComparer.Default.Equals(invocation.Type, type))
        {
            if (invocation.TargetMethod is { ContainingType.Name: StepVerbs } method && method.Name == verb)
            {
                return method.TypeArguments[1] as INamedTypeSymbol;
            }

            step = ReceiverOf(invocation)!;
        }

        return null;
    }
}
