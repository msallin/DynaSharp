using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Operations;

public sealed class EnumWireNamesTests
{
    [Test]
    public async Task TryGetValue_NameOfAMember_ReturnsTheMember()
    {
        // Arrange
        var names = CreateKeyTypes();

        // Act
        var found = names.TryGetValue("RANGE", out var value);

        // Assert
        await Assert.That((found, value)).IsEqualTo((true, KeyType.Range));
    }

    [Test]
    public async Task TryGetName_Member_ReturnsTheName()
    {
        // Arrange
        var names = CreateKeyTypes();

        // Act
        var found = names.TryGetName(KeyType.Range, out var name);

        // Assert
        await Assert.That((found, name)).IsEqualTo((true, "RANGE"));
    }

    [Test]
    public async Task Constructor_TwoMembersWithTheSameName_Throws()
    {
        // Arrange

        // Act
        var act = () => new EnumWireNames<KeyType>(nameof(KeyType), (KeyType.Hash, "KEY"), (KeyType.Range, "KEY"));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Constructor_TheSameMemberTwice_Throws()
    {
        // Arrange

        // Act
        var act = () => new EnumWireNames<KeyType>(nameof(KeyType), (KeyType.Hash, "HASH"), (KeyType.Hash, "RANGE"));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    // Tables are built here and not taken from WireNames, whose static tables exist before a test runs.
    private static EnumWireNames<KeyType> CreateKeyTypes() =>
        new(nameof(KeyType), (KeyType.Hash, "HASH"), (KeyType.Range, "RANGE"));
}
