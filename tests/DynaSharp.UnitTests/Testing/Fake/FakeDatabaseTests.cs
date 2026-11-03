using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class FakeDatabaseTests
{
    [Test]
    public async Task Delete_Table_FreesTheName()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        db.Create(Table("Users"));

        // Act
        db.Delete("Users");

        // Assert
        await Assert.That(db.Tables.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Reset_Tables_DropsThemAll()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        db.Create(Table("Users"));
        db.Create(Table("Orders"));

        // Act
        db.Reset();

        // Assert
        await Assert.That(db.Tables.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetTable_Missing_ThrowsTheMessageOfAnItemOperation()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var act = () => db.GetTable("Users");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Cannot do operations on a non-existent table", StringComparison.Ordinal);
    }

    [Test]
    public async Task Create_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var act = () => db.Create(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Reset_Streams_DropsThemAll()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        var table = Table("Users");
        db.Create(table);
        db.EnableStream(table, StreamViewType.KeysOnly);

        // Act
        db.Reset();

        // Assert
        await Assert.That(db.Streams.Count).IsEqualTo(0);
    }

    [Test]
    public async Task EnableStream_Table_MakesItTheLatestStreamOfTheTable()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        var table = Table("Users");

        // Act
        var stream = db.EnableStream(table, StreamViewType.NewImage);

        // Assert
        await Assert.That((table.LatestStream, db.FindStream(stream.Arn))).IsEqualTo((stream, stream));
    }

    [Test]
    public async Task EnableStream_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var act = () => db.EnableStream(null!, StreamViewType.KeysOnly);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task FindStream_Unknown_ThrowsResourceNotFound()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var act = () => db.FindStream("arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Requested resource not found: Stream: arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x not found", StringComparison.Ordinal);
    }

    private static FakeTable Table(string name) => new(name, new KeySchema("pk", ScalarAttributeType.String), [], DateTimeOffset.UnixEpoch);
}
