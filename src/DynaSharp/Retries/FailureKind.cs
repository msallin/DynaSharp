namespace DynaSharp.Retries;

/// <summary>
/// What a failed attempt says about repeating it, which also selects the backoff base and the cost against the retry quota.
/// </summary>
internal enum FailureKind
{
    /// <summary>Repeating the request cannot help: validation, conditional check failure, access denied, any other 4xx, cancellation.</summary>
    Permanent,

    /// <summary>The service or the connection failed in a way that may not repeat: 5xx, network failure.</summary>
    Transient,

    /// <summary>The service asked the client to slow down. Backs off from a larger base.</summary>
    Throttling,

    /// <summary>An attempt hit its timeout. Transient, but costs twice the quota of other retries.</summary>
    Timeout,
}
