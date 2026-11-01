# DynaSharp sample

A runnable tour of `Table<T>` and generated entities: the data layer of a small online shop on one table, one section per
thing a shop needs from DynamoDB. Read [`DynaSharp.Sample/ShopModel.cs`](DynaSharp.Sample/ShopModel.cs), then
[`DynaSharp.Sample/Program.cs`](DynaSharp.Sample/Program.cs) from top to bottom.

Start a local DynamoDB emulator, then run the sample:

```
docker compose -f samples/compose.yaml up -d
dotnet run --project samples/DynaSharp.Sample
```

That uses Dynoxide on `http://localhost:8000`. To try DynamoDB Local on `http://localhost:8001` instead:

```
docker compose -f samples/compose.yaml --profile dynamodb-local up -d
AWS_ENDPOINT_URL_DYNAMODB=http://localhost:8001 dotnet run --project samples/DynaSharp.Sample
```

The sample reads the endpoint from `AWS_ENDPOINT_URL_DYNAMODB`. Each run works in a table of its own and leaves it behind for
you to look at, so you can run it as often as you like. Stopping the emulators removes the tables:
`docker compose -f samples/compose.yaml --profile dynamodb-local down`.
