# Getting started

Install DynaSharp, run a first program against a local emulator, and meet the shop that the rest of the guide uses. New to DynamoDB? [DynamoDB in short](glossary.md) explains the terms.

## Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along, and has no other dependencies.

```shell
dotnet add package DynaSharp
dotnet add package DynaSharp.Testing        # in a test project
dotnet add package DynaSharp.Coordination   # locks and leader election, optional
```

## Run a local emulator

The repository has two emulators ready in [`samples/compose.yaml`](https://github.com/msallin/DynaSharp/blob/main/samples/compose.yaml): Dynoxide, a native emulator that starts in milliseconds, and DynamoDB Local, the emulator of AWS, in memory and with one database for all credentials and regions.

```shell
docker compose -f samples/compose.yaml up -d                           # Dynoxide on http://localhost:8000
docker compose -f samples/compose.yaml --profile dynamodb-local up -d  # DynamoDB Local on http://localhost:8001
```

A client for an emulator names it in `ServiceUrl`. It still needs a region and credentials, because every request is signed, but an emulator checks neither, so any value does. `DynamoDbClientOptions.ForEmulator` sets all three, with the region `us-east-1` and the keys `local` and `local`:

```csharp
using var client = new DynamoDbClient(DynamoDbClientOptions.ForEmulator(new Uri("http://localhost:8000")));
```

The rest is a `with`, as in `DynamoDbClientOptions.ForEmulator(url) with { MaxAttempts = 1 }`.

DynamoDB Local started without `-sharedDb` keeps one database per access key and region. A tool or a program that uses another region or other keys sees other tables, and none that this client created: the AWS CLI with the region of its profile, for one. `ForEmulator` uses the same values on every call, so two programs that both use it meet. A tool takes `us-east-1` and the keys `local` and `local` too, or the client takes the region of the tool with the second parameter:

```csharp
var options = DynamoDbClientOptions.ForEmulator(new Uri("http://localhost:8000"), "eu-central-2");
```

The compose file starts DynamoDB Local with `-sharedDb`, one database for all of them.

The environment does the same without code: with `AWS_ENDPOINT_URL_DYNAMODB=http://localhost:8000`, `AWS_REGION` and `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` set, `new DynamoDbClient()` goes to the emulator, and the same build goes to AWS where they are not set. An option that is set wins over its variable.

An http endpoint on another host, such as `http://dynamodb-local:8000` in a compose network, works the same. The requests and the session token then travel unencrypted, and the client logs a warning once (event 9, see [Observability](observability.md#logs)).

> [!TIP]
> DynamoDB Local runs [time to live](glossary.md#time-to-live-ttl): its sweeper deletes items whose expiry attribute holds a past date, which looks like a read that finds nothing. Give test data dates in the future, or turn time to live off for such a test.

## A first program

This is the whole `Program.cs` of a console app (`dotnet new console`, then `dotnet add package DynaSharp`), against Dynoxide from above:

```csharp
using DynaSharp;
using DynaSharp.Mapping;

public static class Program
{
    public static async Task Main()
    {
        using var client = new DynamoDbClient(DynamoDbClientOptions.ForEmulator(new Uri("http://localhost:8000")));

        var todos = client.Table<TodoItem>("Todos");
        await todos.CreateTable().IfNotExists().WaitUntilActive().SendAsync();

        await todos.Put(new TodoItem { UserId = "alice", Number = 1, Title = "Buy milk" }).SendAsync();
        await todos.Put(new TodoItem { UserId = "alice", Number = 2, Title = "Call Bob", Done = true }).SendAsync();

        TodoItem? first = await todos.Get(TodoItem.Keys.Of("alice", 1)).ItemAsync();
        Console.WriteLine($"First of alice: {first?.Title}");

        await foreach (var todo in todos.Query(TodoItem.Keys.Pk.Of("alice")).ItemsAsync())
        {
            Console.WriteLine($"#{todo.Number} {todo.Title}, done: {todo.Done}");
        }
    }
}

[DynamoDbEntity]
[Discriminator("TODO")]
[PartitionKey("USER#{UserId}")]
[SortKey("TODO#{Number:d6}")]
public partial record TodoItem
{
    public required string UserId { get; init; }

    public required int Number { get; init; }

    public required string Title { get; init; }

    public bool Done { get; init; }
}
```

What happens:

- **The entity.** `TodoItem` is `partial`, and the source generator writes the code that reads and writes it as an [item](glossary.md#item), plus `Keys`, which builds its keys, and `Attrs`, a typed handle per [attribute](glossary.md#attribute). The [partition key](glossary.md#partition-key) of both items is `USER#alice`, and the [sort key](glossary.md#sort-key) is `TODO#000001` and `TODO#000002`, so they sit in one partition in the order of their numbers. `_type` holds `TODO`, which tells them apart from other types in the same table. [Entities and keys](entities.md) has the rest.
- **The table.** Opening a table makes no request. `CreateTable()` creates it from what the entity declares, `IfNotExists()` leaves a table that is there alone, so the program runs again, and `WaitUntilActive()` waits until the table takes writes. [Tables](tables.md) says more.
- **The commands.** Every chain ends in the terminal that sends it. A `Put` replaces the whole item, `ItemAsync` gives null when there is none, and `ItemsAsync` follows the pages of the `Query` for you. The terminals take a `CancellationToken`. [Reading and writing](commands.md) and [Queries and scans](queries.md) go on from here.

## Configure the client

All requests go through `DynamoDbClient`. It is thread-safe, so create one and keep it. With no options, region, endpoint and credentials come from the environment, the way the AWS SDKs read them, see [Region](#region) and [Credentials](#credentials).

```csharp
using DynaSharp;

using var client = new DynamoDbClient();
```

Every setting can be given explicitly:

```csharp
using DynaSharp;
using DynaSharp.Credentials;

using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    Region = AwsRegion.FromName("eu-central-1"),
    Credentials = new ProfileCredentialsProvider("shop"),
    MaxAttempts = 4,
    AttemptTimeout = TimeSpan.FromSeconds(30),
});
```

`HttpHandler`, `TimeProvider` and `LoggerFactory` are there too, for tests and logging. A handler of your own follows the rules in [Observability](observability.md#a-handler-of-your-own).

## The shop

The rest of the guide uses a small online shop, all of it in one table named `Shop`. Its types are in [`ShopModel.cs`](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/ShopModel.cs) of the sample: `Product`, `Customer`, `Order`, `Cart` and `EmailClaim`, with `Address` and `OrderLine` as documents inside them. Where a page uses `orders`, `customers` or `shop`, they are these:

```csharp
var products = client.Table<Product>("Shop");
var customers = client.Table<Customer>("Shop");
var orders = client.Table<Order>("Shop");
var carts = client.Table<Cart>("Shop");
var emails = client.Table<EmailClaim>("Shop");
var shop = client.Table("Shop", "pk", "sk");
var byStatus = orders.Index(Order.Indexes.ByStatus);
```

| `pk` | `sk` | What |
|---|---|---|
| `PRODUCT#KB-01` | `DETAILS` | a product |
| `CUSTOMER#alice` | `PROFILE` | a customer |
| `CUSTOMER#alice` | `ORDER#000001` | her orders, in her partition, so one `Query` reads them |
| `CUSTOMER#alice` | `CART` | her cart, which two browser tabs can edit at once |
| `EMAIL#a@example.ch` | `CLAIM` | who holds an email address, which keeps them unique |

Each entity opens the table under its own type, and `shop` opens it without a model, as a `Table<Item>`. `shop.Of<Cart>()` is the same table as `carts`, which lets code that holds `shop` stay with one name. `byStatus` reads the [global index](glossary.md#global-secondary-index-gsi) `ByStatus` of `Order`. One `CreateTable()` creates the table with the indexes of all five:

```csharp
await orders.CreateTable()
    .Including<Product>()
    .Including<Customer>()
    .Including<Cart>()
    .Including<EmailClaim>()
    .IfNotExists()
    .WaitUntilActive()
    .SendAsync(ct);
```

The [sample](https://github.com/msallin/DynaSharp/tree/main/samples) runs all of it, from top to bottom, against an emulator.

## In a service

In ASP.NET Core or any other host, register the client as a singleton, so that its pooled connections and cached credentials live as long as the process. The container disposes it when the host stops.

- Put the tables in a singleton of their own. A table is small and immutable, and opening one makes no request.
- Pass `LoggerFactory` from the container, so the client can log what happens outside a single request, such as where the credentials came from and a refresh that failed.
- Create the table in an `IHostedService`. The host runs its `StartAsync` before the server takes requests, and a failure stops the host:

```csharp
using DynaSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class ShopTables(DynamoDbClient client)
{
    public Table<Order> Orders { get; } = client.Table<Order>("Shop");

    public Table<Customer> Customers { get; } = client.Table<Customer>("Shop");
}

public sealed class CreateShopTable(ShopTables tables) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await tables.Orders.CreateTable().Including<Customer>().IfNotExists().WaitUntilActive().SendAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class ShopServices
{
    public static IServiceCollection AddShop(this IServiceCollection services) => services
        .AddSingleton(provider => new DynamoDbClient(new DynamoDbClientOptions
        {
            LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
        }))
        .AddSingleton<ShopTables>()
        .AddHostedService<CreateShopTable>();
}
```

Call `builder.Services.AddShop()` in `Program.cs`, and inject `ShopTables` like any other service.

For a second client with other settings, such as one for writes that must not be retried, start from the options of the first: `new DynamoDbClient(client.Options with { MaxAttempts = 1 })`. It shares the credentials provider and with it the cached credentials. Register it with `AddKeyedSingleton`, so that both can be injected.

## Native AOT

An application with `<PublishAot>true</PublishAot>` publishes as usual, `dotnet publish -c Release -r linux-x64`. DynaSharp needs nothing extra, because the mapping comes from the source generator and nothing is reflected or generated at run time.

- Native AOT compiles for the operating system it runs on. On Windows it needs the Visual Studio workload "Desktop development with C++", which brings the native linker.
- CI publishes [`tests/DynaSharp.AotSmoke`](https://github.com/msallin/DynaSharp/tree/main/tests/DynaSharp.AotSmoke) with Native AOT, where any trim or AOT warning fails the publish, and runs it through reads, writes, errors, converters and telemetry.

## Region

Without `Region`, the client reads `AWS_REGION`, then `AWS_DEFAULT_REGION`, then the `region` setting of the profile in the shared AWS files. The region is required even for an emulator, because it is part of the request signature. For the endpoint of an emulator, see [Run a local emulator](#run-a-local-emulator).

## Credentials

Without `Credentials`, the client uses the default chain of the AWS SDKs and takes the first source that has credentials:

1. `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and `AWS_SESSION_TOKEN`, with `AWS_CREDENTIAL_EXPIRATION` for temporary credentials, which are then read again before that time
2. the web identity token file named by `AWS_WEB_IDENTITY_TOKEN_FILE`, with `AWS_ROLE_ARN`
3. the profile selected by `AWS_PROFILE`, or `default` when it is unset or empty, in the shared credentials and config files, which may assume a role, use SSO, run a `credential_process` or hold keys, in the order of the AWS CLI
4. the container credentials endpoint of ECS and EKS, which is also EKS Pod Identity
5. the instance metadata service of EC2

A source that applies but is broken fails the request and does not fall through to the next one, so a misconfigured pod never signs with the node's role. To use one source only, pass `EnvironmentCredentialsProvider`, `WebIdentityCredentialsProvider`, `ProfileCredentialsProvider`, `AssumeRoleCredentialsProvider`, `SsoCredentialsProvider`, `ContainerCredentialsProvider`, `InstanceMetadataCredentialsProvider` or `StaticCredentialsProvider`.

[Credentials](credentials.md) covers IRSA, a `credential_process`, an assumed role and IAM Identity Center (SSO), and what fails when a source is broken.

## Next steps

- [Entities and keys](entities.md): declare what an item looks like and how its keys are built.
- [Tables](tables.md): open a table, and create it from what the entities declare.
- [Reading and writing](commands.md) and [Queries and scans](queries.md): the operations.
- [Migrating](migrating.md), if you come from the AWS SDK or from EfficientDynamoDb, and [How DynaSharp compares](comparison.md) for what each of them has.
- [Compatibility](compatibility.md): what a release promises.
