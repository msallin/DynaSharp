using System.Runtime.CompilerServices;

namespace DynaSharp.Serialization;

/// <summary>
/// Room for the attributes of an item or map on the stack. Few items have more than this, and a rented array per map would mean renting
/// while the enclosing item holds one, which misses the thread's cache of the pool every time.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[InlineArray(Length)]
internal struct InlineBuffer<T>
{
    private const int Length = 16;

#pragma warning disable IDE0051, CS0169 // The compiler lays out the elements from this field; nothing names it.
    private T _element;
#pragma warning restore IDE0051, CS0169
}
