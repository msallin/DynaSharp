namespace DynaSharp.Operations;

/// <summary>
/// The <c>ClientRequestToken</c> of a <c>TransactWriteItems</c> that the caller gave none: a new GUID per call. Both APIs write it where they serialize the request,
/// once per call, so every attempt of the call, retries included, sends the same token, which is what makes a retried transaction idempotent.
/// </summary>
internal static class ClientRequestToken
{
    /// <summary>The length of a token in UTF-8 bytes: a GUID in its <c>D</c> format, which fits the 36 characters that DynamoDB allows.</summary>
    public const int Length = 36;

    /// <summary>Writes a new token.</summary>
    /// <param name="destination">At least <see cref="Length"/> bytes.</param>
    public static void Create(Span<byte> destination) => _ = Guid.NewGuid().TryFormat(destination, out _);
}
