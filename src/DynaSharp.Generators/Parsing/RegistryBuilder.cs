using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.Generators;

/// <summary>
/// Puts the entities of an assembly together for <c>IncludingAll()</c>, which <c>CreateTable()</c> needs so that an entity cannot be forgotten. It exists only where it cannot be wrong at
/// compile time: every entity has the keys of one table, as <c>Including&lt;T&gt;</c> demands, and code outside the types can name them all.
/// </summary>
internal static class RegistryBuilder
{
    /// <summary>The name of the generated class.</summary>
    public const string ClassName = "DynamoDbEntities";

    /// <summary>What the registry takes of an entity.</summary>
    /// <param name="type">The model of the entity.</param>
    /// <returns>The entry.</returns>
    public static RegisteredEntity Entry(TypeModel type) =>
        new(type.FullName, type.Entity!.PartitionKey.AttributeName, type.Entity.SortKey?.AttributeName, type.Reach, type.Entity.TableName, MetadataName(type));

    /// <summary>Finds the namespace of the class and what in the assembly takes a name it needs. It runs on every edit, and its few lookups are by name.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The home.</returns>
    public static AssemblyHome Home(Compilation compilation)
    {
        var segments = NamespaceSegments(compilation.AssemblyName ?? string.Empty);

        // A type of the name of a part of the namespace clashes with the namespace, and one of the name of the class with the class (CS0101 either way).
        var taken = new List<string>();
        for (var count = 1; count <= segments.Count; count++)
        {
            taken.Add(string.Join(".", segments.Take(count)));
        }

        taken.Add(taken[taken.Count - 1] + "." + ClassName);
        var @namespace = string.Join(".", segments.Select(Names.Identifier));
        foreach (var name in taken)
        {
            if (compilation.Assembly.GetTypeByMetadataName(name) is { } type)
            {
                var spot = Spot.Of(type, new Spot(string.Empty, default));
                return new AssemblyHome(@namespace, new DiagnosticInfo(Descriptors.NamedLikeEntitiesClass.Id, new EquatableArray<string>([type.ToDisplayString(), name]), spot.FilePath, spot.Span));
            }
        }

        return new AssemblyHome(@namespace, null);
    }

    /// <summary>Decides whether the class exists, and says why when it does not.</summary>
    /// <param name="entities">The entities of the assembly, in any order.</param>
    /// <param name="home">The namespace.</param>
    /// <returns>The registry.</returns>
    public static EntityRegistry Build(ImmutableArray<RegisteredEntity> entities, AssemblyHome home)
    {
        if (entities.IsEmpty)
        {
            return new EntityRegistry(null, default);
        }

        var ordered = entities.OrderBy(static entity => entity.FullName, StringComparer.Ordinal).ToList();
        var conflicts = KeyConflicts(ordered);
        if (ordered.Find(static entity => entity.Reach < Accessibility.Internal) is { } hidden)
        {
            return Without($"'{hidden.DisplayName}' is private to a type around it, so no class outside that type can name it", conflicts);
        }

        var tables = ordered.GroupBy(static entity => (entity.PartitionKey, entity.SortKey)).ToList();
        if (tables.Count > 1)
        {
            var keys = tables.Select(static table => $"{Describe(table.Key.PartitionKey, table.Key.SortKey)} of {table.First().DisplayName}");
            return Without($"they declare the keys of more than one table, {string.Join(", ", keys)}", conflicts);
        }

        if (home.Clash is { } clash)
        {
            return new EntityRegistry(null, new EquatableArray<DiagnosticInfo>([clash]));
        }

        var accessibility = ordered.TrueForAll(static entity => entity.Reach == Accessibility.Public) ? "public" : "internal";
        return new EntityRegistry(new EntitiesClass(home.Namespace, accessibility, new EquatableArray<string>([.. ordered.Select(static entity => entity.FullName)])), default);
    }

    /// <summary>
    /// <c>DYNA040</c>: the entities of a declared table whose keys are not those that most of its entities have, each against the first entity with those keys. A tie goes to the keys of the entity
    /// first by name, so that the report does not change with the order of the files.
    /// </summary>
    /// <param name="ordered">The entities, by name.</param>
    /// <returns>The warnings.</returns>
    public static List<DiagnosticInfo> KeyConflicts(List<RegisteredEntity> ordered)
    {
        var found = new List<DiagnosticInfo>();
        foreach (var table in ordered.Where(static entity => entity.TableName is not null).GroupBy(static entity => entity.TableName!, StringComparer.Ordinal))
        {
            var schemas = table.GroupBy(static entity => (entity.PartitionKey, entity.SortKey)).ToList();
            if (schemas.Count == 1)
            {
                continue;
            }

            var common = schemas.OrderByDescending(static schema => schema.Count()).First();
            var reference = common.First();
            foreach (var entity in schemas.Where(schema => schema != common).SelectMany(static schema => schema))
            {
                var arguments = new EquatableArray<string>([entity.DisplayName, table.Key, Describe(entity.PartitionKey, entity.SortKey), reference.DisplayName, Describe(reference.PartitionKey, reference.SortKey)]);
                found.Add(new DiagnosticInfo(Descriptors.TableKeysDiffer.Id, arguments, string.Empty, default, entity.MetadataName));
            }
        }

        return found;
    }

    /// <summary>
    /// The parts of the namespace an assembly name gives, each a valid name: <c>Lps.LabelPrintServer</c> stays, <c>My-App.2</c> gives <c>My_App</c> and <c>_2</c>. A keyword keeps its text and
    /// needs an <c>@</c> as code, which <see cref="Names.Identifier"/> adds.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly.</param>
    /// <returns>The parts, at least one.</returns>
    public static IReadOnlyList<string> NamespaceSegments(string assemblyName) => [.. assemblyName.Split('.').Select(Segment)];

    private static string Segment(string text)
    {
        var segment = new StringBuilder();
        foreach (var character in text)
        {
            segment.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
        }

        return segment.Length > 0 && SyntaxFacts.IsIdentifierStartCharacter(segment[0]) ? segment.ToString() : "_" + segment.ToString();
    }

    private static string Describe(string partitionKey, string? sortKey) => sortKey is null ? $"'{partitionKey}'" : $"'{partitionKey}' and '{sortKey}'";

    private static EntityRegistry Without(string reason, List<DiagnosticInfo> conflicts) =>
        new(null, new EquatableArray<DiagnosticInfo>([.. conflicts, new DiagnosticInfo(Descriptors.NoIncludingAll.Id, new EquatableArray<string>([reason]), string.Empty, default)]));

    // Shop.Outer+Order: the namespace without the @ of a keyword, and the types around it by their names.
    private static string MetadataName(TypeModel type)
    {
        var name = string.Join("+", type.ContainingTypes.Select(static outer => outer.Identifier.TrimStart('@')).Append(type.Name));
        return type.Namespace is null ? name : type.Namespace.Replace("@", string.Empty) + "." + name;
    }
}
