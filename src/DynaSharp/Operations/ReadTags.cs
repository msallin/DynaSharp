using System.Diagnostics;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>
/// The span tags that the operations that read have in common, named after the request parameters they report (OpenTelemetry
/// semantic conventions for DynamoDB). Tags are set only when the request sets the parameter, except for the consistent read flag,
/// which is always reported. Key values, expressions other than the projection, and attribute values never become tags.
/// </summary>
internal static class ReadTags
{
    /// <summary>Sets the tags that <c>Query</c> and <c>Scan</c> have in common.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="consistentRead">Whether the read is strongly consistent.</param>
    /// <param name="projectionExpression">The projection expression: attribute names only, no values.</param>
    /// <param name="indexName">The index, if one is read.</param>
    /// <param name="limit">The item limit, if set.</param>
    /// <param name="select">The attribute selection.</param>
    public static void Set(Activity activity, bool consistentRead, string? projectionExpression, string? indexName, int? limit, Select select)
    {
        SetProjection(activity, consistentRead, projectionExpression);
        if (indexName is not null)
        {
            activity.SetTag(TelemetryNames.IndexName, indexName);
        }

        SetLimit(activity, limit);
        if (WireNames.Selects.TryGetName(select, out var selectName))
        {
            activity.SetTag(TelemetryNames.Select, selectName);
        }
    }

    /// <summary>Sets the tags that every read of a table has, which is all that <c>GetItem</c> reports.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="consistentRead">Whether the read is strongly consistent.</param>
    /// <param name="projectionExpression">The projection expression: attribute names only, no values.</param>
    public static void SetProjection(Activity activity, bool consistentRead, string? projectionExpression)
    {
        activity.SetTag(TelemetryNames.ConsistentRead, consistentRead);
        if (projectionExpression is not null)
        {
            activity.SetTag(TelemetryNames.Projection, projectionExpression);
        }
    }

    /// <summary>Sets the limit tag, the one tag the operations of DynamoDB Streams report.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="limit">The limit, if set.</param>
    public static void SetLimit(Activity activity, int? limit)
    {
        if (limit is { } value)
        {
            activity.SetTag(TelemetryNames.Limit, value);
        }
    }

    /// <summary>Sets the tags for the response of a read: how many items it returned and how many it evaluated.</summary>
    /// <remarks>
    /// The conventions list <c>aws.dynamodb.count</c> and <c>aws.dynamodb.scanned_count</c> for Scan only. Query gets them as well, on
    /// purpose: they are the same response parameters, and a trace of a query is as hard to read without them.
    /// </remarks>
    /// <param name="activity">The span.</param>
    /// <param name="count">The number of items in the page, after the filter.</param>
    /// <param name="scannedCount">The number of items evaluated before the filter.</param>
    public static void SetResponse(Activity activity, int count, int scannedCount)
    {
        activity.SetTag(TelemetryNames.Count, count);
        activity.SetTag(TelemetryNames.ScannedCount, scannedCount);
    }
}
