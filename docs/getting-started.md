# Getting started

DynaSharp is a DynamoDB client for .NET: items are mapped by a source generator, requests go straight to the wire as UTF-8, and the API speaks DynamoDB. This page installs it, creates a client and says where region and credentials come from. [Entities and keys](entities.md) goes on from there.

## Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along.

There is no public feed. Pack a clone of this repository into a folder, make that folder a source, and add the package from it. A clone without a tag packs a prerelease version, so pin the version the folder holds: `--prerelease` takes the newest.

```shell
dotnet pack src/DynaSharp -c Release -o /path/to/packages
dotnet pack src/DynaSharp.Testing -c Release -o /path/to/packages
dotnet nuget add source /path/to/packages --name dynasharp
dotnet add package DynaSharp --version 0.1.0-preview.4
```

The default .NET `.gitignore` holds `*.nupkg`, so a folder source in a repository needs `git add -f`, and a filled package cache hides the loss until a clean machine restores. On Windows `PublishAot` needs the C++ workload of Visual Studio.

## Creating the client

All requests go through `DynamoDbClient`. It is thread-safe, so create one and keep it. With no options, region, endpoint and credentials come from the environment, the way the AWS SDKs read them.

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

`HttpHandler`, `TimeProvider` and `LoggerFactory` are there too, for tests and logging.

## Region

Without `Region`, the client reads `AWS_REGION`, then `AWS_DEFAULT_REGION`, then the `region` setting of the profile in the shared AWS files. The region is required even for an emulator, because it is part of the request signature.

For a local emulator, set `ServiceUrl`, or `AWS_ENDPOINT_URL_DYNAMODB`:

```csharp
using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    ServiceUrl = new Uri("http://localhost:8000"),
    Region = AwsRegion.FromName("us-east-1"),
    Credentials = new StaticCredentialsProvider("local", "local"),
});
```

The repository has an emulator ready: `docker compose -f samples/compose.yaml up -d` starts Dynoxide on `http://localhost:8000`, and the [sample](https://github.com/msallin/DynaSharp/tree/main/samples) runs against it.

## Credentials

Without `Credentials`, the client uses the default chain of the AWS SDKs and takes the first source that has credentials:

1. `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and `AWS_SESSION_TOKEN`
2. the web identity token file named by `AWS_WEB_IDENTITY_TOKEN_FILE`, with `AWS_ROLE_ARN`
3. the profile selected by `AWS_PROFILE` in the shared credentials and config files
4. the container credentials endpoint of ECS and EKS, which is also EKS Pod Identity
5. the instance metadata service of EC2

A source that applies but is broken fails the request and does not fall through to the next one, so a misconfigured pod never signs with the node's role. To use one source only, pass `EnvironmentCredentialsProvider`, `WebIdentityCredentialsProvider`, `ProfileCredentialsProvider`, `ContainerCredentialsProvider`, `InstanceMetadataCredentialsProvider` or `StaticCredentialsProvider`. To test the way through STS without AWS, `StubSts` of `DynaSharp.Testing` stands in for it on a port of the machine.

IRSA on EKS sets those two variables in the pod, so `new DynamoDbClient()` needs only a [region](#region) on top. The client trades the token for credentials at STS with `AssumeRoleWithWebIdentity`, reads the file again for each exchange because the kubelet replaces it, and renews the credentials before they expire. Any other OpenID Connect issuer the account trusts takes the same request. All of this is tested against `StubSts` and has not run on AWS.

Assume role, SSO, `credential_process` and web identity through the profile keys `web_identity_token_file` and `role_arn` are not supported.

## Next steps

- [Entities and keys](entities.md): declare what an item looks like and how its keys are built.
- [Tables](tables.md): open a table, and create it from what the entities declare.
- [Reading and writing](commands.md) and [Queries and scans](queries.md): the operations.
- [Migrating](migrating.md), if you come from the AWS SDK or from EfficientDynamoDb.

The rest of the guide uses a small shop on one table. The full version is a runnable tour: start with [`samples`](https://github.com/msallin/DynaSharp/tree/main/samples).
