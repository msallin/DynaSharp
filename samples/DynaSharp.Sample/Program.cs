// A tour of DynaSharp: the data layer of a small online shop, run against a local emulator. Read it top to bottom; every section is
// one thing a shop needs from DynamoDB. The model is in ShopModel.cs, a handful of records. The source generator turns them into
// everything below that reads, writes and addresses an item, so no item, key or expression is spelled out by hand.
using DynaSharp;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Sample;

// Everything in the options is optional. Left unset, region, endpoint and credentials come from the environment the way the AWS
// SDKs read it, which is what you want in production. An emulator checks nothing, but requests are still signed.
var endpoint = Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_DYNAMODB") is { Length: > 0 } configured ? configured : "http://localhost:8000";
using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    ServiceUrl = new Uri(endpoint),
    Region = AwsRegion.FromName("us-east-1"),
    Credentials = new StaticCredentialsProvider("local", "local"),
});
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
var ct = timeout.Token;

// A table is a small immutable object: open it once and keep it. These are five views of one table, and an index is read through
// a type of its own that has no writes. Opening makes no request. Every run works in a table of its own, so runs cannot collide.
var tableName = $"Shop-{Guid.NewGuid():N}"[..13];
var products = client.Table<Product>(tableName);
var customers = client.Table<Customer>(tableName);
var orders = client.Table<Order>(tableName);
var carts = client.Table<Cart>(tableName);
var emails = client.Table<EmailClaim>(tableName);
var byStatus = orders.Index(Order.Indexes.ByStatus);
var byTracking = orders.Index(Order.Indexes.ByTracking);

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("1. Creating the table, and the one call that drops to DynaSharp.Requests");

// One table holds all five types, so no single entity describes it: its indexes are the union of what they declare, and Including
// names the others. Key names, indexes and attribute types come from the entities, so nothing here repeats them. Billing is on
// demand and an index holds all attributes, unless told otherwise. Two entities that disagree about a key throw here, before any
// request. ToRequest gives what this would send, for what it does not model, such as tags.
try
{
    var creation = await orders.CreateTable()
        .Including<Product>().Including<Customer>().Including<Cart>().Including<EmailClaim>()
        .WaitUntilActive()
        .SendAsync(ct);
    var description = creation.Table;
    Output.Say($"Table {description.TableName} is {description.TableStatus}, with {description.GlobalSecondaryIndexes.Count} global indexes");
}
catch (HttpRequestException exception)
{
    await Console.Error.WriteLineAsync($"Nothing answers at {endpoint}: {exception.Message}");
    await Console.Error.WriteLineAsync("Start an emulator with: docker compose -f samples/compose.yaml up -d");
    return 1;
}

// Below the tables sits the request API: every DynamoDB operation as a request and a response that mirror the service, as extension
// methods that "using DynaSharp.Requests" brings in. It is there for what the tables do not model, and this is what it looks like.
// Time to live is such a thing: DynamoDB deletes an abandoned cart some time after Cart.ExpiresAt, once the table is told to.
var expiry = await client.UpdateTimeToLiveAsync(
    new UpdateTimeToLiveRequest
    {
        TableName = tableName,
        TimeToLiveSpecification = new() { Enabled = true, AttributeName = Cart.Attrs.ExpiresAt.Path },
    },
    ct);
Output.Say($"Items expire by the attribute {expiry.TimeToLiveSpecification!.AttributeName}");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("2. BatchWrite: filling the catalog");

// A batch takes any number of puts and deletes. It cuts them into requests of 25 and sends again what DynamoDB left unprocessed.
// It is not atomic and has no conditions; what cannot be written ends in a BatchIncompleteException.
Product[] catalog =
[
    new() { Sku = "KB-01", Name = "Keyboard", Price = 89.00m, Stock = 5, Categories = ["office", "input"] },
    new() { Sku = "MS-02", Name = "Mouse", Price = 39.50m, Stock = 12, Categories = ["office", "input"] },
    new() { Sku = "DK-03", Name = "Standing desk", Price = 649.00m, Stock = 1, Categories = ["furniture"] },
    new() { Sku = "LP-04", Name = "Lamp", Price = 54.90m, Stock = 7 },
];

