namespace DynaSharp;

/// <summary>
/// A table that <see cref="CreateTableCommand.IfNotExists"/> found differs from what the command declares, and the command was told to stop there: <see cref="Drift"/> lists each difference, and the
/// message says them all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it comes from.</b> <see cref="CreateTableCommand.FailOnDrift"/> turns the drift that <see cref="CreateTableResult.Drift"/> would report into this exception, and
/// <see cref="CreateTableCommand.WaitUntilActive(TimeSpan)"/> throws it without that clause when the table does not list a global index the command declares, which no wait adds.
/// </para>
/// <para>
/// <b>What it left.</b> Drift of an index or a stream is found from the first look, before any wait, so the command changed nothing. Time to live on another attribute is found after the wait, and the command
/// leaves it as it is too. A key that differs is no drift and throws a plain <see cref="InvalidOperationException"/>, which this type derives from, so one <c>catch</c> covers a table that does not fit.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     await orders.CreateTable().Including&lt;Customer&gt;().IfNotExists().FailOnDrift().SendAsync(ct);
/// }
/// catch (TableDriftException exception)
/// {
///     Console.Error.WriteLine($"{exception.TableName} lacks {exception.Drift.Count(drift => drift.Kind == TableDriftKind.IndexMissing)} indexes.");
///     throw;
/// }
/// </code>
/// </example>
public sealed class TableDriftException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="TableDriftException"/> class, with a message that names the table and says each drift.</summary>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="drift">The ways the table differs, at least one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> or <paramref name="drift"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="drift"/> is empty.</exception>
    public TableDriftException(string tableName, IReadOnlyList<TableDrift> drift)
        : base(Describe(tableName, drift))
    {
        TableName = tableName;
        Drift = drift;
    }

    /// <summary>Gets the name of the table, or its ARN, as the table was opened.</summary>
    public string TableName { get; }

    /// <summary>Gets each way in which the table differs from what the command declares, in the order <see cref="CreateTableResult.Drift"/> would list them.</summary>
    public IReadOnlyList<TableDrift> Drift { get; }

    /// <summary>Joins the descriptions of drift into one text, for this message and for the warning of a drift that does not throw.</summary>
    /// <param name="drift">The drift.</param>
    /// <returns>The descriptions, separated by a space.</returns>
    internal static string Join(IReadOnlyList<TableDrift> drift)
    {
        var descriptions = new string[drift.Count];
        for (var i = 0; i < descriptions.Length; i++)
        {
            descriptions[i] = drift[i].Description;
        }

        return string.Join(' ', descriptions);
    }

    private static string Describe(string tableName, IReadOnlyList<TableDrift> drift)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(drift);
        if (drift.Count == 0)
        {
            throw new ArgumentException("A drift exception needs at least one drift.", nameof(drift));
        }

        var differences = drift.Count == 1 ? "1 way" : $"{drift.Count} ways";
        return $"The table {tableName} differs from what CreateTable() declares in {differences}: {Join(drift)}";
    }
}
