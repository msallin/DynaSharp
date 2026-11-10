using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// The one value a variable can hold where it is read, for a rule that follows what a chain starts from: <c>var byStatus = orders.Index(Order.Indexes.ByStatus); byStatus.Query(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A local counts when its declaration is the only write to it in the member: no assignment, no <c>ref</c> or <c>out</c> argument, no tuple it could be deconstructed into, and no <c>ref</c> local
/// in the member. A <c>readonly</c> field and a get-only auto-property count when exactly one write reaches them, their initializer or one assignment in a constructor of their type, and a get-only
/// property with an expression body counts by that expression. Whatever else is no such value: a field that is not <c>readonly</c>, a property with a setter or an <c>init</c>, one that can be overridden, one declared in
/// parts, and a member of a type declared in another file, which the semantic model of this one cannot read.
/// </para>
/// </remarks>
internal static class OnlyValue
{
    /// <summary>The one value of what a reference reads, or the operation itself where it is no such variable.</summary>
    /// <param name="operation">The operation, such as the receiver of a call.</param>
    /// <returns>The value the variable was given, the operation where it reads no variable, or null where the variable can hold another value.</returns>
    public static IOperation? Of(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => OfLocal(local),
        IFieldReferenceOperation { Field.IsReadOnly: true } field => OfMember(field.Field, field),
        IPropertyReferenceOperation { Property: { SetMethod: null, IsAbstract: false, IsVirtual: false, IsOverride: false } property } reference => OfProperty(property, reference),
        IFieldReferenceOperation or IPropertyReferenceOperation => null,
        _ => operation,
    };

    private static IOperation? OfLocal(ILocalReferenceOperation reference)
    {
        var local = reference.Local;
        IOperation? value = null;
        foreach (var operation in RootOf(reference).Descendants())
        {
            // A ref local can write any local it refers to, which is more than this follows.
            if (operation is IVariableDeclaratorOperation { Symbol.IsRef: true })
            {
                return null;
            }

            if (operation is IVariableDeclaratorOperation { Initializer: { } initializer } declarator && Same(declarator.Symbol, local))
            {
                value = initializer.Value;
            }
            else if (operation is ILocalReferenceOperation other && Same(other.Local, local) && Writes(other))
            {
                return null;
            }
        }

        return value;
    }

    private static IOperation? OfProperty(IPropertySymbol property, IPropertyReferenceOperation reference)
    {
        var references = property.DeclaringSyntaxReferences;
        if (references.Length != 1 || references[0].SyntaxTree != reference.Syntax.SyntaxTree || references[0].GetSyntax() is not PropertyDeclarationSyntax declaration)
        {
            return null;
        }

        if (declaration.ExpressionBody is { } body)
        {
            return reference.SemanticModel!.GetOperation(body.Expression);
        }

        // A property without a setter has one accessor, get, which an auto-property declares without a body.
        return declaration.AccessorList!.Accessors[0] is { Body: null, ExpressionBody: null } ? OfMember(property, reference) : null;
    }

    // The initializer and the writes in the constructors of the type, all of which a semantic model of this file sees only when the type is declared in it alone.
    private static IOperation? OfMember(ISymbol member, IOperation reference)
    {
        var tree = reference.Syntax.SyntaxTree;
        var model = reference.SemanticModel!;
        var declared = member.DeclaringSyntaxReferences[0];
        if (declared.SyntaxTree != tree || member.ContainingType.DeclaringSyntaxReferences.Any(type => type.SyntaxTree != tree))
        {
            return null;
        }

        var values = new List<IOperation>();
        if (Initializer(declared.GetSyntax()) is { } initializer)
        {
            values.Add(model.GetOperation(initializer.Value)!);
        }

        foreach (var type in member.ContainingType.DeclaringSyntaxReferences)
        {
            foreach (var constructor in type.GetSyntax().ChildNodes().OfType<ConstructorDeclarationSyntax>().Select(syntax => model.GetOperation(syntax)).OfType<IOperation>())
            {
                foreach (var write in constructor.Descendants().Where(operation => IsReferenceTo(operation, member) && Writes(operation)))
                {
                    if (write.Parent is not ISimpleAssignmentOperation assignment)
                    {
                        return null;
                    }

                    values.Add(assignment.Value);
                }
            }
        }

        return values.Count == 1 ? values[0] : null;
    }

    private static EqualsValueClauseSyntax? Initializer(SyntaxNode declaration) => declaration switch
    {
        VariableDeclaratorSyntax field => field.Initializer,
        _ => ((PropertyDeclarationSyntax)declaration).Initializer,
    };

    private static bool IsReferenceTo(IOperation operation, ISymbol member) => operation switch
    {
        IFieldReferenceOperation field => Same(field.Field, member),
        IPropertyReferenceOperation property => Same(property.Property, member),
        _ => false,
    };

    // Whether a reference stands where a value goes in: the target of an assignment, a ref or out argument, or a tuple that can be deconstructed into.
    private static bool Writes(IOperation reference)
    {
        var parent = reference.Parent;
        if (parent is IAssignmentOperation assignment)
        {
            return assignment.Target == reference;
        }

        if (parent is IArgumentOperation argument)
        {
            // Only the argument of __arglist has no parameter, and its value is the list, never a reference.
            return argument.Parameter!.RefKind is RefKind.Ref or RefKind.Out;
        }

        return parent is ITupleOperation;
    }

    private static IOperation RootOf(IOperation operation)
    {
        while (operation.Parent is { } parent)
        {
            operation = parent;
        }

        return operation;
    }

    private static bool Same(ISymbol? one, ISymbol other) => SymbolEqualityComparer.Default.Equals(one, other);
}