var written = await products.BatchWrite().PutAll(catalog).SendAsync(ct);
Output.Say($"{written.Written} products in {written.Requests} request");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("3. Get and BatchGet: reading by key");

// Keys.Of takes what the key template needs, and a key of another entity does not compile here. ItemAsync returns the item, or null
// for a miss, which is not an error. SendAsync is the same request, with Found and the capacity on what it returns.
if (await products.Get(Product.Keys.Of("KB-01")).Consistent().ItemAsync(ct) is { } keyboard)
{
    Output.Say($"{keyboard.Name}: {keyboard.Price:F2}, {keyboard.Stock} in stock, in {string.Join(" and ", keyboard.Categories.Order(StringComparer.Ordinal))}");
}

var unknown = await products.Get(Product.Keys.Of("XX-99")).SendAsync(ct);
Output.Say($"XX-99 found: {unknown.Found}");

// A batch get cuts the keys into requests of 100. Items come back in no order, and a key without an item contributes nothing.
string[] basket = ["MS-02", "LP-04", "XX-99"];
var found = await products.BatchGet().Keys(basket.Select(Product.Keys.Of)).SendAsync(ct);
Output.Say($"Of {basket.Length} keys, {found.Items.Count} exist: {string.Join(", ", found.Items.Select(product => product.Name).Order(StringComparer.Ordinal))}");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("4. A condition: two people edit one price");

// A condition is checked in the same atomic step as the write. Attrs makes it typed, so comparing a price with a string does not
// compile, and a name like "status" that DynamoDB reserves needs no care. ReturnOldOnFailure saves the read to find out why it failed.
try
{
    await products.Update(Product.Keys.Of("KB-01"))
        .Set(Product.Attrs.Price, 79.00m)
        .If(Product.Attrs.Price.Eq(95.00m))
        .ReturnOldOnFailure()
        .SendAsync(ct);
}
catch (ConditionalCheckFailedException exception)
{
    Output.Say($"Refused: the editor saw 95.00, but the price is {exception.Item!.To<Product>().Price:F2}");
}

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("5. A transaction: registering with a unique email");

