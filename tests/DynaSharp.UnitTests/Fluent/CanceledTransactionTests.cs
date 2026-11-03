using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What the message of a cancelled transaction says. DynamoDB lists the codes, <c>[None, ConditionalCheckFailed]</c>, so the client adds the position, the verb and the table of each action that failed, and
/// never a key or a value.
/// </summary>
public sealed class CanceledTransactionTests
{
    [Test]
    public async Task SendAsync_SecondPutFails_NamesItsPositionVerbAndTable()
    {
        // Arrange (a user and the claim of the email, and the email is taken)
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed));
        using var client = TestClient.Create(handler);
        var emails = client.Table("Emails", "pk");
        var users = client.Table("Users", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite()
            .Put(emails, new Item { ["pk"] = "e1" })
            .Put(users, new Item { ["pk"] = "u1" }).IfNotExists()
            .SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(
            "Transaction cancelled, please refer cancellation reasons for specific reasons [None, ConditionalCheckFailed]" + Environment.NewLine + "Action 2 of 2, Put on Users: ConditionalCheckFailed");
    }

    [Test]
    public async Task SendAsync_SeveralActionsFail_NamesEachWithItsVerb()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(
            TransactionFailure.TransactionConflict, TransactionFailure.None, TransactionFailure.ConditionalCheckFailed, TransactionFailure.ValidationError));
        using var client = TestClient.Create(handler);
        var orders = client.Table("Orders", "pk");
        var users = client.Table("Users", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite()
            .Update(orders, "o1").Set("n", 1)
            .Delete(orders, "o2")
            .Check(users, "u1").If(Attr("active").Exists())
            .Delete(users, "u2")
            .SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith(
            Environment.NewLine + "Action 1 of 4, Update on Orders: TransactionConflict; Action 3 of 4, ConditionCheck on Users: ConditionalCheckFailed; Action 4 of 4, Delete on Users: ValidationError");
    }

    [Test]
    public async Task SendAsync_GetConflicts_NamesTheGet()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.TransactionConflict));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactGet()
            .Get(client.Table("Users", "pk"), "u1", out _)
            .Get(client.Table("Orders", "pk"), "o1", out _)
            .SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith(Environment.NewLine + "Action 2 of 2, Get on Orders: TransactionConflict");
    }

    [Test]
    public async Task SendAsync_CodeThatIsNewOrMissing_NamesItAsSentOrAsUnknown()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(new CancellationReason { Code = "SomethingNew" }, new CancellationReason()));
        using var client = TestClient.Create(handler);
        var users = client.Table("Users", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite()
            .Put(users, new Item { ["pk"] = "u1" })
            .Put(users, new Item { ["pk"] = "u2" })
            .SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith(Environment.NewLine + "Action 1 of 2, Put on Users: SomethingNew; Action 2 of 2, Put on Users: Unknown");
    }

    [Test]
    public async Task SendAsync_ReasonsBeyondTheActions_NamesOnlyTheActionsThatExist()
    {
        // Arrange (DynamoDB sends one reason per action, a stub need not)
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(TransactionFailure.ConditionalCheckFailed, TransactionFailure.ConditionalCheckFailed));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite().Put(client.Table("Users", "pk"), new Item { ["pk"] = "u1" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith("]" + Environment.NewLine + "Action 1 of 1, Put on Users: ConditionalCheckFailed");
    }

    [Test]
    public async Task SendAsync_OnlyReasonsBeyondTheActions_KeepsTheMessageOfTheService()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite().Put(client.Table("Users", "pk"), new Item { ["pk"] = "u1" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("Transaction cancelled, please refer cancellation reasons for specific reasons [None, ConditionalCheckFailed]");
    }

    [Test]
    public async Task SendAsync_NoReasons_KeepsTheMessageOfTheService()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.TransactionCanceled(ReadOnlySpan<CancellationReason>.Empty));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite().Put(client.Table("Users", "pk"), new Item { ["pk"] = "u1" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("Transaction cancelled, please refer cancellation reasons for specific reasons []");
    }

    [Test]
    public async Task SendAsync_ValuesOfTheItemsAndOfWhatTheConditionSaw_StayOutOfTheText()
    {
        // Arrange (the item the condition saw comes back as well, with values of its own)
        var seen = new Item { ["pk"] = "secret-pk", ["note"] = "secret-stored" };
        using var handler = new StubHandler(_ => StubResponse.TransactionCanceled(StubResponse.ConditionalCheckFailedReason(seen)));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite()
            .Put(client.Table("Users", "pk"), new Item { ["pk"] = "secret-pk", ["note"] = "secret-note" }).If(Attr("note").Eq("secret-condition")).ReturnOldOnFailure()
            .SendAsync());

        // Assert
        var text = failure.ToString();
        await Assert.That((failure.Message.Contains("Action 1 of 1", StringComparison.Ordinal), text.Contains("secret", StringComparison.Ordinal))).IsEqualTo((true, false));
    }
}
