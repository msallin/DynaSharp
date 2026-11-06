namespace DynaSharp;

/// <summary>
/// One way in which a table that <see cref="CreateTableCommand.IfNotExists"/> found differs from what the command declares, in <see cref="CreateTableResult.Drift"/>. The command changes nothing about it: it is for a log line,
/// a health check or a test that pins the schema, and for code that acts on it, such as an <c>UpdateTable</c> that adds a missing global index.
/// </summary>
/// <param name="Kind">What differs.</param>
/// <param name="IndexName">The index, for <see cref="TableDriftKind.IndexMissing"/> and <see cref="TableDriftKind.IndexDiffers"/>; otherwise <see langword="null"/>.</param>
/// <param name="Description">
/// What the command expects and what the table has, with the table, the index and the attributes named, and what follows from it. It is schema, so it holds names of tables, indexes and attributes, and never a key or an item.
/// </param>
public sealed record TableDrift(TableDriftKind Kind, string? IndexName, string Description);
