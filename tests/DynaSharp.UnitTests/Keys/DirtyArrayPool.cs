using System.Buffers;

namespace DynaSharp.UnitTests.Keys;

/// <summary>An array pool that hands out arrays full of leftovers from earlier users, as a real pool does, and keeps count.</summary>
internal sealed class DirtyArrayPool : ArrayPool<byte>
{
    public const byte Leftover = 0xAB;

    public int Rented { get; private set; }

    public override byte[] Rent(int minimumLength)
    {
        Rented++;
        var array = new byte[minimumLength + 7];
        array.AsSpan().Fill(Leftover);
        return array;
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
    }
}
