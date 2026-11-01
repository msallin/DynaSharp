namespace DynaSharp.UnitTests.Generators;

/// <summary>An empty directory under the temp folder that is removed on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dynasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string name) => System.IO.Path.Combine(Path, name);

    public void Write(string name, string text) => File.WriteAllText(Combine(name), text);

    public string Read(string name) => File.ReadAllText(Combine(name));

    public bool Exists(string name) => File.Exists(Combine(name));

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
