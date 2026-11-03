namespace DynaSharp.Configuration;

/// <summary>The default <see cref="WriteTextFile"/>, which writes to disk.</summary>
internal static class TextFiles
{
    /// <summary>Gets the writer to disk. One shared delegate, so that no call site converts a method group.</summary>
    public static WriteTextFile DiskWriter { get; } = ReplaceOnDisk;

    // As botocore's JSONFileCache: the new content goes into a file next to the old one, which then takes its place in one step, so that a reader
    // never sees half a file. The new file starts as a copy of the old one, so that it keeps its permissions (0600, as the AWS CLI writes it)
    // on every platform without code for each.
    private static void ReplaceOnDisk(string path, string content)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(path, temporary);
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
