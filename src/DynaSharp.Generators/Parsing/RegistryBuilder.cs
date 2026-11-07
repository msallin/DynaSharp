using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// Puts the entities of an assembly together for <c>IncludingAll()</c>, which <c>CreateTable()</c> needs so that an entity cannot be forgotten. It exists only where it cannot be wrong at
/// compile time: every entity has the keys of one table, as <c>Including&lt;T&gt;</c> demands, and code outside the types can name them all.
/// </summary>
internal static class RegistryBuilder
{
    /// <summary>The name of the generated class.</summary>
    public const string ClassName = "DynamoDbEntities";

    /// <summary>
    /// The namespace of the generated class. The class is internal, so the same name in another assembly does not clash with it, and code that has <c>using DynaSharp;</c> finds it
    /// wherever it stands, the global namespace of top-level statements included.
    /// </summary>
    public const string Namespace = "DynaSharp";

    /// <summary>What the registry takes of an entity.</summary>
    /// <param name="type">The model of the entity.</param>
    /// <returns>The entry.</returns>
    public static RegisteredEntity Entry(TypeModel type) =>
        new(type.FullName, type.Entity!.PartitionKey.AttributeName, type.Entity.SortKey?.AttributeName, type.Reach, type.Entity.TableName, MetadataName(type));

    /// <summary>
    /// Finds a type of the assembly that has the name of the class (CS0101). It runs on every edit, and its one lookup is by name. A type named <c>DynaSharp</c> needs no check:
    /// it hides the namespace of the attributes, so the assembly has no entities.
    /// </summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns><c>DYNA038</c>, or null.</returns>
    public static DiagnosticInfo? Clash(Compilation compilation)
    {
        const string Name = Namespace + "." + ClassName;
        if (compilation.Assembly.GetTypeByMetadataName(Name) is not { } type)
        {
            return null;
        }

        var spot = Spot.Of(type, new Spot(string.Empty, default));
        return new DiagnosticInfo(Descriptors.NamedLikeEntitiesClass.Id, new EquatableArray<string>([type.ToDisplayString()]), spot.FilePath, spot.Span);
    }

    /// <summary>Decides whether the class exists, and says why when it does not.</summary>
    /// <param name="entities">The entities of the assembly, in any order.</param>
    /// <param name="clash">What in the assembly takes a name the class needs, or null.</param>
    /// <returns>The registry.</returns>
    public static EntityRegistry Build(ImmutableArray<RegisteredEntity> entities, DiagnosticInfo? clash)
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

        if (clash is not null)
        {
            return new EntityRegistry(null, new EquatableArray<DiagnosticInfo>([clash]));
        }

        return new EntityRegistry(new EntitiesClass(new EquatableArray<string>([.. ordered.Select(static entity => entity.FullName)])), default);
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
