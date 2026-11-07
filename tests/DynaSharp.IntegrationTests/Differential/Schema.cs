using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// The two tables of one sequence. The main table has the partition key <c>pk</c> (a string), the sort key <c>sk</c> of <see cref="SortKeyType"/> and the global index
/// <c>gsi</c> over <c>gpk</c> (a string) and <c>gsk</c> (a number), projected as <see cref="IndexProjection"/>. The other table has the partition key <c>id</c> only, for
/// batches and transactions over two tables. A request names them by <see cref="Main"/> and <see cref="Other"/>, which the runner replaces with the names of the run.
/// </summary>
/// <param name="SortKeyType">The type of <c>sk</c>: <c>S</c>, <c>N</c> or <c>B</c>.</param>
/// <param name="IndexProjection">The projection of <c>gsi</c>: <c>ALL</c>, <c>KEYS_ONLY</c> or <c>INCLUDE</c> (of <see cref="IncludedAttributes"/>).</param>
internal sealed record Schema(string SortKeyType, string IndexProjection)
{
    /// <summary>The placeholder of the main table's name.</summary>
    public const string Main = "$T1";

    /// <summary>The placeholder of the other table's name.</summary>
    public const string Other = "$T2";

    /// <summary>The name of the global secondary index of the main table.</summary>
    public const string Index = "gsi";

    /// <summary>The attributes an <c>INCLUDE</c> index projects beside the keys.</summary>
    public static readonly string[] IncludedAttributes = ["a", "status"];

    // A string sort key is drawn twice as often: begins_with and the items at the size limit need one.
    private static readonly string[] SortKeyTypes = ["S", "S", "N", "B"];

    private static readonly string[] Projections = ["ALL", "KEYS_ONLY", "INCLUDE"];

    /// <summary>Picks the shape of a sequence.</summary>
    /// <param name="random">The generator of the sequence.</param>
    /// <returns>The schema.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes a sequence reproducible.")]
    public static Schema Pick(Random random) => new(SortKeyTypes[random.Next(SortKeyTypes.Length)], Projections[random.Next(Projections.Length)]);

    /// <summary>The CreateTable request of the other table.</summary>
    /// <param name="name">The name of the table.</param>
    /// <returns>The request body.</returns>
    public static string CreateOther(string name) => new JsonObject
    {
        ["TableName"] = name,
        ["BillingMode"] = "PAY_PER_REQUEST",
        ["AttributeDefinitions"] = new JsonArray(Definition("id", "S")),
        ["KeySchema"] = new JsonArray(KeyElement("id", "HASH")),
    }.ToJsonString();

    /// <summary>The CreateTable request of the main table.</summary>
    /// <param name="name">The name of the table.</param>
    /// <returns>The request body.</returns>
    public string CreateMain(string name)
    {
        var projection = new JsonObject { ["ProjectionType"] = IndexProjection };
        if (IndexProjection == "INCLUDE")
        {
            projection["NonKeyAttributes"] = new JsonArray([.. IncludedAttributes.Select(static attribute => (JsonNode)attribute)]);
        }

        return new JsonObject
        {
            ["TableName"] = name,
            ["BillingMode"] = "PAY_PER_REQUEST",
            ["AttributeDefinitions"] = new JsonArray(
                Definition("pk", "S"),
                Definition("sk", SortKeyType),
                Definition("gpk", "S"),
                Definition("gsk", "N")),
            ["KeySchema"] = new JsonArray(KeyElement("pk", "HASH"), KeyElement("sk", "RANGE")),
            ["GlobalSecondaryIndexes"] = new JsonArray(new JsonObject
            {
                ["IndexName"] = Index,
                ["KeySchema"] = new JsonArray(KeyElement("gpk", "HASH"), KeyElement("gsk", "RANGE")),
                ["Projection"] = projection,
            }),
        }.ToJsonString();
    }

    private static JsonObject Definition(string name, string type) => new() { ["AttributeName"] = name, ["AttributeType"] = type };

    private static JsonObject KeyElement(string name, string type) => new() { ["AttributeName"] = name, ["KeyType"] = type };
}
