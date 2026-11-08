using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Inverted index</see>: an item per term and document, so a term reads its documents, and several terms intersect on the client. That is trivial scale: no ranking, one query per term, and a write per term of a
/// document. Beyond that, use OpenSearch. Terms such as <c>C#</c> hold a <c>#</c>, so this table uses <c>|</c> as the delimiter of its keys.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class InvertedIndexTests(Backend backend)
{
    [Test]
    public async Task SearchAsync_TwoTerms_FindsTheDocumentsThatHaveBoth()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var postings = sandbox.Shop.Of<Posting>();
        await IndexAsync(postings, "d1", "dynamodb from c#");
        await IndexAsync(postings, "d2", "dynamodb from java");
        await IndexAsync(postings, "d3", "c# in depth");

        // Act
        var found = await SearchAsync(postings, "c#", "dynamodb");

        // Assert
        await Assert.That(found.Single()).IsEqualTo("d1");
    }

    [Test]
    public async Task DeleteAll_TheTermsOfADocument_TakesItOutOfTheIndex()
    {
        // Arrange (a delete has to know the terms, so the document holds its text)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var postings = sandbox.Shop.Of<Posting>();
        await IndexAsync(postings, "d1", "dynamodb from c#");

        // Act
        await postings.BatchWrite().DeleteAll(Terms("dynamodb from c#").Select(static term => Posting.Keys.Of(term, "d1"))).SendAsync();

        // Assert
        await Assert.That(await SearchAsync(postings, "dynamodb")).IsEmpty();
    }

    private static string[] Terms(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static async Task IndexAsync(Table<Posting> postings, string document, string text) =>
        await postings.BatchWrite().PutAll(Terms(text).Select(term => new Posting { Term = term, Document = document })).SendAsync();

    private static async Task<HashSet<string>> SearchAsync(Table<Posting> postings, params string[] terms)
    {
        HashSet<string>? found = null;
        foreach (var term in terms)
        {
            var documents = await postings.Query(Posting.Keys.Pk.Of(term)).Consistent().ItemsAsync().Select(static posting => posting.Document).ToHashSetAsync();
            found?.IntersectWith(documents);
            found ??= documents;
        }

        return found ?? [];
    }
}

[DynamoDbEntity]
[Discriminator("POSTING")]
[PartitionKey("TERM|{Term}")]
[SortKey("DOC|{Document}")]
internal sealed partial record Posting
{
    public required string Term { get; init; }

    public required string Document { get; init; }
}
