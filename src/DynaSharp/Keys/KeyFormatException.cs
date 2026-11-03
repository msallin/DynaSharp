using System.ComponentModel;

namespace DynaSharp;

/// <summary>
/// A value cannot be part of a key: it is empty, contains a delimiter that its format does not take, a control character or U+10FFFF, is not well-formed text, or does not
/// fit its numeric or timestamp format. It is an <see cref="ArgumentException"/>, so a key built from user input fails as a bad argument.
/// </summary>
/// <remarks>
/// The message and <see cref="ArgumentException.ParamName"/> name the placeholder and never the value, because keys are often
/// built from user input and exceptions end up in logs. From a generated key builder the message names the template as well, as it is written in the code, and for a delimiter
/// the delimiters of the template, but not which of them the value holds: <c>The value contains a delimiter of the key template 'USER#{Id}' ('#')</c>.
/// </remarks>
public sealed class KeyFormatException : ArgumentException
{
    // What is wrong with the value, without the placeholder that ArgumentException appends, so that InTemplate can say it again.
    private readonly string? _reason;

    /// <summary>Initializes a new instance of the <see cref="KeyFormatException"/> class for text that does not match a key template.</summary>
    /// <param name="message">What is wrong. Must not contain the key or a value of it.</param>
    public KeyFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyFormatException"/> class for a value of a placeholder.</summary>
    /// <param name="placeholder">The name of the placeholder, also reported as <see cref="ArgumentException.ParamName"/>.</param>
    /// <param name="reason">What is wrong with the value. Must not contain the value.</param>
    public KeyFormatException(string placeholder, string reason)
        : base(reason, placeholder)
    {
        _reason = reason;
    }

    /// <summary>Gets a value indicating whether the value holds a delimiter of its template, which is what the delimiters of the template explain.</summary>
    internal bool HoldsDelimiter { get; init; }

    /// <summary>
    /// Makes the same exception with the template in the message. Generated key builders call it when a value fails, from a <c>catch</c> that costs a key that is valid nothing, because
    /// <c>KeyWriter</c> knows the delimiters of a template and not its text.
    /// </summary>
    /// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
    /// <param name="template">The template as it is written in the code, such as <c>USER#{Id}</c>.</param>
    /// <param name="delimiters">The characters next to a placeholder of the template, each once, such as <c>#</c>.</param>
    /// <returns>The exception for a value of a placeholder, or this one for one that is not about a value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public KeyFormatException InTemplate(string template, string delimiters)
    {
        if (_reason is null)
        {
            return this;
        }

        var reason = HoldsDelimiter
            ? $"The value contains a delimiter of the key template '{template}' ('{delimiters}')."
            : $"{_reason} The key template is '{template}'.";
        return new KeyFormatException(ParamName!, reason) { HoldsDelimiter = HoldsDelimiter };
    }
}
