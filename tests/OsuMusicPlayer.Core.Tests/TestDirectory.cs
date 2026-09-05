namespace OsuMusicPlayer.Core.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OsuMusicPlayer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(params string[] components)
    {
        var path = System.IO.Path.Combine([Path, .. components]);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, string? content = null)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? Path);
        if (content is null)
        {
            File.WriteAllBytes(path, [1]);
        }
        else
        {
            File.WriteAllText(path, content);
        }

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
