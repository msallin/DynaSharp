namespace DynaSharp;

/// <summary>
/// A value cannot be part of a key: it is empty, contains a delimiter, a control character or U+10FFFF, is not well-formed text, or does not
/// fit its numeric or timestamp format. It is an <see cref="ArgumentException"/>, so a key built from user input fails as a bad argument.
/// </summary>
/// <remarks>
/// The message and <see cref="ArgumentException.ParamName"/> name the placeholder and never the value, because keys are often
/// built from user input and exceptions end up in logs.
/// </remarks>
public sealed class KeyFormatException : ArgumentException
{
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
    }
}
