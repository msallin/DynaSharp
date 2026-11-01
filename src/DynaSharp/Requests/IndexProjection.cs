namespace DynaSharp.Requests;

/// <summary>
/// The attributes a secondary index copies from the table.
/// </summary>
/// <remarks>
/// A query on an index can only return projected attributes (and the key attributes of the table and the index); a global
/// secondary index cannot fetch the rest from the table. Every projected attribute costs storage and write capacity.
/// </remarks>
public sealed record IndexProjection
{
    /// <summary>Gets which attributes are projected.</summary>
    public required ProjectionType ProjectionType { get; init; }

    /// <summary>
    /// Gets the names of the attributes to project besides the keys when <see cref="ProjectionType"/> is
    /// <see cref="Requests.ProjectionType.Include"/>; <see langword="null"/> otherwise. All indexes of a table together can project at
    /// most 100 attributes.
    /// </summary>
    public IReadOnlyList<string>? NonKeyAttributes { get; init; }
}
