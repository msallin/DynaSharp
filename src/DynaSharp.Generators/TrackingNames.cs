namespace DynaSharp.Generators;

/// <summary>
/// Names of the pipeline steps that the incrementality tests inspect. Every const string in this class is walked by the
/// tests, so a step added here is checked for symbol leaks and plain arrays without touching them.
/// </summary>
internal static class TrackingNames
{
    public const string Entities = nameof(Entities);

    public const string Documents = nameof(Documents);

    public const string Models = nameof(Models);

    public const string Diagnostics = nameof(Diagnostics);
}
