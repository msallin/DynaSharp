using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>What the generator knows of an integer type of a key input.</summary>
public sealed class IntegerTypeTests
{
    [Test]
    [Arguments("sbyte", 3)]
    [Arguments("byte", 3)]
    [Arguments("short", 5)]
    [Arguments("ushort", 5)]
    [Arguments("int", 10)]
    [Arguments("uint", 10)]
    [Arguments("long", 19)]
    [Arguments("ulong", 20)]
    public async Task MaxDigits_Type_IsTheDigitsOfItsMaximum(string keyword, int expected)
    {
        // Arrange
        var type = new IntegerType(keyword, IsUnsigned: false, IsFullWidth: false);

        // Act
        var digits = type.MaxDigits;

        // Assert
        await Assert.That(digits).IsEqualTo(expected);
    }
}
