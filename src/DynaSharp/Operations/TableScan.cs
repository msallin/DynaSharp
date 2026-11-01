using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>
/// Finds out which tables a batch or transaction addresses, for the telemetry of the call: the one table, if there is exactly one,
/// and on request all of them for the <c>aws.dynamodb.table_names</c> tag. Table names are never keys or values, so they may be
/// reported.
/// </summary>
internal struct TableScan
{
    private readonly SortedSet<string>? _all;
    private string? _first;
    private bool _several;

    private TableScan(SortedSet<string> all)
    {
        _all = all;
    }

    /// <summary>Gets the table name when every name added so far is the same one; <see langword="null"/> for none and for several.</summary>
    public readonly string? Only => _several ? null : _first;

    /// <summary>Starts a scan that remembers all names, for <see cref="SetTag"/>. The default scan only tells whether there is one table.</summary>
    /// <returns>The scan.</returns>
    [SuppressMessage("Style", "IDE0028:Simplify collection initialization", Justification = "The compiler in Stryker cannot parse collection expression arguments.")]
    public static TableScan Collecting() => new(new SortedSet<string>(StringComparer.Ordinal));

    /// <summary>Adds the table of an item of the request. Names are compared ordinally, as DynamoDB does.</summary>
    /// <param name="table">The table name or ARN.</param>
    public void Add(string table)
    {
        _all?.Add(table);
        if (_first is null)
        {
            _first = table;
        }
        else if (!string.Equals(_first, table, StringComparison.Ordinal))
        {
            _several = true;
        }
    }

    /// <summary>Adds the tables of a batch: the keys of <c>RequestItems</c>.</summary>
    /// <typeparam name="TValue">What the request holds per table.</typeparam>
    /// <param name="requestItems">The <c>RequestItems</c> of a batch request.</param>
    public void AddKeys<TValue>(IReadOnlyDictionary<string, TValue> requestItems)
    {
        // Through the interface the keys are enumerated with a boxed enumerator on every call. A Dictionary, which is what callers pass,
        // has a struct one.
        if (requestItems is Dictionary<string, TValue> dictionary)
        {
            foreach (var table in dictionary.Keys)
            {
                Add(table);
            }

            return;
        }

        foreach (var table in requestItems.Keys)
        {
            Add(table);
        }
    }

    /// <summary>Sets <c>aws.dynamodb.table_names</c> to the distinct names of a collecting scan, sorted ordinally.</summary>
    /// <param name="activity">The span.</param>
    public readonly void SetTag(Activity activity)
    {
        string[] names = _all is null ? [] : [.. _all];
        activity.SetTag(TelemetryNames.TableNames, names);
    }
}
