using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

// A library must not resume on the context of its caller, see CountingSynchronizationContext. Each test holds back one request of the command, so that its await
// is the first that suspends, and every await between SendAsync and that request is the one under test.
public sealed partial class CreateTableCommandTests
{
    [Test]
    public async Task SendAsync_IfNotExistsAndTheFirstLookAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Json(Active));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable().IfNotExists());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_CreateAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(0, StubResponse.Json(Created));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WaitWhoseLookAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(1, StubResponse.Json(Created), StubResponse.Json(Active));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable().WaitUntilActive());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_DescribeAfterAResponseWithoutADescriptionAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(1, StubResponse.Json("{}"), StubResponse.Json(Active));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_LookAfterACreateThatFoundTheTableAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(2, NotFound(), StubResponse.ResourceInUse(), StubResponse.Json(Active));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable().IfNotExists());

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_LookAtTimeToLiveAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(1, StubResponse.Json(Active), StubResponse.Json(Disabled), StubResponse.Json(Updated));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable().IfNotExists().TimeToLive("expires"));

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_UpdateOfTimeToLiveAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var held = new HeldResponses(2, StubResponse.Json(Active), StubResponse.Json(Disabled), StubResponse.Json(Updated));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable().IfNotExists().TimeToLive("expires"));

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_LookAfterARejectedUpdateOfTimeToLiveAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (another instance turned it on first)
        var held = new HeldResponses(
            3,
            StubResponse.Json(Active),
            StubResponse.Json(Disabled),
            StubResponse.DynamoDbError("ValidationException", "TimeToLive is already enabled."),
            StubResponse.Json(TimeToLive("ENABLED", "expires")));

        // Act
        var posts = await PostsAsync(held, static table => table.CreateTable().IfNotExists().TimeToLive("expires"));

        // Assert
        await Assert.That(posts).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WaitBeforeTheNextCreate_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (DescribeTable does not see yet what another process is creating)
        var context = new CountingSynchronizationContext();
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.ResourceInUse());
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.Json(Created));
        using var client = TestClient.Create(handler, time);
        var call = context.Start(() => client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync().AsTask());

        // Act
        time.Advance(TimeSpan.FromMilliseconds(100));
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    private static async Task<int> PostsAsync(HeldResponses held, Func<Table<Item>, CreateTableCommand> command)
    {
        var context = new CountingSynchronizationContext();
        using var handler = new StubHandler(held.AnswerAsync);
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var create = command(client.Table("Shop", "pk"));
        var call = context.Start(() => create.SendAsync().AsTask());
        held.Release();
        await call;
        return context.Posts;
    }
}
