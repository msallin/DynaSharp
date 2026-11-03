using System.Security.Cryptography;

namespace DynaSharp;

/// <summary>
/// The keys that sign and check the <see cref="ContinuationToken"/> of the paged reads of a client, so that a client of a web API can no longer pick its position: a token it made up
/// or edited, or one of another query, fails before a request goes out. Set it as <see cref="DynamoDbClientOptions.TokenSigner"/>. Every instance of a service that hands out tokens
/// and takes them back holds the same keys, as it would for cookies.
/// </summary>
/// <remarks>
/// <para>
/// A token is signed with HMAC-SHA256 under the first key. It is checked against every key, in order, with a comparison that takes the same time whatever the bytes are, so
/// a key is rotated in three steps: add the new key behind the old one on every instance, move it to the front once all of them have it, and drop the old key once the tokens
/// that it signed may have expired from the clients. A token under a key that is gone fails as a forged one does.
/// </para>
/// <para>
/// Signing is not encryption: a signed token still shows the key values of the last item to whoever holds it, see <see cref="ContinuationToken"/>. What the signature covers, and so
/// what a token cannot be moved to, is the table, the index and the key condition of the read, with the partitions of a fan-out. The filter, the projection, the limit and the
/// direction of a plain read are not part of it: a token goes on with another filter or page size of the same query, as it does unsigned.
/// </para>
/// <para>
/// Each key is copied, so the arrays can be cleared after this is made. The object holds the keys and nothing else, is safe to share between clients and threads, and prints its type
/// name only.
/// </para>
/// </remarks>
public sealed class ContinuationTokenSigner
{
    /// <summary>The shortest key, in bytes: 32, the output of SHA-256, which is what HMAC-SHA256 needs to be as strong as it can be.</summary>
    public const int MinKeyLength = 32;

    /// <summary>The length of a signature in bytes.</summary>
    internal const int SignatureLength = HMACSHA256.HashSizeInBytes;

    private readonly byte[][] _keys;

    /// <summary>Initializes a new instance of the <see cref="ContinuationTokenSigner"/> class.</summary>
    /// <param name="keys">
    /// The keys, the one that signs first, then those that are still accepted. Each is random bytes of at least <see cref="MinKeyLength"/>, for example from
    /// <c>RandomNumberGenerator.GetBytes(32)</c>, kept as a secret of the service and read with <c>Convert.FromBase64String</c>.
    /// </param>
    /// <exception cref="ArgumentException">There is no key, or a key is shorter than <see cref="MinKeyLength"/>.</exception>
    /// <exception cref="ArgumentNullException">A key is <see langword="null"/>.</exception>
    public ContinuationTokenSigner(params ReadOnlySpan<byte[]> keys)
    {
        if (keys.IsEmpty)
        {
            throw new ArgumentException("A signer needs a key to sign with.", nameof(keys));
        }

        _keys = new byte[keys.Length][];
        for (var i = 0; i < keys.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(keys[i], nameof(keys));
            if (keys[i].Length < MinKeyLength)
            {
                throw new ArgumentException($"Key {i} has {keys[i].Length} bytes, and a key has at least {MinKeyLength}: random bytes, not a password.", nameof(keys));
            }

            _keys[i] = [.. keys[i]];
        }
    }

    /// <summary>Signs a message with the first key.</summary>
    /// <param name="message">The message.</param>
    /// <param name="signature">Where the signature goes, <see cref="SignatureLength"/> bytes.</param>
    internal void Sign(ReadOnlySpan<byte> message, Span<byte> signature) => HMACSHA256.HashData(_keys[0], message, signature);

    /// <summary>Checks a signature against every key.</summary>
    /// <param name="message">The message.</param>
    /// <param name="signature">The signature that came with it.</param>
    /// <returns><see langword="true"/> if a key signed the message so.</returns>
    internal bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        Span<byte> expected = stackalloc byte[SignatureLength];
        foreach (var key in _keys)
        {
            HMACSHA256.HashData(key, message, expected);
            if (CryptographicOperations.FixedTimeEquals(expected, signature))
            {
                return true;
            }
        }

        return false;
    }
}
