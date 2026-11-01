namespace DynaSharp.Retries;

/// <summary>What to do after a failed attempt.</summary>
internal enum RetryVerdict
{
    /// <summary>Give up: the failure is permanent, or retries are switched off.</summary>
    Surface,

    /// <summary>Give up: the failure may be transient, but all attempts are used.</summary>
    AttemptsExhausted,

    /// <summary>Give up: the failure may be transient, but the retry quota is empty.</summary>
    QuotaExhausted,

    /// <summary>Send again at once: the signing clock was corrected, or expired credentials were dropped. Costs no quota and waits for nothing.</summary>
    Resend,

    /// <summary>Wait, then send again.</summary>
    Retry,
}
