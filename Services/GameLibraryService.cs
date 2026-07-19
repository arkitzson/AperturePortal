using System.IO;
using System.Text.Json;
using ApertureOS.Models;

namespace ApertureOS.Services;

public class GameLibraryService
{
    private readonly string _dataDir;
    private readonly string _coversDir;
    private readonly string _headersDir;
    private readonly string _libraryFile;

    public GameLibraryService()
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApertureOS");
        _coversDir = Path.Combine(_dataDir, "Covers");
        _headersDir = Path.Combine(_dataDir, "Headers");
        _libraryFile = Path.Combine(_dataDir, "games.json");

        Directory.CreateDirectory(_coversDir);
        Directory.CreateDirectory(_headersDir);
    }

    public List<Game> LoadGames()
    {
        if (!File.Exists(_libraryFile))
            return new List<Game>();

        var json = File.ReadAllText(_libraryFile);
        var games = JsonSerializer.Deserialize<List<Game>>(json) ?? new List<Game>();

        // Back-compat: games.json written before the Platform field existed has none, so every
        // game on it deserializes with the default GamePlatform.Manual - backfill Steam-synced
        // entries (identifiable by their steam://rungameid/ ExePath regardless of this field) so
        // their platform badge is correct without forcing a re-sync. In-memory only; nothing here
        // needs writing back since this same fixup is cheap to redo on every load.
        foreach (var game in games)
        {
            if (game.Platform == GamePlatform.Manual && SteamLibraryService.TryGetSteamAppId(game.ExePath) is not null)
            {
                game.Platform = GamePlatform.Steam;
            }
        }

        return games;
    }

    public void SaveGames(IEnumerable<Game> games)
    {
        var json = JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_libraryFile, json);
    }

    /// <summary>
    /// Copies the chosen cover image into app storage so it survives even if the
    /// original file is later moved or deleted, and returns the new path.
    /// </summary>
    public string StoreCoverImage(string sourceImagePath, string gameName) =>
        StoreImage(_coversDir, sourceImagePath, gameName);

    /// <summary>Same as <see cref="StoreCoverImage"/> but for the wide header/banner image.</summary>
    public string StoreHeaderImage(string sourceImagePath, string gameName) =>
        StoreImage(_headersDir, sourceImagePath, gameName);

    private static string StoreImage(string destDir, string sourceImagePath, string gameName)
    {
        var extension = Path.GetExtension(sourceImagePath);
        var safeName = string.Join("_", gameName.Split(Path.GetInvalidFileNameChars()));
        var destFileName = $"{safeName}_{Guid.NewGuid():N}{extension}";
        var destPath = Path.Combine(destDir, destFileName);

        File.Copy(sourceImagePath, destPath, overwrite: true);
        return destPath;
    }
}
