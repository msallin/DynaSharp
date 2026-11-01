namespace DynaSharp.Configuration;

/// <summary>
/// The environment of the current process, as the function that the configuration code takes. One shared delegate instead of a
/// method group at every call site.
/// </summary>
internal static class ProcessEnvironment
{
    /// <summary>Gets a function that reads a variable of the current process, or returns <see langword="null"/> if it is not set.</summary>
    public static Func<string, string?> GetVariable { get; } = Environment.GetEnvironmentVariable;
}