// The claim and the customer are written together or not at all. If the email is taken, the claim fails, the transaction is
// cancelled, and the marker says which action it was. A catch filter can only see a marker declared before the try.
async Task<bool> RegisterAsync(Customer customer)
{
    TransactMarker emailTaken = default;
    try
    {
        await client.TransactWrite()
            .Unique(emails, new EmailClaim(customer.Email, customer.Id), out emailTaken).ReturnOldOnFailure()
            .Put(customers, customer).IfNotExists()
            .SendAsync(ct);
        return true;
    }
    catch (TransactionCanceledException exception) when (exception.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
    {
        Output.Say($"Cancelled: {customer.Email} belongs to {exception.ItemFor(emailTaken)!.To<EmailClaim>().CustomerId}");
        return false;
    }
}

var alice = new Customer { Id = "alice", Name = "Alice Example", Email = "alice@example.ch", ShipTo = new Address("Bahnhofstrasse 1", "Zurich", "8001") };
Output.Say($"Register alice: {await RegisterAsync(alice)}");
Output.Say($"Register bob: {await RegisterAsync(new Customer { Id = "bob", Name = "Bob Builder", Email = "bob@example.ch" })}");
Output.Say($"Register mallory with alice's email: {await RegisterAsync(new Customer { Id = "mallory", Name = "Mallory", Email = "alice@example.ch" })}");
Output.Say($"Mallory exists: {await customers.Get(Customer.Keys.Of("mallory")).Consistent().ItemAsync(ct) is not null}");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("6. Update: single attributes, also inside a document");

// An update changes what it names and leaves the rest. On its own it would create an item that does not exist; IfExists stops that.
// At walks into a nested document, so the city changes without touching the street.
var moved = await customers.Update(Customer.Keys.Of("alice"))
    .Set(Customer.Attrs.ShipTo.At(Address.Attrs.City), "Bern")
    .Add(Customer.Attrs.Interests, ["keyboards", "lamps"])
    .IfExists()
    .ReturnNew()
    .SendAsync(ct);
Output.Say($"Alice now ships to {moved.Item!.ShipTo!.City}, and likes {string.Join(" and ", moved.Item.Interests.Order(StringComparer.Ordinal))}");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("7. Placing an order: a counter and a transaction with stock");

// ADD increments on the server, so two orders never get one number, and Return gives the new value in one round trip. A retry after
// a lost response would count twice, so this call gets one attempt. A cancelled order burns its number, which is fine for a shop.
// The transaction then writes the order, adds to what the customer spent, and takes each line from the stock if there is enough.
// A step turns into a TransactWriteCommand, which holds the chain across the loop whatever step a round ends with. A marker per line
// tells which product stopped it.
async Task<Order?> PlaceOrderAsync(string customerId, DateTimeOffset placedAt, params OrderLine[] lines)
{
    var number = await customers.Update(Customer.Keys.Of(customerId))
        .Add(Customer.Attrs.OrderCount, 1)
        .IfExists()
        .MaxAttempts(1)
        .Return(Customer.Attrs.OrderCount)
        .SendAsync(ct);

    var order = new Order
    {
        CustomerId = customerId,
        Number = number,
        Status = OrderStatus.Placed,
        PlacedAt = placedAt,
        Total = lines.Sum(line => line.Quantity * line.UnitPrice),
        Lines = lines,
    };

    var soldOut = new TransactMarker[lines.Length];
    try
    {
        TransactWriteCommand transaction = client.TransactWrite()
            .Put(orders, order).IfNotExists()
            .Update(customers, Customer.Keys.Of(customerId)).Add(Customer.Attrs.Spent, order.Total);

        for (var i = 0; i < lines.Length; i++)
        {
            transaction = transaction
                .Update(products, Product.Keys.Of(lines[i].Sku), out soldOut[i])
                .Add(Product.Attrs.Stock, -lines[i].Quantity)
                .If(Product.Attrs.Stock.Ge(lines[i].Quantity));
        }

        await transaction.SendAsync(ct);
        return order;
    }
    catch (TransactionCanceledException exception)
    {
        var missing = Enumerable.Range(0, lines.Length).Where(i => exception.ReasonFor(soldOut[i]) == TransactionFailure.ConditionalCheckFailed);
        Output.Say($"Cancelled, not enough of: {string.Join(", ", missing.Select(i => lines[i].Sku))}");
        return null;
    }
}

var monday = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
OrderLine[] office = [new("KB-01", 1, 89.00m), new("MS-02", 2, 39.50m)];
await PlaceOrderAsync("alice", monday, office);
await PlaceOrderAsync("alice", monday.AddHours(1), new OrderLine("LP-04", 2, 54.90m));
await PlaceOrderAsync("bob", monday.AddHours(2), new OrderLine("DK-03", 1, 649.00m));
await PlaceOrderAsync("alice", monday.AddHours(3), new OrderLine("DK-03", 1, 649.00m));
await PlaceOrderAsync("alice", monday.AddHours(4), new OrderLine("MS-02", 1, 39.50m));
await PlaceOrderAsync("alice", monday.AddHours(5), new OrderLine("KB-01", 1, 89.00m), new OrderLine("LP-04", 1, 54.90m));

if (await customers.Get(Customer.Keys.Of("alice")).Consistent().ItemAsync(ct) is { } afterwards)
{
    Output.Say($"Alice took {afterwards.OrderCount} order numbers and spent {afterwards.Spent:F2}");
}

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("8. Query: one partition, newest first, filtered");

// A query reads one partition in the order of the sort key. ItemsAsync follows pages for you, also pages a filter left empty. The
// sort key condition keeps the profile out cheaply; a filter runs after the read, so what it drops is still paid for.
var pkOfAlice = Order.Keys.Pk.Of("alice");
var bigOrders = orders.Query(pkOfAlice, Order.Keys.Sk.BeginsWith())
    .Filter(Order.Attrs.Total.Gt(100m))
    .Descending()
    .ItemsAsync(ct);
await foreach (var order in bigOrders)
{
    Output.Say($"#{order.Number} on {order.PlacedAt:yyyy-MM-dd HH:mm}: {order.Total:F2}, {order.Lines.Count} line(s)");
}

Output.Say($"Alice has {await orders.Query(pkOfAlice, Order.Keys.Sk.BeginsWith()).CountAsync(ct)} orders");

// One table pays off here: one query reads the customer and her orders in one go. Table<Item> is the table without a schema, and Of
// says which types to expect: a filter lets only those through, and each item arrives as what it is.
var shop = client.Table(tableName, "pk", "sk");
await foreach (var item in shop.Query(pkOfAlice).Of<Customer, Order>().ItemsAsync(ct))
{
    Output.Say(item.Match(profile => $"Profile of {profile.Name}", order => $"Order #{order.Number}"));
}

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("9. Paging with a token: what a web endpoint does");

// A web API holds no state between requests, so it reads one page and hands the client a token, which comes back with the next
// request. The token is the key of the last item. It is text for the client to keep, not a secret.
async Task<(IReadOnlyList<Order> Items, string? Next)> OrdersPageAsync(string customerId, string? after)
{
    var page = await orders.Query(Order.Keys.Pk.Of(customerId), Order.Keys.Sk.BeginsWith())
        .Limit(3)
        .PageAsync(after is null ? default : ContinuationToken.Parse(after), ct);
    return (page.Items, page.HasMore ? page.Next.ToString() : null);
}

string? next = null;
do
{
    var (items, following) = await OrdersPageAsync("alice", next);
    Output.Say($"Orders {string.Join(", ", items.Select(order => $"#{order.Number}"))}, then {(following is null ? "nothing" : $"token {following[..16]}...")}");
    next = following;
}
while (next is not null);

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("10. A global index: all placed orders since a moment, of any customer");

// The table key answers per customer. The index answers across customers, and the key builders of the index are generated as well.
// A global index is eventually consistent: on real DynamoDB, a write shows up here a moment later.
var placed = byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed), SortKeyCondition.Ge(Order.Keys.ByStatusSk.Format(monday.AddHours(2)))).ItemsAsync(ct);
await foreach (var order in placed)
{
    Output.Say($"{order.PlacedAt:HH:mm} {order.CustomerId} #{order.Number}");
}

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("11. A sparse index: shipping an order and finding it by its parcel");

