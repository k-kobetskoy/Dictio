using System.IO;

namespace Dictio.Services;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dictio", "debug.log");

    private static readonly string LocalLogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "debug.log");

    private const long MaxBytes = 2 * 1024 * 1024; // 2 MB

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
        WriteToPath(LogPath, line);
        WriteToPath(LocalLogPath, line);
    }

    private static void WriteToPath(string path, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RotateIfNeeded(path);
            File.AppendAllText(path, line);
        }
        catch { }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        if (info.Length > MaxBytes)
        {
            File.Copy(path, path + ".old", overwrite: true);
            File.Delete(path);
        }
    }
}
