using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// What the delegate of a <c>RetryOnConflictAsync</c> holds that makes the retry useless, which nothing shows until two writers meet. DYNA217: a second retry inside the first, so the runs multiply and
/// one that gave up counts as a conflict of the outer one, because <c>ConflictRetry.RunAsync</c> rethrows what <c>FailureClassifier.IsConflict</c> accepts. DYNA218: a version that stays the same from run to
/// run, because it was read before the retry started, so every run sends it and fails the same way.
/// </summary>
/// <remarks>
/// A warning fails the build of a consumer, so DYNA218 reports what it can tell apart from correct code. An item that is written with its version needs a read in the member to be seen, because a new item
/// built outside the delegate is correct and looks the same, also in a parameter. A version on its own says that it was read somewhere, unless it is a constant or comes from an object made here. A retry with
/// one run is no retry, whatever it holds, and one that leaves a failed condition to the caller runs nothing again with a stale version, so it has no DYNA218. All of this costs nothing outside a delegate: every other invocation ends at the name, and the data flow runs when a delegate holds a write with a version.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ConflictRetryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The name of the retry, on any type of the library.</summary>
    internal const string RetryMethod = "RetryOnConflictAsync";

    /// <summary>The parameter of the retry that takes the delegate.</summary>
    internal const string OperationParameter = "operation";

    /// <summary>The parameter of the retry that says which conflicts run the delegate again.</summary>
    internal const string RetryOnParameter = "retryOn";

    /// <summary><c>ConflictKind.ConditionalCheckFailed</c>, which this assembly cannot take from the library, as the number it is.</summary>
    internal const int ConditionalCheckFailed = 1;

    /// <summary>The types of the library whose <c>Put</c> and <c>Delete</c> take an item or an entity, whose version a run would send.</summary>
    internal static readonly HashSet<string> ItemWriters = ["Table", "EntityTableExtensions", "TransactWriteExtensions"];

    private const string MaxAttemptsParameter = "maxAttempts";

    private const string VersionParameter = "version";

    private const string IfVersion = "IfVersion";

    private const string IgnoreVersion = "IgnoreVersion";

    private const string Delete = "Delete";

    private const string ConfigureAwait = "ConfigureAwait";

    private static readonly HashSet<string> ItemWrites = ["Put", Delete];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.NestedConflictRetry, Descriptors.VersionReadBeforeRetry];

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
                var sightings = new Sightings();
                start.RegisterOperationAction(operation => Analyze(operation, models, sightings), OperationKind.Invocation);
            }
        });
    }

    /// <summary>Whether a method is the retry.</summary>
    /// <param name="method">The method.</param>
    /// <returns>Whether the library declares it under this name, on whichever type.</returns>
    internal static bool IsRetry(IMethodSymbol method) => method.Name == RetryMethod && DynaSharpTypes.IsDeclaredByDynaSharp(method.ContainingType);

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models, Sightings sightings)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (Repeats(invocation))
        {
            foreach (var method in MethodsAround(context))
            {
                sightings.RetryIn(context, method, LocationOf(invocation));
            }

            if (RunOf(invocation) is { } run)
            {
                ReportNested(context, run, sightings);
                if (RepeatsFailedConditions(invocation))
                {
                    ReportFixedVersions(context, invocation, run, models, sightings);
                }
            }
        }
    }

    // One run allows no second look, so nothing in it can multiply or stay stale from run to run.
    private static bool Repeats(IInvocationOperation invocation) =>
        IsRetry(invocation.TargetMethod)
        && !invocation.Arguments.Any(static argument => argument.Parameter!.Name == MaxAttemptsParameter && argument.Value.ConstantValue is { HasValue: true, Value: 1 });

    // A retry that leaves a failed condition to the caller surfaces a stale version at once, which is what the version is there for, so it runs nothing again for the same old value. A value
    // that is not a constant may name it.
    private static bool RepeatsFailedConditions(IInvocationOperation retry) =>
        !retry.Arguments.Any(static argument => argument.Parameter!.Name == RetryOnParameter && argument.Value.ConstantValue is { HasValue: true, Value: int kinds } && (kinds & ConditionalCheckFailed) == 0);

    // A delegate in a variable and a method of a class are not followed: only a lambda, an anonymous method and a local function are in sight of the call.
    private static IBlockOperation? RunOf(IInvocationOperation invocation) => invocation.Arguments.Single(static argument => argument.Parameter!.Name == OperationParameter).Value switch
    {
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda.Body,
        IDelegateCreationOperation { Target: IMethodReferenceOperation { Method: { MethodKind: MethodKind.LocalFunction } function } } =>
            ((ILocalFunctionOperation)invocation.SemanticModel!.GetOperation(function.DeclaringSyntaxReferences[0].GetSyntax())!).Body,
        _ => null,
    };

    // A retry in a local function is in the method around it too, as a reader sees it. A lambda has no name to call it by, so what is in one counts for the function around that.
    private static IEnumerable<IMethodSymbol> MethodsAround(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is IMethodSymbol member)
        {
            yield return DeclarationOf(member);
        }

        for (var around = context.Operation.Parent; around is not null; around = around.Parent)
        {
            if (around is ILocalFunctionOperation function)
            {
                yield return DeclarationOf(function.Symbol);
            }
        }
    }

    // A generic method is called in a constructed form, and a partial one has two symbols.
    private static IMethodSymbol DeclarationOf(IMethodSymbol method) => (method.PartialDefinitionPart ?? method).OriginalDefinition;

    private static Location LocationOf(IInvocationOperation retry) => ((InvocationExpressionSyntax)retry.Syntax).Expression.GetLocation();

    private static void ReportNested(OperationAnalysisContext context, IOperation run, Sightings sightings)
    {
        foreach (var call in run.Descendants().OfType<IInvocationOperation>())
        {
            if (Repeats(call))
            {
                sightings.Report(context, Descriptors.NestedConflictRetry, LocationOf(call));
            }
            else
            {
                sightings.CalledByARun(context, DeclarationOf(call.TargetMethod));
            }
        }
    }

    private static void ReportFixedVersions(OperationAnalysisContext context, IInvocationOperation retry, IBlockOperation run, ModelCatalog models, Sightings sightings)
    {
        DataFlowAnalysis? flow = null;
        foreach (var call in run.Descendants().OfType<IInvocationOperation>())
        {
            if (VersionOf(call, models) is { } version
                && OriginOf(version.Value, version.Property) is var origin and (ILocalReferenceOperation or IParameterReferenceOperation)
                && VariableOf(origin) is var variable)
            {
                flow ??= retry.SemanticModel!.AnalyzeDataFlow(RegionOf(run));
                if (flow.Captured.Contains(variable, SymbolEqualityComparer.Default)
                    && !flow.WrittenInside.Contains(variable, SymbolEqualityComparer.Default)
                    && !IsDeclaredIn(run, variable)
                    && !MayChange(variable, run)
                    && HoldsOldVersion(variable, version.Property, version.Unknown, retry, follow: true))
                {
                    sightings.Report(context, Descriptors.VersionReadBeforeRetry, origin.Syntax.GetLocation(), variable.Name);
                }
            }
        }
    }

    // The body of a lambda is a block or an expression, and the block of a local function with an arrow stands on the arrow, which is no region.
    private static SyntaxNode RegionOf(IBlockOperation run) => run.Syntax is ArrowExpressionClauseSyntax arrow ? arrow.Expression : run.Syntax;

    // Captured names a parameter of a lambda inside the delegate as well, when a lambda inside that uses it.
    private static bool IsDeclaredIn(IBlockOperation run, ISymbol variable) => run.Syntax.Span.Contains(variable.Locations[0].SourceSpan);

    private static ISymbol VariableOf(IOperation reference) => reference is ILocalReferenceOperation local ? local.Local : ((IParameterReferenceOperation)reference).Parameter;

    // What a write takes its version from. A delete by entity cannot mean a new item, which version 0 would not delete, so it counts what a put does not, and IfVersion and IgnoreVersion
    // take the version of the entity out of a chain. A batch writes nothing with a version, and a delete in a transaction goes by key.
    private static WrittenVersion? VersionOf(IInvocationOperation call, ModelCatalog models)
    {
        if (!DynaSharpTypes.IsDeclaredByDynaSharp(call.TargetMethod.ContainingType))
        {
            return null;
        }

        if (call.TargetMethod.Name == IfVersion)
        {
            return new WrittenVersion(call.Arguments.Single(static argument => argument.Parameter!.Name == VersionParameter).Value, null, true);
        }

        if (ItemWrites.Contains(call.TargetMethod.Name)
            && ItemWriters.Contains(call.TargetMethod.ContainingType.Name)
            && call.Arguments.FirstOrDefault(static argument => argument.Parameter!.Name is "item" or "entity") is { } item
            && item.Parameter!.Type is INamedTypeSymbol type
            && models.ModelOf(type)?.Entity?.VersionProperty is { } property
            && !EntityCommandAnalyzer.ChainCalls(call, IgnoreVersion)
            && !EntityCommandAnalyzer.ChainCalls(call, IfVersion))
        {
            return new WrittenVersion(item.Value, property, call.TargetMethod.Name == Delete);
        }

        return null;
    }

    // Where a value starts: order.Revision, order!, order with { Total = 1 } and an awaited read each start at what they read. A with that sets the version starts at what it sets.
    private static IOperation OriginOf(IOperation value, string? property)
    {
        while (true)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    value = conversion.Operand;
                    break;
                case IMemberReferenceOperation { Instance: { } instance }:
                    value = instance;
                    break;
                case IWithOperation copy:
                    value = VersionSetBy(copy, property) ?? copy.Operand;
                    break;
                case IAwaitOperation awaited:
                    value = awaited.Operation;
                    break;
                case ICoalesceOperation coalesce:
                    value = coalesce.Value;
                    break;
                case IInvocationOperation { TargetMethod.Name: ConfigureAwait, Instance: { } awaitable }:
                    value = awaitable;
                    break;
                default:
                    return value;
            }
        }
    }

    private static IOperation? VersionSetBy(IWithOperation copy, string? property) => copy.Initializer.Initializers
        .OfType<ISimpleAssignmentOperation>()
        .Where(assignment => ((IMemberReferenceOperation)assignment.Target).Member.Name == property)
        .Select(static assignment => assignment.Value)
        .FirstOrDefault();

    // A run can bring a variable up to date without assigning it: through a member, or by handing it to code that can. A write of the library hands it over too, and changes nothing.
    private static bool MayChange(ISymbol variable, IBlockOperation run) =>
        run.Descendants().Any(operation => CommandFlow.IsReferenceTo(operation, variable) && MayChange(operation));

    private static bool MayChange(IOperation reference)
    {
        var value = reference;
        while (value.Parent is IMemberReferenceOperation member)
        {
            value = member;
        }

        return value.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == value,
            IIncrementOrDecrementOperation => true,
            IInvocationOperation call => call.Instance == value,
            IArgumentOperation { Parent: IInvocationOperation call } => value == reference && !IsLibrary(call),
            _ => false,
        };
    }

    private static bool IsLibrary(IInvocationOperation call) => DynaSharpTypes.IsDeclaredByDynaSharp(call.TargetMethod.ContainingType);

    // Whether what is in a variable is a version from before the retry, by what it was assigned. A local function or a lambda that assigns it may do so during a run. One more local is followed, which
    // is as far as `var version = order.Revision;` goes.
    private static bool HoldsOldVersion(ISymbol variable, string? property, bool unknown, IInvocationOperation retry, bool follow)
    {
        if (variable is not ILocalSymbol local)
        {
            return unknown;
        }

        var holds = false;
        foreach (var definition in DefinitionsOf(local, retry))
        {
            if (RunsLater(definition, retry))
            {
                return false;
            }

            holds |= OriginOf(definition, property) switch
            {
                IInvocationOperation call when IsLibrary(call) => true,
                ILocalReferenceOperation other when follow => HoldsOldVersion(other.Local, property, unknown, retry, follow: false),
                IObjectCreationOperation => false,
                { ConstantValue.HasValue: true } => false,
                _ => unknown,
            };
        }

        return holds;
    }

    private static bool RunsLater(IOperation definition, IInvocationOperation retry) =>
        definition.Syntax.Ancestors().FirstOrDefault(static node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) is { } function && !function.Span.Contains(retry.Syntax.Span);

    private static IEnumerable<IOperation> DefinitionsOf(ILocalSymbol local, IOperation retry)
    {
        var member = retry;
        while (member.Parent is { } parent)
        {
            member = parent;
        }

        foreach (var operation in member.Descendants())
        {
            var value = operation switch
            {
                IVariableDeclaratorOperation { Initializer: { } initializer } declarator when Same(declarator.Symbol, local) => initializer.Value,
                ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment when Same(target.Local, local) => assignment.Value,
                IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation control } loop when Same(control.Symbol, local) => loop.Collection,
                IIsPatternOperation check when Declares(check.Pattern, local) => check.Value,
                _ => null,
            };

            if (value is not null)
            {
                yield return value;
            }
        }
    }

    private static bool Declares(IPatternOperation pattern, ILocalSymbol local) => pattern.DescendantsAndSelf().Any(operation => operation switch
    {
        IDeclarationPatternOperation declaration => Same(declaration.DeclaredSymbol, local),
        IRecursivePatternOperation recursive => Same(recursive.DeclaredSymbol, local),
        _ => false,
    });

    private static bool Same(ISymbol? one, ISymbol other) => SymbolEqualityComparer.Default.Equals(one, other);

    /// <summary>Where a write of a retry delegate takes its version from.</summary>
    /// <param name="Value">The argument.</param>
    /// <param name="Property">The property that holds the version when the argument is an item, or null when the argument is the version.</param>
    /// <param name="Unknown">Whether a value that this analyzer cannot trace counts as a version from before the retry.</param>
    private readonly record struct WrittenVersion(IOperation Value, string? Property, bool Unknown);

    /// <summary>
    /// What one compilation has seen so far. A retry in a method that a run calls takes two sightings, of the retry and of the call, which come in no order and on any thread, so the second one reports. Every
    /// invocation of the compilation passes here anyway, so this asks for no second semantic model, which RS1030 forbids, and it cannot meet a method of another compilation. The same finding can come
    /// from several sides: a retry in a retry in a retry from both around it, a method from each retry that calls it.
    /// </summary>
    private sealed class Sightings
    {
        private readonly ConcurrentDictionary<Location, bool> _reported = new();
        private readonly Dictionary<IMethodSymbol, List<Location>> _retries = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _calledByARun = new(SymbolEqualityComparer.Default);

        public void Report(OperationAnalysisContext context, DiagnosticDescriptor descriptor, Location location, params object[] arguments)
        {
            if (_reported.TryAdd(location, true))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));
            }
        }

        public void RetryIn(OperationAnalysisContext context, IMethodSymbol method, Location location)
        {
            bool called;
            lock (_retries)
            {
                if (!_retries.TryGetValue(method, out var locations))
                {
                    _retries[method] = locations = [];
                }

                locations.Add(location);
                called = _calledByARun.Contains(method);
            }

            if (called)
            {
                Report(context, Descriptors.NestedConflictRetry, location);
            }
        }

        public void CalledByARun(OperationAnalysisContext context, IMethodSymbol method)
        {
            Location[] locations;
            lock (_retries)
            {
                _calledByARun.Add(method);
                locations = _retries.TryGetValue(method, out var seen) ? [.. seen] : [];
            }

            foreach (var location in locations)
            {
                Report(context, Descriptors.NestedConflictRetry, location);
            }
        }
    }
}
