using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// Looks through what a tracked pipeline step produced for anything that defeats incremental caching: a symbol, syntax or
/// compilation (they change on every edit and keep the old compilation alive), a location bound to a tree, or a collection
/// other than an <c>EquatableArray&lt;T&gt;</c>, because arrays, lists and immutable arrays compare by reference.
/// </summary>
internal static class TrackedOutputWalker
{
    private static readonly Type[] CompilationBound =
    [
        typeof(ISymbol), typeof(Compilation), typeof(SyntaxNode), typeof(SyntaxTree), typeof(SemanticModel), typeof(MetadataReference),
    ];

    /// <summary>One line per offending member, as a path from the root; empty when the graph is clean.</summary>
    public static IReadOnlyList<string> Violations(object? root)
    {
        List<string> found = [];
        HashSet<object> seen = [with(ReferenceEqualityComparer.Instance)];
        Walk(root, "output", found, seen);
        return found;
    }

    private static void Walk(object? value, string path, List<string> found, HashSet<object> seen)
    {
        if (value is null or string)
        {
            return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        if (CompilationBound.FirstOrDefault(bound => bound.IsInstanceOfType(value)) is { } bad)
        {
            found.Add($"{path}: {type.Name} is a {bad.Name}");
        }
        else if (value is Location { SourceTree: not null })
        {
            found.Add($"{path}: {type.Name} is bound to a syntax tree");
        }
        else if (value is IEnumerable elements)
        {
            WalkCollection(elements, type, path, found, seen);
        }
        else if (IsOurs(type) && seen.Add(value))
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Walk(field.GetValue(value), $"{path}.{field.Name}", found, seen);
            }
        }
    }

    private static void WalkCollection(IEnumerable elements, Type type, string path, List<string> found, HashSet<object> seen)
    {
        if (type.Name != "EquatableArray`1")
        {
            found.Add($"{path}: {type.Name} compares by reference, wrap it in EquatableArray<T>");
            return;
        }

        var index = 0;
        foreach (var element in elements)
        {
            Walk(element, $"{path}[{index++}]", found, seen);
        }
    }

    // Records, structs and the tuples that Combine produces. Other BCL and Roslyn types are not taken apart.
    private static bool IsOurs(Type type) =>
        type.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal)
        || type.FullName.StartsWith("DynaSharp.", StringComparison.Ordinal);
}
