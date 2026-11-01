using DynaSharp.Http;

namespace DynaSharp.UnitTests.Http;

public sealed class BoundedPoolTests
{
    [Test]
    public async Task Rent_EmptyPool_ReturnsNull()
    {
        // Arrange
        var pool = new BoundedPool<object>(2);

        // Act
        var item = pool.Rent();

        // Assert
        await Assert.That(item).IsNull();
    }

    [Test]
    public async Task Rent_AfterReturn_ReturnsTheObject()
    {
        // Arrange
        var pool = new BoundedPool<object>(2);
        var returned = new object();
        pool.Return(returned);

        // Act
        var item = pool.Rent();

        // Assert
        await Assert.That(item).IsSameReferenceAs(returned);
    }

    [Test]
    public async Task Rent_ObjectIsRentedOnce_DoesNotHandItOutAgain()
    {
        // Arrange
        var pool = new BoundedPool<object>(2);
        pool.Return(new object());
        pool.Rent();

        // Act
        var item = pool.Rent();

        // Assert
        await Assert.That(item).IsNull();
    }

    [Test]
    public async Task Return_PoolHasRoom_ReturnsTrue()
    {
        // Arrange
        var pool = new BoundedPool<object>(1);

        // Act
        var accepted = pool.Return(new object());

        // Assert
        await Assert.That(accepted).IsTrue();
    }

    [Test]
    public async Task Return_PoolIsFull_ReturnsFalse()
    {
        // Arrange
        var pool = new BoundedPool<object>(1);
        pool.Return(new object());

        // Act
        var accepted = pool.Return(new object());

        // Assert
        await Assert.That(accepted).IsFalse();
    }

    [Test]
    public async Task Return_PoolIsFull_KeepsWhatItHolds()
    {
        // Arrange
        var pool = new BoundedPool<object>(1);
        var held = new object();
        pool.Return(held);
        pool.Return(new object());

        // Act
        var item = pool.Rent();

        // Assert
        await Assert.That(item).IsSameReferenceAs(held);
    }

    [Test]
    public async Task Return_AfterRentFreedARoom_ReturnsTrue()
    {
        // Arrange
        var pool = new BoundedPool<object>(1);
        pool.Return(new object());
        pool.Rent();

        // Act
        var accepted = pool.Return(new object());

        // Assert
        await Assert.That(accepted).IsTrue();
    }

    [Test]
    public async Task Close_PoolHoldsObjects_HandsThemOut()
    {
        // Arrange
        var pool = new BoundedPool<object>(3);
        var first = new object();
        var second = new object();
        pool.Return(first);
        pool.Return(second);

        // Act
        var held = pool.Close();

        // Assert
        await Assert.That(held).IsEquivalentTo([first, second]);
    }

    [Test]
    public async Task Close_EmptyPool_HandsOutNothing()
    {
        // Arrange
        var pool = new BoundedPool<object>(3);

        // Act
        var held = pool.Close();

        // Assert
        await Assert.That(held).IsEmpty();
    }

    [Test]
    public async Task Close_PoolHoldsObjects_RentFindsNothingAfterwards()
    {
        // Arrange
        var pool = new BoundedPool<object>(3);
        pool.Return(new object());
        pool.Close();

        // Act
        var item = pool.Rent();

        // Assert
        await Assert.That(item).IsNull();
    }

    [Test]
    public async Task Return_PoolIsClosed_ReturnsFalse()
    {
        // Arrange
        var pool = new BoundedPool<object>(3);
        pool.Close();

        // Act
        var accepted = pool.Return(new object());

        // Assert
        await Assert.That(accepted).IsFalse();
    }

    [Test]
    public async Task RentAndReturn_ManyThreads_NeverHandOutAnObjectTwice()
    {
        // Arrange
        var pool = new BoundedPool<Guard>(4);
        for (var i = 0; i < 4; i++)
        {
            pool.Return(new Guard());
        }

        var doubleOwnership = 0;

        // Act
        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                var item = pool.Rent() ?? new Guard();
                if (!item.TryEnter())
                {
                    Interlocked.Increment(ref doubleOwnership);
                }

                item.Exit();
                pool.Return(item);
            }
        });

        // Assert
        await Assert.That(doubleOwnership).IsEqualTo(0);
    }

    // Entered while a thread has the object; a second thread that gets it meanwhile cannot enter and counts it.
    private sealed class Guard
    {
        private int _owners;

        public bool TryEnter() => Interlocked.CompareExchange(ref _owners, 1, 0) == 0;

        public void Exit() => Interlocked.Exchange(ref _owners, 0);
    }
}
