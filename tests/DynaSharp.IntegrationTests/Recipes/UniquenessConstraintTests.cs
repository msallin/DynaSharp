using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Uniqueness constraints</see>: DynamoDB keeps only a primary key unique, so a value that must be unique gets an item whose key is that value, written with the record in one
/// transaction. With several values the caller has to learn which one failed, which is what a marker says.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class UniquenessConstraintTests(Backend backend)
{
    private const string Mail = "a@example.ch";

    private enum Registration
    {
        Done,
        EmailTaken,
        HandleTaken,
    }

    [Test]
    public async Task RegisterAsync_EmailTaken_SaysWhichValueAndWritesNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await RegisterAsync(sandbox, new Registrant { Id = "alice", Email = Mail, Handle = "ally" });

        // Act
        var outcome = await RegisterAsync(sandbox, new Registrant { Id = "bob", Email = Mail, Handle = "bobby" });

        // Assert (the handle of the failed attempt stays free)
        var bobby = await sandbox.Shop.Of<HandleClaim>().Get(HandleClaim.Keys.Of("bobby")).Consistent().SendAsync();
        await Assert.That((outcome, bobby.Found, (await sandbox.Shop.Of<Registrant>().Get(Registrant.Keys.Of("bob")).Consistent().SendAsync()).Found)).IsEqualTo((Registration.EmailTaken, false, false));
    }

    [Test]
    public async Task RegisterAsync_HandleTaken_SaysTheOtherValue()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await RegisterAsync(sandbox, new Registrant { Id = "alice", Email = Mail, Handle = "ally" });

        // Act
        var outcome = await RegisterAsync(sandbox, new Registrant { Id = "bob", Email = "b@example.ch", Handle = "ally" });

        // Assert
        await Assert.That(outcome).IsEqualTo(Registration.HandleTaken);
    }

    [Test]
    public async Task RegisterAsync_ConcurrentForOneEmail_LetsOneWin()
    {
        // Arrange
        const int Contenders = 8;
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var outcomes = await Task.WhenAll(Enumerable.Range(0, Contenders).Select(i => RegisterAsync(sandbox, new Registrant { Id = $"user{i}", Email = Mail, Handle = $"handle{i}" })));

        // Assert
        await Assert.That(outcomes.Count(static outcome => outcome == Registration.Done)).IsEqualTo(1);
    }

    [Test]
    public async Task ChangeEmailAsync_ReleasesTheOldValueInTheSameTransaction()
    {
        // Arrange
        const string New = "new@example.ch";
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await RegisterAsync(sandbox, new Registrant { Id = "alice", Email = Mail, Handle = "ally" });
        var emails = sandbox.Shop.Of<EmailClaim>();

        // Act
        await sandbox.Client.TransactWrite()
            .Delete(emails, EmailClaim.Keys.Of(Mail)).If(EmailClaim.Attrs.Owner.Eq("alice"))
            .Unique(emails, new EmailClaim { Address = New, Owner = "alice" }, out _)
            .Update(sandbox.Shop.Of<Registrant>(), Registrant.Keys.Of("alice")).Set(Registrant.Attrs.Email, New)
            .SendAsync();
        var outcome = await RegisterAsync(sandbox, new Registrant { Id = "bob", Email = Mail, Handle = "bobby" });

        // Assert
        await Assert.That(outcome).IsEqualTo(Registration.Done);
    }

    private static async Task<Registration> RegisterAsync(CommandSandbox sandbox, Registrant user)
    {
        TransactMarker emailTaken = default;
        TransactMarker handleTaken = default;

        // By hand, because only a conflict may repeat this. RetryOnConflictAsync would also repeat a failed IfNotExists of the record, and nothing is read that could change the outcome.
        while (true)
        {
            try
            {
                await sandbox.Client.TransactWrite()
                    .Unique(sandbox.Shop.Of<EmailClaim>(), new EmailClaim { Address = user.Email, Owner = user.Id }, out emailTaken)
                    .Unique(sandbox.Shop.Of<HandleClaim>(), new HandleClaim { Handle = user.Handle, Owner = user.Id }, out handleTaken)
                    .Put(sandbox.Shop.Of<Registrant>(), user).IfNotExists()
                    .SendAsync();
                return Registration.Done;
            }
            catch (TransactionCanceledException exception) when (exception.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
            {
                return Registration.EmailTaken;
            }
            catch (TransactionCanceledException exception) when (exception.ReasonFor(handleTaken) == TransactionFailure.ConditionalCheckFailed)
            {
                return Registration.HandleTaken;
            }
            catch (TransactionCanceledException exception) when (exception.Failures.Any(static failure => failure.Reason == TransactionFailure.TransactionConflict))
            {
                // Another registration of the same value is in flight: only asking again tells who got it.
            }
        }
    }
}

[DynamoDbEntity]
[Discriminator("REGISTRANT")]
[PartitionKey("USER#{Id}")]
[SortKey("PROFILE")]
internal sealed partial record Registrant
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    public required string Handle { get; init; }
}

[DynamoDbEntity]
[Discriminator("EMAIL")]
[PartitionKey("EMAIL#{Address}")]
[SortKey("CLAIM")]
internal sealed partial record EmailClaim
{
    public required string Address { get; init; }

    public required string Owner { get; init; }
}

[DynamoDbEntity]
[Discriminator("HANDLE")]
[PartitionKey("HANDLE#{Handle}")]
[SortKey("CLAIM")]
internal sealed partial record HandleClaim
{
    public required string Handle { get; init; }

    public required string Owner { get; init; }
}
