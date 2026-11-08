using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

// A library must not resume on the context of its caller, see CountingSynchronizationContext. Each test holds back one response of a read, so that its await is
// the first that suspends, and every await between the terminal and that response is the one under test.
public sealed class PagedReadContextTests
{
    private static readonly Item First = new() { ["pk"] = "a", ["sk"] = "1" };
    private static readonly Item Second = new() { ["pk"] = "a", ["sk"] = "2" };

    [Test]
    public async Task PageAsync_AnswerLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Items([First]));

        // Act
        var posts = await PostsAsync(held, static shop => shop.Query("a").PageAsync().AsTask());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task PageAsync_TakeAndASecondResponseLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(1, StubResponse.Items([First], First), StubResponse.Items([Second]));

        // Act
        var posts = await PostsAsync(held, static shop => shop.Query("a").Take(2).PageAsync().AsTask());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemAsync_AnswerLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Items([First]));

        // Act
        var posts = await PostsAsync(held, static shop => shop.Query("a").ItemAsync().AsTask());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_AnswerLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Items([First]));

        // Act
        var posts = await PostsAsync(held, static shop => ReadAllAsync(shop.Query("a").ItemsAsync()));

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_OnMappingFailureAndAnAnswerLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Items([First]));

        // Act
        var posts = await PostsAsync(held, static shop => ReadAllAsync(shop.Query("a").OnMappingFailure(static _ => { }).ItemsAsync()));

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task PagesAsync_AnswerLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Items([First]));

        // Act
        var posts = await PostsAsync(held, static shop => ReadAllAsync(shop.Query("a").PagesAsync()));

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    private static async Task<int> PostsAsync(HeldResponses held, Func<Table<Item>, Task> read)
    {
        var context = new CountingSynchronizationContext();
        using var handler = new StubHandler(held.AnswerAsync);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var reading = context.Start(() => read(shop));
        held.Release();
        await reading;
        return context.Posts;
    }

    private static async Task ReadAllAsync<T>(IAsyncEnumerable<T> items)
    {
        await foreach (var _ in items.ConfigureAwait(false))
        {
        }
    }
}
