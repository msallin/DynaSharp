using System.Runtime.CompilerServices;

namespace DynaSharp.Fluent;

/// <summary>Checks arguments of a type parameter, which <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/> would box when the type is a struct.</summary>
internal static class Argument
{
    /// <summary>Throws if a value is <see langword="null"/>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="parameterName">The parameter it came through, which the compiler fills in unless the value is only part of that parameter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void ThrowIfNull<T>(T value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }
}