// Shipping fills in the tracking number. A put writes the whole item, so every key attribute stays right: the order moves to another
// partition of ByStatus and appears in ByTracking. The condition is what makes it safe against a second shipment.
if (await orders.Get(Order.Keys.Of("bob", 1)).Consistent().ItemAsync(ct) is { } bobsOrder)
{
    await orders.Put(bobsOrder with { Status = OrderStatus.Shipped, TrackingNumber = "TRK-4711" }).If(Order.Attrs.Status.Eq(OrderStatus.Placed)).SendAsync(ct);
}

await foreach (var order in byTracking.Query(Order.Keys.ByTrackingPk.Of("TRK-4711")).ItemsAsync(ct))
{
    Output.Say($"Parcel TRK-4711 is order #{order.Number} of {order.CustomerId}, {order.Status}");
}

// Only orders with a tracking number have this index's key, so it holds one order of all, and reading it costs what it holds.
Output.Say($"Orders in ByTracking: {await byTracking.Scan().CountAsync(ct)}, of {await orders.Scan().CountAsync(ct)}");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("12. A fan-out query: one list over several partitions");

// The warehouse wants everything that has not arrived yet, newest first. That is two partitions of ByStatus, and a query reads one, so
// this query reads both and merges them by the sort key. A partition is read only when the merge needs it, so breaking out of the
// loop reads no more. PageAsync does the same for a web endpoint, with a token that remembers where each partition stands.
await foreach (var order in byStatus.Query([Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusPk.Of(OrderStatus.Shipped)]).Descending().ItemsAsync(ct))
{
    Output.Say($"{order.PlacedAt:HH:mm} {order.Status}: #{order.Number} of {order.CustomerId}");
}

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("13. A version: one cart in two browser tabs");

