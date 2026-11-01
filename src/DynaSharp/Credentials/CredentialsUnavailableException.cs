namespace DynaSharp;

/// <summary>
/// Thrown when a credentials provider cannot find or obtain credentials.
/// </summary>
public sealed class CredentialsUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialsUnavailableException"/> class.
    /// </summary>
    /// <param name="message">Which source was tried and what is missing.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public CredentialsUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
