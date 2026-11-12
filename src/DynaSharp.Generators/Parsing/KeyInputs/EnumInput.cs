namespace DynaSharp.Generators;

/// <summary>An enum: the member name by default, and <c>d&lt;w&gt;</c> for its value.</summary>
/// <param name="typeName">The type as messages show it.</param>
/// <param name="valueType">The enum as code.</param>
/// <param name="underlying">The underlying type.</param>
/// <param name="namesAreUnique">False for <c>[Flags]</c> and aliased members, whose names are not one-to-one.</param>
/// <param name="hasNoNames">True for an enum without members, which no name can write.</param>
/// <param name="digitsWidth">The width of a <c>d&lt;w&gt;</c> that holds every value, or null when a member is negative.</param>
/// <param name="converter">Registers the generated converter and gives its name. It is not needed for <c>d&lt;w&gt;</c>.</param>
internal sealed class EnumInput(string typeName, string valueType, IntegerType underlying, bool namesAreUnique, bool hasNoNames, int? digitsWidth, Func<string> converter)
    : KeyInputType(typeName, "no format for the member name, or d<w>, such as d3, for its value")
{
    private const int MaxWidth = 20;

    /// <inheritdoc/>
    public override IReadOnlyList<string?> Fits { get; } = digitsWidth is { } width ? [null, "d" + width] : [null];

    public override FormatBinding Bind(string? format, string property)
    {
        if (format is null && hasNoNames)
        {
            // An enum without members has no name to write, whatever the property, also one that only the key holds ([KeyOnly], or a getter); its value fits d1.
            return new FormatBinding(null, new KeyProblem(Descriptors.EnumWithoutMembersInKey, [TypeName], Fix: KeyProblem.Rewrite(property, [(property, "d" + digitsWidth)])));
        }

        if (format is null)
        {
            return namesAreUnique
                ? Encoded(new EnumNameEncoding(converter(), valueType))
                : new FormatBinding(null, new KeyProblem(Descriptors.EnumNamesNotUnique, [TypeName], Fix: digitsWidth is { } digits ? KeyProblem.Rewrite(property, [(property, "d" + digits)]) : null));
        }

        var width = ReadNumber(format.Substring(1));
        if (format[0] != 'd' || width < 0)
        {
            return DoesNotFit(format);
        }

        return width is 0 or > MaxWidth ? OutOfRange(format) : Encoded(new DigitsEncoding(width, underlying, valueType));
    }
}
