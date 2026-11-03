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

    private static FakeTable Table(string name) => new(name, new KeySchema("pk", ScalarAttributeType.String), [], DateTimeOffset.UnixEpoch);
}
