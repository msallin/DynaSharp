using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ArrayBuilderTests
{
    [Test]
    public async Task ToArray_MoreElementsThanInitialCapacity_ReturnsAllInOrder()
    {
        // Arrange
        var builder = new ArrayBuilder<int>(new CountingArrayPool<int>());
        int[] result;

        // Act
        try
        {
            for (var i = 0; i < 100; i++)
            {
                builder.Add(i);
            }

            result = builder.ToArray();
        }
        finally
        {
            builder.Dispose();
        }

        // Assert
        await Assert.That(result.SequenceEqual(Enumerable.Range(0, 100))).IsTrue();
    }

    [Test]
    public async Task ToArray_NoElements_ReturnsEmptyArray()
    {
        // Arrange
        var builder = new ArrayBuilder<string>(new CountingArrayPool<string>());
        string[] result;

        // Act
        try
        {
            result = builder.ToArray();
        }
        finally
        {
            builder.Dispose();
        }

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Count_AfterAdds_ReturnsNumberOfElements()
    {
        // Arrange
        var builder = new ArrayBuilder<string>(new CountingArrayPool<string>());
        int count;

        // Act
        try
        {
            builder.Add("a");
            builder.Add("b");
            count = builder.Count;
        }
        finally
        {
            builder.Dispose();
        }

        // Assert
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task Add_BeyondCapacity_RentsTwiceTheSize()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool);

        // Act
        for (var i = 0; i < 40; i++)
        {
            builder.Add("x");
        }

        builder.Dispose();

        // Assert
        await Assert.That(string.Join(',', pool.RentedLengths)).IsEqualTo("16,32,64");
    }

    [Test]
    public async Task Dispose_AfterAdds_ResetsCount()
    {
        // Arrange
        var builder = new ArrayBuilder<string>(new CountingArrayPool<string>());
        builder.Add("a");

        // Act
        builder.Dispose();

        // Assert
        await Assert.That(builder.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_AfterGrowing_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool);
        for (var i = 0; i < 40; i++)
        {
            builder.Add("x");
        }

        // Act
        builder.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_AfterGrowing_ClearsElementsBeforeReturning()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool);
        for (var i = 0; i < 40; i++)
        {
            builder.Add("x");
        }

        // Act
        builder.Dispose();

        // Assert
        await Assert.That(pool.ReturnedUncleared).IsFalse();
    }

    [Test]
    public async Task Dispose_CalledTwice_ReturnsArrayOnce()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool);
        builder.Add("a");
        builder.Dispose();

        // Act
        builder.Dispose();

        // Assert
        await Assert.That(pool.Returned).IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_InFinallyAfterException_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        try
        {
            Fill(pool);
        }
        catch (InvalidOperationException)
        {
            // Expected: the builder failed half way.
        }

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Add_WithinTheBuffer_RentsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool, new string[2]);

        // Act
        builder.Add("a");
        builder.Add("b");
        var result = builder.ToArray();
        builder.Dispose();

        // Assert
        await Assert.That((string.Join(',', result), pool.RentedLengths.Count)).IsEqualTo(("a,b", 0));
    }

    [Test]
    public async Task Add_BeyondTheBuffer_ContinuesInARentedArrayAndReturnsIt()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool, new string[2]);

        // Act
        builder.Add("a");
        builder.Add("b");
        builder.Add("c");
        var written = string.Join(',', builder.WrittenSpan.ToArray());
        builder.Dispose();
        builder.Dispose();

        // Assert
        await Assert.That((written, string.Join(',', pool.RentedLengths), pool.Returned, pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo(("a,b,c", "4", 1, 0, false));
    }

    [Test]
    public async Task Add_BeyondARentedArrayGrownFromTheBuffer_ReturnsTheSmallerArray()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var builder = new ArrayBuilder<string>(pool, new string[1]);

        // Act
        builder.Add("a");
        builder.Add("b");
        builder.Add("c");
        var result = builder.ToArray();
        builder.Dispose();

        // Assert
        await Assert.That((string.Join(',', result), string.Join(',', pool.RentedLengths), pool.Returned, pool.Outstanding)).IsEqualTo(("a,b,c", "2,4", 2, 0));
    }

    // The pattern the converter uses: an exception after growing, disposal in finally.
    private static void Fill(CountingArrayPool<string> pool)
    {
        var builder = new ArrayBuilder<string>(pool);
        try
        {
            for (var i = 0; i < 20; i++)
            {
                builder.Add("x");
            }

            throw new InvalidOperationException("Malformed input.");
        }
        finally
        {
            builder.Dispose();
        }
    }
}
