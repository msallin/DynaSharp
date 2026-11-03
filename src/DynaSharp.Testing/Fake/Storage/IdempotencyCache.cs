using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The <c>ClientRequestToken</c>s of the <c>TransactWriteItems</c> that went through, each with its request and when it came, on the clock of the fake. Within 10 minutes the same
/// token with the same request is a replay, which changes nothing; with another request it is <c>IdempotentParameterMismatchException</c>. After 10 minutes the token is new again.
/// </summary>
/// <remarks>Requests are the same when their JSON is: the same members with the same values, in any order. Any member counts, as on DynamoDB, which compares every parameter.</remarks>
internal sealed class IdempotencyCache
{
    /// <summary>How long DynamoDB remembers a token.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, (JsonElement Request, DateTimeOffset At)> _tokens = new(StringComparer.Ordinal);

    /// <summary>Tells whether a request repeats one that went through with its token.</summary>
    /// <param name="token">The token.</param>
    /// <param name="request">The request.</param>
    /// <param name="now">The time on the clock of the fake.</param>
    /// <returns><see langword="true"/> for a replay, <see langword="false"/> for a token that is new or older than the window.</returns>
    /// <exception cref="FakeError">An <c>IdempotentParameterMismatchException</c>: the token came within the window with another request.</exception>
    public bool IsReplay(string token, JsonElement request, DateTimeOffset now)
    {
        if (!_tokens.TryGetValue(token, out var first))
        {
            return false;
        }

        if (now - first.At >= Window)
        {
            _tokens.Remove(token);
            return false;
        }

        return JsonElement.DeepEquals(first.Request, request) ? true : throw FakeError.IdempotentParameterMismatch();
    }

    /// <summary>Remembers the token of a request that went through.</summary>
    /// <param name="token">The token.</param>
    /// <param name="request">The request, which is copied.</param>
    /// <param name="now">The time on the clock of the fake.</param>
    public void Remember(string token, JsonElement request, DateTimeOffset now) => _tokens[token] = (request.Clone(), now);

    /// <summary>Forgets every token.</summary>
    public void Clear() => _tokens.Clear();
}
