using System.IO;

namespace ApertureOS.Services;

/// <summary>Best-effort append-only trace log at %APPDATA%\ApertureOS\error.log.</summary>
public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApertureOS", "error.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
            // Logging is best-effort only.
        }
    }
}