// A [Version] property turns every write of the entity into a compare-and-swap, with no lock and no read in between. A put takes the
// version from the entity, where 0 means new, and stores the next one. The entity stays as it is; the result says what was stored.
var created = await carts.Put(new Cart { CustomerId = "bob", Lines = [new("MS-02", 1, 39.50m)], ExpiresAt = monday.AddDays(30) }).SendAsync(ct);
Output.Say($"Bob's cart is at version {created.Version}");

// A key does not carry a version, so an update by key says which one it read. Leaving that out throws before anything is sent, and
// IgnoreVersion is how to say that it is meant. A cart that moved on fails the condition, and the tab has to read it again.
async Task SaveCartAsync(string tab, Cart seen, params OrderLine[] lines)
{
    try
    {
        var saved = await carts.Update(Cart.Keys.Of(seen.CustomerId)).Set(Cart.Attrs.Lines, lines).IfVersion(seen.Revision).SendAsync(ct);
        Output.Say($"{tab} saved version {saved.Version}");
    }
    catch (ConditionalCheckFailedException)
    {
        Output.Say($"{tab} was refused: the cart is no longer at version {seen.Revision}");
    }
}

// Both tabs loaded the cart at the same version.
if (await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(ct) is { } seenByBoth)
{
    await SaveCartAsync("The first tab", seenByBoth, new OrderLine("MS-02", 2, 39.50m));
    await SaveCartAsync("The second tab", seenByBoth, new OrderLine("LP-04", 1, 54.90m));
}

// The second tab need not give up. Nothing repeats a failed condition by itself, but RetryOnConflictAsync does when asked: it runs this code
// again if it loses to another writer, up to five times, and a little apart. The read belongs inside, because the next run needs the
// version that won. It also decides again: this one keeps what the first tab saved, and adds its line.
var added = await client.RetryOnConflictAsync(
    async ct =>
    {
        var current = await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(ct);
        OrderLine[] lines = [.. current!.Lines, new OrderLine("LP-04", 1, 54.90m)];
        return await carts.Update(Cart.Keys.Of("bob")).Set(Cart.Attrs.Lines, lines).IfVersion(current.Revision).SendAsync(ct);
    },
    ct);
Output.Say($"The second tab read the cart again and saved version {added.Version}");

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("14. Checking out: a snapshot, an order and a delete");

// The checkout page shows the customer and the cart, and two gets could see them at two different moments. TransactGet reads up to 100
// items as of one point in time, for twice the capacity. Each get hands out a slot, which reads its item from the result, with its type.
var checkout = await client.TransactGet()
    .Get(customers, Customer.Keys.Of("bob"), out var buyerSlot)
    .Get(carts, Cart.Keys.Of("bob"), out var cartSlot)
    .SendAsync(ct);

// Delete takes the key from the entity, and the version with it, so a cart that a tab changed in the meantime stays.
if (checkout.Read(buyerSlot).Item is { } buyer && checkout.Read(cartSlot).Item is { } cart)
{
    Output.Say($"{buyer.Name} checks out version {cart.Revision} of the cart, with {cart.Lines.Sum(line => line.Quantity)} item(s)");
    if (await PlaceOrderAsync(buyer.Id, monday.AddDays(1), [.. cart.Lines]) is { } placedFromCart)
    {
        await carts.Delete(cart).SendAsync(ct);
        Output.Say($"Order #{placedFromCart.Number} for {placedFromCart.Total:F2} is placed, and the cart exists: {await carts.Get(cart).Consistent().ItemAsync(ct) is not null}");
    }
}

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("15. What goes over the wire");

// ToRequestJson renders what a chain would send, without sending it: every name and value is a placeholder. It is what to look at when
// DynamoDB rejects something, and what a test can assert on without a client.
Output.Say(products.Update(Product.Keys.Of("KB-01")).Set(Product.Attrs.Price, 79.00m).If(Product.Attrs.Stock.Gt(0)).ToRequestJson());

// ------------------------------------------------------------------------------------------------------------------------
Output.Section("16. Deleting the table");

// This deletes the table with every item in it. It does not wait: the table is gone only some time later, and until then a new table
// of this name cannot be created. An item is deleted by Delete, which is why this one has the long name.
var deleted = await orders.DeleteTableAsync(ct);
Output.Say($"Table {deleted.TableName} is {deleted.TableStatus}");
return 0;
