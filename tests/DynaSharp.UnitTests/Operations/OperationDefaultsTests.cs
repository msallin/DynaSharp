using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Operations;

/// <summary>
/// The target prefix and the service name of an operation: DynamoDB's unless the operation overrides them, as the operations of
/// DynamoDB Streams do.
/// </summary>
public sealed class OperationDefaultsTests
{
    [Test]
    public async Task TargetPrefix_OperationWithoutOverride_IsTheDynamoDbPrefix()
    {
        // Arrange (nothing to arrange)

        // Act
        var prefix = TargetPrefixOf<GetItemOperation, GetItemRequest, GetItemResponse>();

        // Assert
        await Assert.That(prefix).IsEqualTo("DynamoDB_20120810.");
    }

    [Test]
    public async Task ServiceName_OperationWithoutOverride_IsDynamoDb()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = ServiceNameOf<GetItemOperation, GetItemRequest, GetItemResponse>();

        // Assert
        await Assert.That(name).IsEqualTo("DynamoDB");
    }

    [Test]
    public async Task TargetPrefix_StreamsOperations_IsTheStreamsPrefixThatBotocoreUses()
    {
        // Arrange
        var botocorePrefixes = new[]
        {
            OperationVectors.Request("ListStreams", "OneTable").Target,
            OperationVectors.Request("DescribeStream", "Minimal").Target,
            OperationVectors.Request("GetShardIterator", "Latest").Target,
            OperationVectors.Request("GetRecords", "WithLimit").Target,
        };

        // Act
        var prefixes = new[]
        {
            TargetPrefixOf<ListStreamsOperation, ListStreamsRequest, ListStreamsResponse>() + ListStreamsOperation.Name,
            TargetPrefixOf<DescribeStreamOperation, DescribeStreamRequest, DescribeStreamResponse>() + DescribeStreamOperation.Name,
            TargetPrefixOf<GetShardIteratorOperation, GetShardIteratorRequest, GetShardIteratorResponse>() + GetShardIteratorOperation.Name,
            TargetPrefixOf<GetRecordsOperation, GetRecordsRequest, GetRecordsResponse>() + GetRecordsOperation.Name,
        };

        // Assert
        await Assert.That(prefixes).IsEquivalentTo(botocorePrefixes);
    }

    [Test]
    public async Task ServiceName_StreamsOperations_IsTheServiceIdOfTheModel()
    {
        // Arrange (nothing to arrange)

        // Act
        var names = new[]
        {
            ServiceNameOf<ListStreamsOperation, ListStreamsRequest, ListStreamsResponse>(),
            ServiceNameOf<DescribeStreamOperation, DescribeStreamRequest, DescribeStreamResponse>(),
            ServiceNameOf<GetShardIteratorOperation, GetShardIteratorRequest, GetShardIteratorResponse>(),
            ServiceNameOf<GetRecordsOperation, GetRecordsRequest, GetRecordsResponse>(),
        };

        // Assert
        await Assert.That(names.Distinct()).IsEquivalentTo(["DynamoDB Streams"]);
    }

    [Test]
    public async Task StreamsTargetPrefix_Always_MatchesTheServiceModel()
    {
        // Arrange (nothing to arrange)

        // Act
        var prefix = WireProtocol.StreamsTargetPrefix;

        // Assert
        await Assert.That(prefix).IsEqualTo("DynamoDBStreams_20120810.");
    }

    private static string TargetPrefixOf<TOperation, TRequest, TResponse>()
        where TOperation : IOperation<TRequest, TResponse> => TOperation.TargetPrefix;

    private static string ServiceNameOf<TOperation, TRequest, TResponse>()
        where TOperation : IOperation<TRequest, TResponse> => TOperation.ServiceName;
}
