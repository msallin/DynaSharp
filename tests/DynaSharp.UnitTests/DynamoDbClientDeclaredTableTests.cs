using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Consumer;

namespace DynaSharp.UnitTests;

/// <summary><see cref="DynamoDbClient.Table{T}()"/>: the table an entity declares, and how <see cref="DynamoDbClientOptions.TableNaming"/> names it.</summary>
public sealed class DynamoDbClientDeclaredTableTests
{
    [Test]
    public async Task Table_EntityWithATable_OpensTheTableItDeclares()
    {
        // Arrange
        using var client = Create(null);

        // Act
        var patrons = client.Table<Patron>();

        // Assert
        await Assert.That(patrons.Name).IsEqualTo("patrons");
    }

    [Test]
    public async Task TableName_OfTheGeneratedEntity_IsTheNameTheClientOpensWithoutTableNaming()
    {
        // Arrange
        using var client = Create(null);

        // Act
        var patrons = client.Table<Patron>();

        // Assert
        await Assert.That(patrons.Name).IsEqualTo(Patron.TableName);
    }

    [Test]
    public async Task Table_TableNaming_OpensTheNameItReturns()
    {
        // Arrange
        using var client = Create(static name => $"dev-{name}");

        // Act
        var patrons = client.Table<Patron>();

        // Assert
        await Assert.That(patrons.Name).IsEqualTo("dev-patrons");
    }

    [Test]
    public async Task Table_ExplicitNameBesideTableNaming_OpensTheNameAsGiven()
    {
        // Arrange
        const string Arn = "arn:aws:dynamodb:eu-central-2:123456789012:table/patrons";
        using var client = Create(static name => $"dev-{name}");

        // Act
        var patrons = client.Table<Patron>(Arn);

        // Assert
        await Assert.That(patrons.Name).IsEqualTo(Arn);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Table_TableNamingReturnsNoName_OpensTheDeclaredTable(string? returned)
    {
        // Arrange
        using var client = Create(_ => returned);

        // Act
        var patrons = client.Table<Patron>();

        // Assert
        await Assert.That(patrons.Name).IsEqualTo("patrons");
    }

    [Test]
    public async Task ResolveTableName_TableNaming_GivesTheNameItReturns()
    {
        // Arrange
        using var client = Create(static name => $"dev-{name}");

        // Act
        var name = client.ResolveTableName("Shop");

        // Assert
        await Assert.That(name).IsEqualTo("dev-Shop");
    }

    [Test]
    public async Task ResolveTableName_NoTableNaming_GivesTheDeclaredName()
    {
        // Arrange
        using var client = Create(null);

        // Act
        var name = client.ResolveTableName("Shop");

        // Assert
        await Assert.That(name).IsEqualTo("Shop");
    }

    [Test]
    public async Task ResolveTableName_TableNamingThatRenamesSomeTables_KeepsTheOthers()
    {
        // Arrange
        using var client = Create(static name => name == "Shop" ? "dev-Shop" : null);

        // Act
        var names = (client.ResolveTableName("Shop"), client.ResolveTableName("Locks"));

        // Assert
        await Assert.That(names).IsEqualTo(("dev-Shop", "Locks"));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ResolveTableName_NoName_ThrowsArgumentException(string? declared)
    {
        // Arrange
        using var client = Create(null);

        // Act
        var exception = Assert.Throws<ArgumentException>(() => client.ResolveTableName(declared!));

        // Assert
        await Assert.That(exception.ParamName).IsEqualTo("declaredName");
    }

    [Test]
    public async Task Table_EntityWithoutATable_ThrowsInvalidOperationExceptionThatNamesBothWays()
    {
        // Arrange
        using var client = Create(static name => $"dev-{name}");

        // Act (what DYNA238 reports at compile time is what this test checks at run time)
#pragma warning disable DYNA238
        var exception = Assert.Throws<InvalidOperationException>(() => client.Table<Subscriber>());
#pragma warning restore DYNA238

        // Assert
        await Assert.That(exception.Message).IsEqualTo(
            "DynaSharp.UnitTests.Testing.Consumer.Subscriber declares no table. Declare one with [DynamoDbEntity(Table = \"...\")] on the type, or with [assembly: DynamoDbDefaults(Table = \"...\")] for every entity of its assembly, or pass the name of the table to this call.");
    }

    [Test]
    public async Task Table_TableNamingOfAClientOfTheFake_WritesToTheNamedTable()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>("dev-patrons");
        using var client = fake.CreateClient(static options => options with { TableNaming = static name => $"dev-{name}" });
        var patron = new Patron("p-1", "Ada");

        // Act
        await client.Table<Patron>().Put(patron).SendAsync();

        // Assert
        await Assert.That(fake.Items<Patron>("dev-patrons")).IsEquivalentTo([patron]);
    }

    private static DynamoDbClient Create(Func<string, string?>? naming) =>
        new(TestClient.Options(new StubHandler()) with { TableNaming = naming }, static _ => null, null, null);
}
