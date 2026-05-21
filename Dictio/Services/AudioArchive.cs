using System.IO;

namespace Dictio.Services;

public static class AudioArchive
{
    private const int MaxFiles = 10;

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dictio", "audio");

    public static string Save(MemoryStream stream)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            stream.Position = 0;
            File.WriteAllBytes(path, stream.ToArray());
            Logger.Log($"Audio saved: {path} ({stream.Length / 1024} KB)");
            Trim();
            return path;
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioArchive.Save error: {ex.Message}");
            return "";
        }
    }

    public static IReadOnlyList<FileInfo> GetAll()
    {
        if (!Directory.Exists(Dir)) return [];
        return new DirectoryInfo(Dir)
            .GetFiles("recording_*.wav")
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    private static void Trim()
    {
        var files = new DirectoryInfo(Dir)
            .GetFiles("recording_*.wav")
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        foreach (var old in files.Skip(MaxFiles))
        {
            try { old.Delete(); }
            catch { }
        }
    }
}
