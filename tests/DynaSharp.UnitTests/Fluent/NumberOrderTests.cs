using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Seeded, so a failure is reproducible.")]
public sealed class NumberOrderTests
{
    // Ascending, by hand: each line is a neighbour of the next at the precision that a double, a decimal or a text comparison would get wrong.
    private static readonly string[] Ascending =
    [
        "-9.9999999999999999999999999999999999999E+125",
        "-1E+100",
        "-123456789012345678901234567890123456.78",
        "-123456789012345678901234567890123456.77",
        "-1000",
        "-999.5",
        "-100",
        "-99",
        "-10.01",
        "-10",
        "-9.99",
        "-1",
        "-0.123",
        "-0.12",
        "-0.1",
        "-1E-129",
        "0",
        "1E-130",
        "0.1",
        "0.12",
        "0.123",
        "1",
        "1.00000000000000000000000000000000000001",
        "9",
        "9.5",
        "10",
        "99",
        "100",
        "123456789012345678901234567890123456.77",
        "123456789012345678901234567890123456.78",
        "1E+100",
        "9.9999999999999999999999999999999999999E+125",
    ];

    [Test]
    public async Task TryAppend_NumbersInAscendingOrder_CompareInThatOrder()
    {
        // Arrange
        var forms = Ascending.Select(Form).ToArray();

        // Act
        var outOfOrder = Enumerable.Range(1, forms.Length - 1).Where(i => forms[i - 1].AsSpan().SequenceCompareTo(forms[i]) >= 0).Select(i => Ascending[i]);

        // Assert
        await Assert.That(outOfOrder).IsEmpty();
    }

    [Test]
    [Arguments("1", "1.0")]
    [Arguments("1", "1.000")]
    [Arguments("100", "1E+2")]
    [Arguments("100", "1e2")]
    [Arguments("100", "10E1")]
    [Arguments("100", "100.0E0")]
    [Arguments("0.5", ".5")]
    [Arguments("0.5", "5E-1")]
    [Arguments("0.5", "+0.5")]
    [Arguments("0.5", "00.500")]
    [Arguments("0", "-0")]
    [Arguments("0", "0.000")]
    [Arguments("0", "0E+5")]
    [Arguments("-12.5", "-125E-1")]
    [Arguments("1000", "1E+3")]
    [Arguments("0.001", "1E-3")]
    [Arguments("0.1", "1E-000000001")]
    public async Task TryAppend_TwoSpellingsOfANumber_HaveOneForm(string first, string second)
    {
        // Arrange and Act
        var forms = (Convert.ToHexString(Form(first)), Convert.ToHexString(Form(second)));

        // Assert
        await Assert.That(forms.Item1).IsEqualTo(forms.Item2);
    }

    [Test]
    [Arguments("")]
    [Arguments("-")]
    [Arguments("+")]
    [Arguments(".")]
    [Arguments("-.")]
    [Arguments("abc")]
    [Arguments("1a")]
    [Arguments("1.2.3")]
    [Arguments("1E")]
    [Arguments("1E+")]
    [Arguments("1E-")]
    [Arguments("E5")]
    [Arguments("1E5.5")]
    [Arguments("1E1234567890")]
    [Arguments("1 ")]
    [Arguments(" 1")]
    [Arguments("0x10")]
    [Arguments("NaN")]
    [Arguments("Infinity")]
    public async Task TryAppend_TextThatIsNoNumber_IsFalseAndWritesNothing(string text)
    {
        // Arrange
        using var buffer = new PooledByteBuffer(64);

        // Act
        var appended = NumberOrder.TryAppend(Encoding.UTF8.GetBytes(text), buffer);

        // Assert
        await Assert.That((appended, buffer.WrittenCount)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task TryAppend_Forms_AppendToWhatIsThere()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(64);
        buffer.Append("x"u8);

        // Act
        NumberOrder.TryAppend("12.50"u8, buffer);

        // Assert
        await Assert.That(Convert.ToHexString(buffer.WrittenSpan)).IsEqualTo("78" + "02" + "80000002" + "313235");
    }

    [Test]
    public async Task TryAppend_NegativeNumber_InvertsSignExponentAndDigits()
    {
        // Arrange and Act
        var form = Convert.ToHexString(Form("-12.50"));

        // Assert (the digits 1, 2 and 5 inverted, and the end above every digit)
        await Assert.That(form).IsEqualTo("00" + "7FFFFFFD" + "CECDCA" + "FF");
    }

    [Test]
    public async Task TryAppend_RandomNumbers_CompareAsTheirExactValues()
    {
        // Arrange: exact values as a scaled BigInteger, which is the independent oracle, in every spelling
        var random = new Random(20261101);
        var numbers = Enumerable.Range(0, 2000).Select(_ => RandomNumber(random)).ToArray();

        // Act
        var wrong = 0;
        for (var i = 1; i < numbers.Length; i++)
        {
            var actual = Math.Sign(Form(numbers[i - 1].Text).AsSpan().SequenceCompareTo(Form(numbers[i].Text)));
            wrong += actual == Math.Sign(numbers[i - 1].Value.CompareTo(numbers[i].Value)) ? 0 : 1;
        }

        // Assert
        await Assert.That(wrong).IsEqualTo(0);
    }

    private static (string Text, BigInteger Value) RandomNumber(Random random)
    {
        // Up to 38 digits with up to 20 decimals: the value is that many digits times 10^-20.
        var digits = random.Next(1, 39);
        var mantissa = new StringBuilder();
        for (var i = 0; i < digits; i++)
        {
            mantissa.Append((char)('0' + random.Next(10)));
        }

        var unscaled = BigInteger.Parse(mantissa.ToString(), CultureInfo.InvariantCulture);
        var scale = random.Next(0, 21);
        var negative = random.Next(2) == 0;
        var value = unscaled * BigInteger.Pow(10, 20 - scale) * (negative ? -1 : 1);
        var text = (negative ? "-" : string.Empty) + mantissa + (random.Next(2) == 0 ? "E" : "e") + (-scale).ToString(CultureInfo.InvariantCulture);
        return (text, value);
    }

    private static byte[] Form(string number)
    {
        using var buffer = new PooledByteBuffer(64);
        return NumberOrder.TryAppend(Encoding.UTF8.GetBytes(number), buffer) ? buffer.WrittenSpan.ToArray() : throw new InvalidOperationException(number);
    }
}
