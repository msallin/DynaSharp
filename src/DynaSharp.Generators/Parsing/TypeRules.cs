using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>The rules about a type as a whole, before its members are looked at.</summary>
internal static class TypeRules
{
    private const string KeysClass = "Keys";

    private const string AttrsClass = "Attrs";

    private const string IndexesClass = "Indexes";

    /// <summary>Checks the declaration.</summary>
    /// <returns>False when the type cannot be analyzed further.</returns>
    public static bool Check(GeneratorAttributeSyntaxContext context, INamedTypeSymbol type, MappedKind kind, DiagnosticBag bag, Spot spot)
    {
        var other = kind == MappedKind.Entity ? WellKnownNames.DocumentAttribute : WellKnownNames.EntityAttribute;
        if (AttributeReader.Find(type, other) is not null)
        {
            // Both attributes fire the pipeline, so one of the two runs reports and the other stays silent.
            bag.MarkBroken();
            if (kind == MappedKind.Entity)
            {
                bag.Report(Descriptors.EntityAndDocument, spot, type.Name);
            }

            return false;
        }

        if (Unmappable(type) is { } reason)
        {
            bag.Report(Descriptors.UnmappableType, spot, type.Name, reason);
            return false;
        }

        if (!IsPartial(type))
        {
            bag.Report(Descriptors.NotPartial, spot, type.Name);
        }

        var language = ((CSharpParseOptions)context.TargetNode.SyntaxTree.Options).LanguageVersion;
        if (language < LanguageVersion.CSharp11)
        {
            bag.Report(Descriptors.LanguageVersionTooLow, spot, language.ToDisplayString());
        }

        if (!AnnotatesNullability(context))
        {
            bag.Report(Descriptors.NullableContextDisabled, spot, type.Name);
        }

        // Both kinds get Attrs, because a nested document is addressed through the handles of its own type. Only an entity has keys.
        if (kind == MappedKind.Entity)
        {
            CheckReservedMember(type, KeysClass, bag, spot);
            CheckReservedMember(type, IndexesClass, bag, spot);
        }

        CheckReservedMember(type, AttrsClass, bag, spot);

        // No member can show this: the class would be a member of a type with its own name, and that is CS0542 in the generated file.
        if (HasNameOfGeneratedClass(type, kind))
        {
            bag.Report(Descriptors.NamedLikeGeneratedClass, spot, type.Name);
        }

        return true;
    }

    /// <summary>Whether nullable annotations are enabled where the attributed part of the type is declared.</summary>
    public static bool AnnotatesNullability(GeneratorAttributeSyntaxContext context) =>
        context.SemanticModel.GetNullableContext(context.TargetNode.SpanStart).AnnotationsEnabled();

    public static bool HidesKeys(INamedTypeSymbol type) => HidesInherited(type, KeysClass, WellKnownNames.EntityAttribute);

    public static bool HidesAttrs(INamedTypeSymbol type) =>
        HidesInherited(type, AttrsClass, WellKnownNames.EntityAttribute) || HidesInherited(type, AttrsClass, WellKnownNames.DocumentAttribute);

    // Unlike the other two, a base entity has this class only if it declares an index.
    public static bool HidesIndexes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(IndexesClass).Length > 0 || (AttributeReader.Find(current, WellKnownNames.EntityAttribute) is not null && DeclaresIndex(current)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DeclaresIndex(INamedTypeSymbol type) =>
        AttributeReader.Find(type, WellKnownNames.GlobalIndexKeyAttribute) is not null || AttributeReader.Find(type, WellKnownNames.LocalIndexKeyAttribute) is not null;

    // Which classes a type gets decides this: a document named Keys has no Keys class, and neither has an entity without an index an Indexes class.
    private static bool HasNameOfGeneratedClass(INamedTypeSymbol type, MappedKind kind) => type.Name switch
    {
        AttrsClass => true,
        KeysClass => kind == MappedKind.Entity,
        IndexesClass => kind == MappedKind.Entity && DeclaresIndex(type),
        _ => false,
    };

    private static string? Unmappable(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            return "it is an interface";
        }

        if (type.IsStatic)
        {
            return "it is static";
        }

        if (type.IsAbstract)
        {
            return "it is abstract";
        }

        if (type.IsGenericType)
        {
            return "it is generic";
        }

        if (type.IsRefLikeType)
        {
            return "it is a ref struct";
        }

        return IsFileLocal(type) ? "it is file-local" : null;
    }

    // The generated part goes into a file of its own, and a part of a file-local type in another file is another type. That holds for what is nested in one too.
    private static bool IsFileLocal(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            var isPartial = current.DeclaringSyntaxReferences
                .Any(static reference => ((TypeDeclarationSyntax)reference.GetSyntax()).Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckReservedMember(INamedTypeSymbol type, string name, DiagnosticBag bag, Spot spot)
    {
        foreach (var member in type.GetMembers(name))
        {
            bag.Report(Descriptors.MemberConflict, Spot.Of(member, spot), name, type.Name);
        }
    }

    // A generated class of a base type is not visible in the semantic model of this compilation, so the attribute counts as well.
    private static bool HidesInherited(INamedTypeSymbol type, string name, string generatingAttribute)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (AttributeReader.Find(current, generatingAttribute) is not null || current.GetMembers(name).Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}
