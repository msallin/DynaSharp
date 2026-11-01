using System.Buffers;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// An array pool for a single test: hands out new arrays and tracks them, so a test can verify that every rented array is returned
/// exactly once, independent of the shared pool and of other tests.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <param name="surplus">How much longer than requested the arrays are. A real pool hands out arrays that are larger than requested.</param>
internal sealed class CountingArrayPool<T>(int surplus = 0) : ArrayPool<T>
{
    private readonly Lock _gate = new();
    private readonly HashSet<T[]> _outstanding = [with(ReferenceEqualityComparer.Instance)];
    private readonly List<int> _rentedLengths = [];
    private int _returned;
    private bool _returnedUncleared;

    /// <summary>Gets the requested length of every rental, in order.</summary>
    public IReadOnlyList<int> RentedLengths
    {
        get
        {
            lock (_gate)
            {
                return [.. _rentedLengths];
            }
        }
    }

    /// <summary>Gets the number of rented arrays not returned yet.</summary>
    public int Outstanding
    {
        get
        {
            lock (_gate)
            {
                return _outstanding.Count;
            }
        }
    }

    /// <summary>Gets the number of returned arrays.</summary>
    public int Returned
    {
        get
        {
            lock (_gate)
            {
                return _returned;
            }
        }
    }

    /// <summary>Gets a value indicating whether an array came back with elements other than the default value.</summary>
    public bool ReturnedUncleared
    {
        get
        {
            lock (_gate)
            {
                return _returnedUncleared;
            }
        }
    }

    public override T[] Rent(int minimumLength)
    {
        var array = new T[minimumLength + surplus];
        lock (_gate)
        {
            _outstanding.Add(array);
            _rentedLengths.Add(minimumLength);
        }

        return array;
    }

    public override void Return(T[] array, bool clearArray = false)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(array))
            {
                throw new InvalidOperationException("The array was returned twice or was not rented from this pool.");
            }

            _returned++;
            _returnedUncleared |= !clearArray && Array.Exists(array, static element => !EqualityComparer<T>.Default.Equals(element, default));
        }
    }
}
