using System.Net;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Exceptions;

public sealed class TransactionCanceledExceptionTests
{
    [Test]
    public async Task Constructor_ReasonsGiven_ExposesThemInTheOrderGiven()
    {
        // Arrange
        CancellationReason[] reasons = [new() { Code = "None" }, new() { Code = "ConditionalCheckFailed" }, new() { Code = "TransactionConflict" }];

        // Act
        var exception = new TransactionCanceledException("Transaction cancelled.", reasons);

        // Assert
        await Assert.That(string.Join(',', exception.CancellationReasons.Select(reason => reason.Code))).IsEqualTo("None,ConditionalCheckFailed,TransactionConflict");
    }

    [Test]
    public async Task Constructor_ReasonsGiven_KeepsTheListItself()
    {
        // Arrange
        IReadOnlyList<CancellationReason> reasons = [new() { Code = "None" }];

        // Act
        var exception = new TransactionCanceledException("Transaction cancelled.", reasons);

        // Assert
        await Assert.That(exception.CancellationReasons).IsSameReferenceAs(reasons);
    }

    [Test]
    public async Task Constructor_NoReasons_ReasonsAreEmptyAndNotNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new TransactionCanceledException("Transaction cancelled.");

        // Assert
        await Assert.That(exception.CancellationReasons).IsEmpty();
    }

    [Test]
    public async Task Constructor_NullReasons_ReasonsAreEmptyAndNotNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new TransactionCanceledException("Transaction cancelled.", null);

        // Assert
        await Assert.That(exception.CancellationReasons).IsEmpty();
    }

    [Test]
    public async Task Constructor_RequestIdAndStatusGiven_SetsThem()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new TransactionCanceledException("Transaction cancelled.", [], "A1B2C3", HttpStatusCode.Conflict);

        // Assert
        await Assert.That((exception.RequestId, exception.StatusCode)).IsEqualTo(("A1B2C3", HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Constructor_Always_ReportsTheWireCodeAsErrorCode()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new TransactionCanceledException("Transaction cancelled.");

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("TransactionCanceledException");
    }

    [Test]
    [Arguments("None", TransactionFailure.None)]
    [Arguments("ConditionalCheckFailed", TransactionFailure.ConditionalCheckFailed)]
    [Arguments("ItemCollectionSizeLimitExceeded", TransactionFailure.ItemCollectionSizeLimitExceeded)]
    [Arguments("TransactionConflict", TransactionFailure.TransactionConflict)]
    [Arguments("ProvisionedThroughputExceeded", TransactionFailure.Throttled)]
    [Arguments("ThrottlingError", TransactionFailure.Throttled)]
    [Arguments("ValidationError", TransactionFailure.ValidationError)]
    [Arguments("ACodeOfTheFuture", TransactionFailure.Unknown)]
    [Arguments(null, TransactionFailure.Unknown)]
    public async Task ReasonFor_EveryCode_MapsToItsFailure(string? code, TransactionFailure expected)
    {
        // Arrange
        var failure = new TransactionCanceledException("Cancelled.", [new CancellationReason { Code = code }]);

        // Act
        var reason = failure.ReasonFor(new TransactMarker(0));

        // Assert
        await Assert.That(reason).IsEqualTo(expected);
    }

    [Test]
    public async Task ReasonFor_DefaultMarker_ThrowsArgumentExceptionSayingWhereAMarkerComesFrom()
    {
        // Arrange
        var failure = new TransactionCanceledException("Cancelled.", [new CancellationReason { Code = "None" }]);

        // Act
        var act = () => failure.ReasonFor(default);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessage("This marker is a default value. A marker comes from an action of a transaction, through its out parameter. (Parameter 'marker')", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReasonFor_NoReasonForTheAction_IsUnknown()
    {
        // Arrange (DynamoDB sends no reasons for a cancellation that is not tied to an action)
        var failure = new TransactionCanceledException("Cancelled.");

        // Act
        var reason = failure.ReasonFor(new TransactMarker(2));

        // Assert
        await Assert.That(reason).IsEqualTo(TransactionFailure.Unknown);
    }

    [Test]
    public async Task ReasonFor_DefaultMarker_Throws()
    {
        // Arrange
        var failure = new TransactionCanceledException("Cancelled.", [new CancellationReason { Code = "None" }]);

        // Act
        var thrown = Assert.Throws<ArgumentException>(() => failure.ReasonFor(default));

        // Assert
        await Assert.That(thrown.ParamName).IsEqualTo("marker");
    }

    [Test]
    public async Task ItemFor_DefaultMarker_Throws()
    {
        // Arrange
        var failure = new TransactionCanceledException("Cancelled.", [new CancellationReason { Code = "None" }]);

        // Act
        var thrown = Assert.Throws<ArgumentException>(() => failure.ItemFor(default));

        // Assert
        await Assert.That(thrown.ParamName).IsEqualTo("marker");
    }

    [Test]
    public async Task ItemFor_NoReasonForTheAction_IsNull()
    {
        // Arrange
        var failure = new TransactionCanceledException("Cancelled.");

        // Act
        var item = failure.ItemFor(new TransactMarker(0));

        // Assert
        await Assert.That(item).IsNull();
    }

    [Test]
    public async Task ItemFor_ReasonFromADictionary_MakesAnItem()
    {
        // Arrange (the constructor is public, for a fake of the service, and a reason holds any attributes)
        var attributes = new Dictionary<string, AttributeValue> { ["pk"] = "a" };
        var failure = new TransactionCanceledException("Cancelled.", [new CancellationReason { Code = "ConditionalCheckFailed", Item = attributes }]);

        // Act
        var item = failure.ItemFor(new TransactMarker(0));

        // Assert
        await Assert.That(item!.GetString("pk")).IsEqualTo("a");
    }

    [Test]
    public async Task Failures_MixedReasons_ListsTheActionsThatFailedWithTheirIndex()
    {
        // Arrange
        var failure = new TransactionCanceledException(
            "Cancelled.",
            [
                new CancellationReason { Code = "None" },
                new CancellationReason { Code = "ConditionalCheckFailed" },
                new CancellationReason { Code = "None" },
                new CancellationReason { Code = "ThrottlingError" },
                new CancellationReason { Code = "ACodeOfTheFuture" },
            ]);

        // Act
        var failures = failure.Failures;

        // Assert
        await Assert.That(failures).IsEquivalentTo(
        [
            new TransactionActionFailure(1, TransactionFailure.ConditionalCheckFailed),
            new TransactionActionFailure(3, TransactionFailure.Throttled),
            new TransactionActionFailure(4, TransactionFailure.Unknown),
        ]);
    }

    [Test]
    public async Task Failures_NoReasons_IsEmpty()
    {
        // Arrange
        var failure = new TransactionCanceledException("Cancelled.", null);

        // Act
        var failures = failure.Failures;

        // Assert
        await Assert.That((failures.Count, failure.CancellationReasons.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Failures_OfAnErrorOfTheService_ComeFromItsReasons()
    {
        // Arrange (the error vectors pin the mapping; this is the way there, through a client)
        const string Body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","Message":"Cancelled","CancellationReasons":[{"Code":"None"},{"Code":"TransactionConflict","Message":"Conflict"}]}""";
        using var handler = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.BadRequest, Body = Body });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite().Delete(shop, "a").Delete(shop, "b").SendAsync());

        // Assert
        await Assert.That(failure.Failures.Single()).IsEqualTo(new TransactionActionFailure(1, TransactionFailure.TransactionConflict));
    }
}
