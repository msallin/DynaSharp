using System.Globalization;
using System.Net;
using System.Text;
using DynaSharp.FakeServer;
using DynaSharp.Testing;

// Serves one FakeDynamoDb on http://127.0.0.1:<port>/ for clients that are not .NET, such as the Parity Suite (build/parity.sh).
// Usage: dotnet run --project tools/DynaSharp.FakeServer -- --port 8000
// Every Authorization is accepted, no signature is checked. Each answer carries x-amzn-RequestId and x-amz-crc32, as DynamoDB sends them.
var port = ReadPort(args);
var fake = new FakeDynamoDb();
using var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
listener.Start();
Console.WriteLine($"FakeDynamoDb listens on http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

using var registration = stop.Token.Register(listener.Stop);
while (!stop.IsCancellationRequested)
{
    HttpListenerContext context;
    try
    {
        context = await listener.GetContextAsync();
    }
    catch (HttpListenerException) when (stop.IsCancellationRequested)
    {
        break;
    }

    // The fake takes one request at a time under its lock; the connections need not wait for each other.
    _ = Task.Run(() => AnswerAsync(fake, context));
}

static async Task AnswerAsync(FakeDynamoDb fake, HttpListenerContext context)
{
    using var response = context.Response;
    string body;
    using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
    {
        body = await reader.ReadToEndAsync();
    }

    StubResponse answer;
    try
    {
        answer = fake.Answer(context.Request.Headers["X-Amz-Target"] ?? string.Empty, body);
    }
#pragma warning disable CA1031 // A bug of the fake must not leave the client waiting: it answers 500 and goes to the console, where a run log shows it.
    catch (Exception exception)
#pragma warning restore CA1031
    {
        await Console.Error.WriteLineAsync($"The fake failed on {context.Request.Headers["X-Amz-Target"]}: {exception}");
        answer = StubResponse.DynamoDbError("InternalServerError", exception.Message, HttpStatusCode.InternalServerError);
    }

    var bytes = Encoding.UTF8.GetBytes(answer.Body);
    response.StatusCode = (int)answer.StatusCode;
    response.ContentType = "application/x-amz-json-1.0";
    foreach (var (name, value) in answer.Headers ?? new Dictionary<string, string>())
    {
        response.Headers[name] = value;
    }

    response.Headers["x-amz-crc32"] = Crc32.Compute(bytes).ToString(CultureInfo.InvariantCulture);
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
}

static int ReadPort(string[] arguments)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (arguments[i] == "--port")
        {
            return int.Parse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    return 8000;
}
