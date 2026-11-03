using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Testing.HandlerRequest;

namespace DynaSharp.UnitTests.Testing;

/// <summary>
/// Which operations the answer of each factory is for, as a <see cref="StubHandler"/> reads them: a request to <c>DescribeLimits</c>, which no factory answers, shows what a response is for, or that it fits any.
/// </summary>
public sealed class StubResponseOperationTests
{
    private const string Write = "PutItem, UpdateItem or DeleteItem";
    private const string NoFactoryAnswers = "DynamoDB_20120810.DescribeLimits";

    private static readonly Item Key = new() { ["pk"] = "a" };

    private static readonly Dictionary<string, (Func<StubResponse> Create, string Answers)> ForOperations = new()
    {
        ["Item"] = (() => StubResponse.Item(Key), "GetItem"),
        ["Item of a miss"] = (() => StubResponse.Item<Item>(null), "GetItem"),
        ["Items"] = (() => StubResponse.Items([Key]), "Query or Scan"),
        ["Items with a key"] = (() => StubResponse.Items([Key], Key), "Query or Scan"),
        ["Count"] = (() => StubResponse.Count(1), "Query or Scan"),
        ["Attributes"] = (() => StubResponse.Attributes(Key), Write),
        ["Attributes of none"] = (() => StubResponse.Attributes<Item>(null), Write),
        ["ConditionalCheckFailed"] = (() => StubResponse.ConditionalCheckFailed(), Write),
        ["ConditionalCheckFailed with the item"] = (() => StubResponse.ConditionalCheckFailed(Key), Write),
        ["TransactionCanceled"] = (() => StubResponse.TransactionCanceled(TransactionFailure.ConditionalCheckFailed), "TransactWriteItems or TransactGetItems"),
        ["Unprocessed writes"] = (() => StubResponse.Unprocessed("Orders", StubResponse.Delete(Key)), "BatchWriteItem"),
        ["Unprocessed keys"] = (() => StubResponse.Unprocessed("Orders", StubResponse.Keys(Key)), "BatchGetItem"),
        ["BatchItems"] = (() => StubResponse.BatchItems("Orders", [Key]), "BatchGetItem"),
        ["Table"] = (() => StubResponse.Table("Orders"), "DescribeTable"),
        ["TableCreated"] = (() => StubResponse.TableCreated("Orders"), "CreateTable"),
        ["TableUpdated"] = (() => StubResponse.TableUpdated("Orders"), "UpdateTable"),
        ["TableDeleted"] = (() => StubResponse.TableDeleted("Orders"), "DeleteTable"),
        ["TablesListed"] = (() => StubResponse.TablesListed("Orders"), "ListTables"),
        ["TablesListed with more to come"] = (() => StubResponse.TablesListed(["Orders"], "Orders"), "ListTables"),
        ["TimeToLive"] = (() => StubResponse.TimeToLive(TimeToLiveStatus.Enabled, "expires"), "DescribeTimeToLive"),
        ["TimeToLiveUpdated"] = (() => StubResponse.TimeToLiveUpdated(true, "expires"), "UpdateTimeToLive"),
        ["StreamDescription"] = (() => StubResponse.StreamDescription("Orders"), "DescribeStream"),
        ["ShardIterator"] = (() => StubResponse.ShardIterator("iterator"), "GetShardIterator"),
        ["Records"] = (() => StubResponse.Records(null), "GetRecords"),
    };

    private static readonly Dictionary<string, Func<StubResponse>> ForAny = new()
    {
        ["ResourceNotFound"] = StubResponse.ResourceNotFound,
        ["ResourceInUse"] = StubResponse.ResourceInUse,
        ["DynamoDbError"] = () => StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded"),
        ["Json"] = () => StubResponse.Json("{}"),
        ["new StubResponse()"] = () => new StubResponse(),
    };

    public static IEnumerable<string> FactoriesForOperations() => ForOperations.Keys;

    public static IEnumerable<string> FactoriesForAny() => ForAny.Keys;

    [Test]
    [MethodDataSource(nameof(FactoriesForOperations))]
    public async Task SendAsync_AnswerOfAFactoryForAnotherOperation_NamesTheOperationsItsBodyIsWrittenFor(string factory)
    {
        // Arrange
        var (create, answers) = ForOperations[factory];
        using var handler = new StubHandler();
        handler.Enqueue(create());

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: NoFactoryAnswers));

        // Assert
        await Assert.That(exception.Message).Contains($"for a request to DescribeLimits, but that response is the answer to {answers}.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(FactoriesForAny))]
    public async Task SendAsync_AnswerOfAFactoryForAnyOperation_IsReturned(string factory)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ForAny[factory]());

        // Act
        var act = async () =>
        {
            using var response = await PostAsync(handler, target: NoFactoryAnswers);
        };

        // Assert
        await Assert.That(act).ThrowsNothing();
    }
}
