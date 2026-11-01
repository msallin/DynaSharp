using System.Globalization;

namespace DynaSharp.Generators;

/// <summary>The row of the format table for one kind of key input. When adding a type, add a class here and a case to <see cref="KeyInputCatalog"/>.</summary>
/// <param name="typeName">The type as messages show it.</param>
/// <param name="formats">What to write instead of a format that does not fit, for the message.</param>
internal abstract class KeyInputType(string typeName, string formats = "")
{
    protected string TypeName { get; } = typeName;

    /// <summary>Resolves a format, null for the default of the type.</summary>
    /// <param name="format">The format.</param>
    /// <param name="property">The property, for messages.</param>
    /// <returns>The encoding or the problem.</returns>
    public abstract FormatBinding Bind(string? format, string property);

    // "12" gives 12. An empty text or one with anything but digits gives -1. More digits than an int holds give int.MaxValue, which no
    // format accepts.
    protected static int ReadNumber(string text)
    {
        if (text.Length == 0 || text.Any(static character => character is < '0' or > '9'))
        {
            return -1;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
    }

    protected static FormatBinding Encoded(KeyEncoding encoding) => new(encoding, null);

    protected static FormatBinding OutOfRange(string format) => new(null, new KeyProblem(Descriptors.FormatOutOfRange, [format]));

    protected FormatBinding DoesNotFit(string format) => new(null, new KeyProblem(Descriptors.FormatDoesNotFit, [format, TypeName, formats]));
}
