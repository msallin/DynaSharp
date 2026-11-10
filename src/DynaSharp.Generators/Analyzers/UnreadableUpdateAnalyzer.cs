using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA241: an <c>Update</c> by key of an entity with members a read requires, which creates the item when none has the key and then leaves those members out, so every typed read of the item throws
/// <c>DynamoDbMappingException</c>. A member is required when <see cref="MissingBehavior.Throw"/> says so and the key of the table does not give it back, by its attribute, <c>[KeyOnly]</c> or the fallback of a
/// stored input; <c>StoreKeyInputs()</c> writes the other inputs of that key. It counts as written when a <c>Set</c>, <c>SetIfNotNull</c>, <c>SetIfNotExists</c> or <c>Add</c> of the chain names it, by its
/// handle, a constant path, or a handle or an <c>Attr</c> in the text of an expression.
/// </summary>
/// <remarks>
/// <para>
/// <c>IfExists()</c> keeps the update from creating the item, and so does <c>IfVersion(n)</c> but with the constant 0, which stands for an item that does not exist. A condition of its own, <c>If(...)</c>, may
/// say the same and is not looked into. <c>IfNotExists()</c> and <c>IfVersion(0)</c> create the item on purpose: the diagnostic stands, without the code fix, which would contradict them.
/// </para>
/// <para>
/// A command is decided as <c>DYNA239</c> decides it, by the calls on a command of its type anywhere in the member, and is not reported when it leaves the member. A step is decided by its own calls up to the
/// next verb, and is not reported when it goes into a local or a method of the consumer before that.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class UnreadableUpdateAnalyzer : DiagnosticAnalyzer
{
    private const string UpdateVerb = "Update";

    private const string TableClass = "Table";

    private const string StepVerbs = "TransactWriteExtensions";

    private const string HandlesClass = "Attrs";

    private const string PathParameter = "path";

    private const string IfExistsClause = "IfExists()";

    // The writes that leave a value in the attribute of an item that did not exist.
    private static readonly HashSet<string> Writes = ["Set", "SetIfNotNull", "SetIfNotExists", "Add"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.UpdateCreatesUnreadableItem];

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
        var method = invocation.TargetMethod;
        if (method.Name != UpdateVerb || !DynaSharpTypes.IsDeclaredByDynaSharp(method.ContainingType) || !EntityCommandAnalyzer.IsWriteByKey(method))
        {
            return;
        }

        var isCommand = method.ContainingType.Name == TableClass;
        var entity = isCommand ? method.ContainingType.TypeArguments[0] : DynaSharpTypes.TableTypeOf(method);
        if (entity is not INamedTypeSymbol type || models.ModelOf(type) is not { Entity: { } entityModel } model || Required(model, entityModel).ToList() is not { Count: > 0 } required)
        {
            return;
        }

        if ((isCommand ? CommandCalls(invocation) : StepCalls(invocation)) is not { } calls)
        {
            return;
        }

        var creates = false;
        var storesKeyInputs = false;
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            switch (call.TargetMethod.Name)
            {
                case "IfExists" or "If":
                    return;
                case "IfVersion" when call.Arguments[call.Arguments.Length - 1].Value.ConstantValue is not { HasValue: true, Value: 0L }:
                    return;
                case "IfVersion" or "IfNotExists":
                    creates = true;
                    break;
                case "StoreKeyInputs":
                    storesKeyInputs = true;
                    break;
                case var write when Writes.Contains(write):
                    written.UnionWith(Named(call, model));
                    break;
            }
        }

        var missing = required.Where(property => !written.Contains(property.Name) && !(storesKeyInputs && EntityCommandAnalyzer.FeedsTableKey(entityModel, property.Name))).Select(static property => "'" + property.Name + "'").ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var syntax = (InvocationExpressionSyntax)invocation.Syntax;
        List<SuggestedEdit> edits = creates ? [] : [new SuggestedEdit("Add " + IfExistsClause, new TextSpan(syntax.Span.End, 0), EntityCommandAnalyzer.NextCall(syntax, IfExistsClause))];
        var name = ((MemberAccessExpressionSyntax)syntax.Expression).Name;
        context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.UpdateCreatesUnreadableItem, name.GetLocation(), edits, Join(missing), type.Name));
    }

    // The members whose attribute an item that an update created lacks and a read of the entity throws for. The update writes the key of the table and nothing else of the item by itself.
    private static IEnumerable<PropertyModel> Required(TypeModel model, EntityModel entity)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { entity.PartitionKey.AttributeName };
        if (entity.SortKey is { } sortKey)
        {
            keys.Add(sortKey.AttributeName);
        }

        return model.Properties.Where(property => property.Missing == MissingBehavior.Throw && !property.IsWriteOnly && !keys.Contains(property.RestoredFromKey ?? property.FallbackKey ?? property.AttributeName));
    }

    // The calls on a command of this type anywhere in the member, as DYNA239 takes them, or null for a command that leaves the member, where the code that gets it can still add a clause.
    private static IEnumerable<IInvocationOperation>? CommandCalls(IInvocationOperation update)
    {
        if (EntityCommandAnalyzer.LeavesMember(update))
        {
            return null;
        }

        IOperation root = update;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        return root.Descendants().OfType<IInvocationOperation>().Where(call => SymbolEqualityComparer.Default.Equals(call.Type, update.Type));
    }

    // The calls on the step itself, up to the next verb or the end of the transaction, or null for a step that goes into a local or a method of the consumer before that.
    private static List<IInvocationOperation>? StepCalls(IInvocationOperation verb)
    {
        var calls = new List<IInvocationOperation>();
        IOperation current = verb;
        while (EntityCommandAnalyzer.NextCall(current) is { } next)
        {
            if (!DynaSharpTypes.IsDeclaredByDynaSharp(next.TargetMethod.ContainingType))
            {
                return null;
            }

            // A verb takes a table; IfExists and IfNotExists of a step are extension methods beside the verbs and take none.
            if (!SymbolEqualityComparer.Default.Equals(next.Type, verb.Type) || (next.TargetMethod.ContainingType.Name == StepVerbs && DynaSharpTypes.TableTypeOf(next.TargetMethod) is not null))
            {
                return calls;
            }

            calls.Add(next);
            current = next;
        }

        return null;
    }

    // The members a write names: by a handle, also inside the text of an expression, and by a constant path, also of an Attr in the text.
    private static IEnumerable<string> Named(IInvocationOperation write, TypeModel model)
    {
        foreach (var operation in write.Arguments.SelectMany(static argument => argument.DescendantsAndSelf()))
        {
            if (operation is IPropertyReferenceOperation { Property: { ContainingType.Name: HandlesClass } handle })
            {
                yield return handle.Name;
            }
            else if (operation is IArgumentOperation { Parameter.Name: PathParameter, Value.ConstantValue: { HasValue: true, Value: string path } } && PathSyntax.IsValid(path))
            {
                var root = PathSyntax.Root(path);
                foreach (var property in model.Properties.Where(property => property.AttributeName == root))
                {
                    yield return property.Name;
                }
            }
        }
    }

    // 'Status', 'Status' and 'Total', 'Status', 'Total' and 'Placed'.
    private static string Join(List<string> names) =>
        names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
}
