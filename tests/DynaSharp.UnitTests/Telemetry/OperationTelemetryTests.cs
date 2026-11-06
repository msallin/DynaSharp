using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Telemetry;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class OperationTelemetryTests
{
    [Test]
    public async Task RecordResponse_NobodyObserves_DoesNotReadTheCapacity()
    {
        // Arrange
        using var source = new ActivitySource(nameof(OperationTelemetryTests));
        var telemetry = new OperationTelemetry(source, TimeProvider.System, "us-east-1", new Uri("http://localhost"));
        var reads = new CapacityReads();

        // Act
        telemetry.RecordResponse<CountingCall, string>(activity: null, "Users", new CountingCall(reads), "response", metering: false);

        // Assert
        await Assert.That(reads.Count).IsEqualTo(0);
    }

    private readonly struct CountingCall(CapacityReads reads) : IWireCall<string>
    {
        public string OperationName => "GetItem";

        public string ServiceName => "DynamoDB";

        public string SpanName => "DynamoDB.GetItem";

        public string Target => "DynamoDB_20120810.GetItem";

        public int MaxAttempts => 0;

        public string? GetTableName() => "Users";

        public ArraySegment<byte> WriteBody(CallBuffers buffers) => default;

        public bool TryReadFailure(ReadOnlySpan<byte> utf8Json, out string response)
        {
            response = string.Empty;
            return false;
        }

        public string ReadResponse(ReadOnlySpan<byte> utf8Json) => string.Empty;

        public void SetActivityTags(Activity activity)
        {
        }

        public void SetResponseTags(Activity activity, string response)
        {
        }

        public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(string response)
        {
            reads.Count++;
            return null;
        }

        public void DescribeFailure(DynamoDbException failure)
        {
        }

        public void Release()
        {
        }
    }

    private sealed class CapacityReads
    {
        public int Count { get; set; }
    }
}
