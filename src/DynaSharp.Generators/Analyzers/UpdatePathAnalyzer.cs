using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA223: two actions of one update on the same path or on paths that overlap, <c>Set(Order.Attrs.Total, 1).Add(Order.Attrs.Total, 2)</c> or <c>Remove(Order.Attrs.ShipTo).Set(Order.Attrs.ShipTo.At(Address.Attrs.City), c)</c>,
/// which DynamoDB rejects. Paths that only share a parent, <c>ShipTo.City</c> and <c>ShipTo.Street</c>, are fine. The writer sends every action as it comes, so such a chain always holds both, except a
/// <c>SetIfNotNull</c> of a constant <see langword="null"/>, which adds nothing.
/// </summary>
/// <remarks>
/// A chain is read once, at its last call: <c>Update(key)</c> to its end, or a step of a transaction from its <c>Update</c> to the next step. A path is a handle, with <c>At</c>, a constant string, or
/// <c>Attr(...)</c> of one. An action of <c>Set($"...")</c>, a chain through a variable and a handle of a type without a model are not looked at.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class UpdatePathAnalyzer : DiagnosticAnalyzer
{
    private const string SetIfNotNull = "SetIfNotNull";

    private const string StepVerbs = "TransactWriteExtensions";

    private const string HandlesClass = "Attrs";

    private const string NestedPath = "At";

    private const string AttrType = "Attr";

    // The commands of an update, and the actions on them that take a handle or a path first. Set($"...") takes an action, which is not followed.
    private static readonly HashSet<string> Updates = ["UpdateCommand", "UpdateStep"];

    private static readonly HashSet<string> PathParameters = [EntityCommandAnalyzer.HandleParameter, "path"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.OverlappingUpdatePaths];

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

    /// <summary>Whether the next call is on the same update: another action or option of the command, but not the verb of the next step of a transaction, which has the same type.</summary>
    /// <param name="invocation">A call that returns an update.</param>
    /// <returns>Whether the update goes on after it.</returns>
    internal static bool GoesOn(IInvocationOperation invocation) => invocation.Parent switch
    {
        IInvocationOperation next when next.Instance == invocation => SymbolEqualityComparer.Default.Equals(next.Type, invocation.Type),
        IArgumentOperation { Parent: IInvocationOperation next } argument when next.TargetMethod.IsExtensionMethod && next.Arguments[0] == argument
            => SymbolEqualityComparer.Default.Equals(next.Type, invocation.Type) && !IsStepVerb(next),
        _ => false,
    };

    /// <summary>Whether a call is a verb that starts a step of a transaction.</summary>
    /// <param name="call">The call.</param>
    /// <returns>Whether it is declared by <c>TransactWriteExtensions</c>.</returns>
    internal static bool IsStepVerb(IInvocationOperation call) => call.TargetMethod.ContainingType.Name == StepVerbs;

    /// <summary>The value a call is made on. A static method of the consumer under the namespace of the library counts as the library, and it has no receiver.</summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The instance, the first argument of an extension method, or null.</returns>
    internal static IOperation? ReceiverOf(IInvocationOperation invocation) => invocation.Instance ?? (invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments[0].Value : null);

    /// <summary>The argument of a parameter. Arguments come in the order of the source, which a named argument can change.</summary>
    /// <param name="call">The call.</param>
    /// <param name="ordinal">The position of the parameter.</param>
    /// <returns>The value of the argument.</returns>
    internal static IOperation ArgumentOf(IInvocationOperation call, int ordinal) => call.Arguments.First(argument => argument.Parameter!.Ordinal == ordinal).Value;

    /// <summary>Whether a value counts as null for a handle: a <see langword="null"/>, and an empty string where <c>[OmitEmpty]</c> counts it as null.</summary>
    /// <param name="value">The value of the action.</param>
    /// <param name="attribute">The handle of the action.</param>
    /// <param name="models">The models of the compilation.</param>
    /// <returns>Whether a <c>SetIfNotNull</c> of it writes nothing, and a <c>Set</c> of it removes the attribute.</returns>
    internal static bool AddsNothing(IOperation value, IOperation attribute, ModelCatalog models) => value switch
    {
        { ConstantValue: { HasValue: true, Value: null } } => true,
        { ConstantValue: { HasValue: true, Value: "" } } or IFieldReferenceOperation { Field: { Name: nameof(string.Empty), ContainingType.SpecialType: SpecialType.System_String } } =>
            PropertyOf(attribute, models)?.OmitEmptyConverter is not null,
        _ => false,
    };

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.Type is not INamedTypeSymbol type || !Updates.Contains(type.Name) || !DynaSharpTypes.IsDeclaredByDynaSharp(type) || GoesOn(invocation))
        {
            return;
        }

        // From the end back, so a later action is the one that is reported: it is the one that DynamoDB would see second.
        var actions = new List<(string Path, IInvocationOperation Call)>();
        IOperation? current = invocation;
        while (current is IInvocationOperation call && SymbolEqualityComparer.Default.Equals(call.Type, type) && !IsStepVerb(call))
        {
            if (PathOf(call, models) is { } path)
            {
                actions.Add((path, call));
            }

            current = ReceiverOf(call);
        }

        for (var later = 0; later < actions.Count; later++)
        {
            for (var earlier = later + 1; earlier < actions.Count; earlier++)
            {
                if (Overlap(actions[later].Path, actions[earlier].Path))
                {
                    var (path, call) = actions[later];
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.OverlappingUpdatePaths, ArgumentOf(call, 0).Syntax.GetLocation(), call.TargetMethod.Name, path, actions[earlier].Path));
                    break;
                }
            }
        }
    }

    // The path of an action, or null for a call that is no action, that adds nothing, or whose path is not known here.
    private static string? PathOf(IInvocationOperation call, ModelCatalog models)
    {
        if (!EntityCommandAnalyzer.WriteMethods.Contains(call.TargetMethod.Name) || !PathParameters.Contains(call.TargetMethod.Parameters[0].Name))
        {
            return null;
        }

        var target = ArgumentOf(call, 0);
        return call.TargetMethod.Name == SetIfNotNull && AddsNothing(ArgumentOf(call, 1), target, models) ? null : PathOf(target, models);
    }

    private static PropertyModel? PropertyOf(IOperation handle, ModelCatalog models) =>
        handle is IPropertyReferenceOperation { Property: { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } property } && models.ModelOf(owner) is { } model ? model.Property(property.Name) : null;

    private static string? PathOf(IOperation value, ModelCatalog models) => value switch
    {
        { ConstantValue: { HasValue: true, Value: string path } } => PathSyntax.IsValid(path) ? path : null,
        IConversionOperation conversion => PathOf(conversion.Operand, models),
        IPropertyReferenceOperation => PropertyOf(value, models)?.AttributeName,
        IInvocationOperation { TargetMethod.Name: NestedPath } nested when DynaSharpTypes.IsDeclaredByDynaSharp(nested.TargetMethod.ContainingType) =>
            PathOf(ArgumentOf(nested, 0), models) is { } document && PathOf(ArgumentOf(nested, 1), models) is { } inner ? document + "." + inner : null,
        IInvocationOperation { TargetMethod: { Name: AttrType, ContainingType.Name: "Expr" } } call => PathOf(call.Arguments[0].Value, models),
        IObjectCreationOperation { Type.Name: AttrType, Arguments.Length: 1 } creation => PathOf(creation.Arguments[0].Value, models),
        _ => null,
    };

    // a and a, a and a.b, a and a[1] overlap; a.b and a.c, a[0] and a[1], and ab and a do not.
    private static bool Overlap(string one, string other)
    {
        var shorter = one.Length <= other.Length ? one : other;
        var longer = ReferenceEquals(shorter, one) ? other : one;
        return longer.StartsWith(shorter, StringComparison.Ordinal) && (longer.Length == shorter.Length || longer[shorter.Length] is '.' or '[');
    }
}
