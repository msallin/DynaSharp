using System.Text.Json.Nodes;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>How the runner sends an operation and what of its answers it compares.</summary>
internal enum Reading
{
    /// <summary>One request, one response.</summary>
    Single,

    /// <summary>A Query or Scan sent again with each <c>LastEvaluatedKey</c> as <c>ExclusiveStartKey</c>, until a page has none. Every page is compared.</summary>
    Pages,

    /// <summary>
    /// A Query or Scan whose order DynamoDB leaves open (a scan, or a query of the index, whose items with equal index keys come in any order), read to the end as
    /// <see cref="Pages"/> and, for a parallel scan, for each segment. Compared are the items as a set, the sums of <c>Count</c> and <c>ScannedCount</c> and the number
    /// of pages, and for an index query, that each answer is in index key order.
    /// </summary>
    Unordered,
}

/// <summary>One request of a sequence. The body names the tables by <see cref="Schema.Main"/> and <see cref="Schema.Other"/>, and <c>$RUN</c> stands for the run in a token.</summary>
/// <param name="Name">The operation, such as <c>PutItem</c>.</param>
/// <param name="Body">The request.</param>
/// <param name="Reading">How it is sent and compared.</param>
internal sealed record Operation(string Name, JsonObject Body, Reading Reading = Reading.Single)
{
    /// <summary>Gets the operation and its request as one line, for a report.</summary>
    /// <returns>The line.</returns>
    public override string ToString() => $"{Name} {Body.ToJsonString()}";
}
