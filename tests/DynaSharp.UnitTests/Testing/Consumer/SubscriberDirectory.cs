using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Testing.Consumer;

/// <summary>
/// The code under test of <see cref="SubscriberDirectoryTests"/>: a small service as an application writes it over <see cref="Table{T}"/>. It knows nothing of tests;
/// all it takes is a client.
/// </summary>
/// <param name="client">The client.</param>
internal sealed class SubscriberDirectory(DynamoDbClient client)
{
    public const string TableName = "Subscribers";

    private static readonly Attr<string, StringConverter> Email = new("email");
    private static readonly Attr<string, StringConverter> Plan = new("plan");

    private readonly Table<Subscriber> _subscribers = client.Table<Subscriber>(TableName);
    private readonly Table<EmailClaim> _claims = client.Table<EmailClaim>(TableName);

    public async Task<Subscriber?> FindAsync(string id) => (await _subscribers.Get(new SubscriberKey(id)).SendAsync()).Item;

    /// <summary>The first registration of an id wins, and a later one learns who holds it.</summary>
    /// <param name="subscriber">The subscriber to register.</param>
    /// <returns>Who holds the id now.</returns>
    public async Task<Subscriber> RegisterAsync(Subscriber subscriber)
    {
        try
        {
            await _subscribers.Put(subscriber).IfNotExists().ReturnOldOnFailure().SendAsync();
            return subscriber;
        }
        catch (ConditionalCheckFailedException failure) when (failure.Item is not null)
        {
            return failure.Item.To<Subscriber>();
        }
    }

    public async Task<Subscriber?> ChangePlanAsync(string id, string plan) =>
        (await _subscribers.Update(new SubscriberKey(id)).Set(Plan, plan).IfExists().ReturnNew().SendAsync()).Item;

    /// <summary>Moves a subscriber to an address that nobody else holds, and frees the old one, all or nothing.</summary>
    /// <param name="subscriber">The subscriber.</param>
    /// <param name="email">The new address.</param>
    /// <returns>Who holds the address if it is taken, otherwise <see langword="null"/>.</returns>
    public async Task<string?> ChangeEmailAsync(Subscriber subscriber, string email)
    {
        TransactMarker claim = default;
        try
        {
            await client.TransactWrite()
                .Unique(_claims, new EmailClaim(email, subscriber.Id), out claim).ReturnOldOnFailure()
                .Delete(_claims, subscriber.Email)
                .Update(_subscribers, new SubscriberKey(subscriber.Id)).Set(Email, email).IfExists()
                .SendAsync();
            return null;
        }
        catch (TransactionCanceledException failure) when (failure.ReasonFor(claim) == TransactionFailure.ConditionalCheckFailed)
        {
            return failure.ItemFor(claim)!.To<EmailClaim>().SubscriberId;
        }
    }

    public async Task<int> ImportAsync(IEnumerable<Subscriber> subscribers)
    {
        var batch = _subscribers.BatchWrite();
        foreach (var subscriber in subscribers)
        {
            batch = batch.Put(subscriber);
        }

        return (await batch.SendAsync()).Written;
    }

    public async Task<IReadOnlyList<Subscriber>> ListAsync()
    {
        var subscribers = new List<Subscriber>();
        await foreach (var subscriber in _subscribers.Scan().ItemsAsync())
        {
            subscribers.Add(subscriber);
        }

        return subscribers;
    }

    public async Task<IReadOnlyList<Subscriber>> ReadManyAsync(IEnumerable<string> ids)
    {
        var read = _subscribers.BatchGet();
        foreach (var id in ids)
        {
            read = read.Key(new SubscriberKey(id));
        }

        return (await read.SendAsync()).Items;
    }
}
