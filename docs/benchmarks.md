# Benchmarks

Allocations are exact. All clients run on the same canned responses with no network, so these numbers show the client's own cost.

The three tables are on the [front page of the repository](https://github.com/msallin/DynaSharp#benchmarks), which is also the [home page of this site](index.md#benchmarks): bytes allocated per call by the mapped APIs of DynaSharp, EfficientDynamoDb and the AWS SDK's `DynamoDBContext`, the low-level APIs of the three in KB, and the API levels of DynaSharp in bytes.

## Why the rows differ

- The item has a set of tags. The first table maps it to a `HashSet<string>` in all three clients, so that they compare. The third maps it to a `string[]`, which is lighter, so a read costs less there, and a `PutItem`, which only walks the set, costs the same.
- A read costs more than a write, because a read builds the item it returns and a write has only a request to fill. A query of 100 items is mostly 100 times what one item costs.
- A generated entity reads straight into its properties. `Table<Item>` and the request API hold each attribute as an `AttributeValue`, which is the step between the first column of the third table and the other two. The `Item` of a `PutItem` is the object of the caller, so that cell is n/a.
- The second table and the last column of the third are the same request API, in KB and in bytes. They come from two benchmark classes, and the `Query` of the third runs in an `async` method of the benchmark, which adds its `Task`.

## Running them

To run them, from the repository root:

```shell
dotnet run -c Release --project benchmarks/DynaSharp.Benchmarks
```

The benchmarks live in [`benchmarks/DynaSharp.Benchmarks`](https://github.com/msallin/DynaSharp/tree/main/benchmarks/DynaSharp.Benchmarks).
