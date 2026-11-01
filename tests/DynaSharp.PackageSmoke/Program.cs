using DynaSharp;
using DynaSharp.Credentials;
using DynaSharp.PackageSmoke;

// Uses the library from the package to prove that it loads and runs. That the generator reached the compiler is checked at build
// time, by the CheckGeneratorComesFromThePackage target in the project file; a mapped type that compiles and runs proves it works.
var item = new Item();
item.Add("pk", "user-1");
var region = AwsRegion.FromName("eu-central-1");

var parcel = new Parcel("p-1", 3, ParcelState.Sent);
var mapped = Item.From(parcel);

if (item.GetString("pk") != "user-1" || region.Name != "eu-central-1")
{
    Console.Error.WriteLine("The DynaSharp package did not behave as built.");
    return 1;
}

if (mapped.GetString("_type") != "PARCEL" || mapped.GetString("State") != "Sent" || mapped.To<Parcel>() != parcel)
{
    Console.Error.WriteLine("The generated code of the DynaSharp package did not behave as built.");
    return 1;
}

// A generated entity through Table<T>, Keys and Attrs, down to the wire and back.
using var handler = new OneItemHandler();
using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    Region = region,
    Credentials = new StaticCredentialsProvider("smoke", "smoke"),
    HttpHandler = handler,
});
var stops = client.Table<Stop>("Rounds");
var stop = new Stop { Round = "r-7", Number = 12, State = ParcelState.Packed };

await stops.Put(stop).IfNotExists().SendAsync();
var written = handler.LastBody;
var read = await stops.Get(Stop.Keys.Of("r-7", 12)).Consistent().SendAsync();
var keyed = handler.LastBody;

handler.UpdateResponse = """{"Attributes":{"Attempts":{"N":"1"}}}""";
var attempts = await stops.Update(Stop.Keys.Of("r-7", 12))
    .Add(Stop.Attrs.Attempts, 1)
    .If(Stop.Attrs.State.Eq(ParcelState.Packed))
    .Return(Stop.Attrs.Attempts)
    .SendAsync();

var wrote = written.Contains("\"pk\":{\"S\":\"ROUND#r-7\"},\"sk\":{\"S\":\"STOP#012\"}", StringComparison.Ordinal) && written.Contains("\"bystatepk\":{\"S\":\"STATE#Packed\"}", StringComparison.Ordinal);
var asked = keyed.Contains("\"Key\":{\"pk\":{\"S\":\"ROUND#r-7\"},\"sk\":{\"S\":\"STOP#012\"}}", StringComparison.Ordinal);
var conditioned = handler.LastBody.Contains("{\"S\":\"Packed\"}", StringComparison.Ordinal);
if (!wrote || !asked || !conditioned || read.Item != stop || attempts != 1 || Stop.Keys.Sk.Parse("STOP#012").Number != 12)
{
    Console.Error.WriteLine("Table<T> over a generated entity of the DynaSharp package did not behave as built.");
    return 1;
}

Console.WriteLine("DynaSharp package smoke test passed.");
return 0;
