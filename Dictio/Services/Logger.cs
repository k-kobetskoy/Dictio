using System.IO;

namespace Dictio.Services;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dictio", "debug.log");

    private const long MaxBytes = 2 * 1024 * 1024; // 2 MB

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            RotateIfNeeded();
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;
        var info = new FileInfo(LogPath);
        if (info.Length > MaxBytes)
        {
            var backup = LogPath + ".old";
            File.Copy(LogPath, backup, overwrite: true);
            File.Delete(LogPath);
        }
    }
}
