using System.IO;
using System.Text.Json;

namespace ApertureOS.Services;

public sealed record EpicGameInfo(string AppName, string Name, string ExePath);

/// <summary>
/// Finds games installed via the Epic Games Launcher by reading its local install manifests -
/// no login or API key needed, same local-only approach as SteamLibraryService.GetInstalledGames.
/// Unlike Steam, synced entries store a direct path to the game's own exe rather than a launcher
/// URI, so they behave exactly like a manually-added game afterward (always considered installed,
/// launched directly) - this app has no Epic-manifest install/download-progress tracking the way
/// it does for Steam. That also means an EAC/BattlEye-protected title launched this way may not
/// initialize anti-cheat the way it would via the real Epic client; most games are unaffected, but
/// if one doesn't launch cleanly this way, re-add it manually pointed at the Epic Games Launcher
/// itself as a workaround.
/// </summary>
public sealed class EpicGamesLibraryService
{
    public List<EpicGameInfo> GetInstalledGames()
    {
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsDir))
            return [];

        var games = new List<EpicGameInfo>();
        foreach (var manifestFile in Directory.GetFiles(manifestsDir, "*.item"))
        {
            if (ParseManifest(manifestFile) is { } game)
            {
                games.Add(game);
            }
        }

        return games;
    }

    private static EpicGameInfo? ParseManifest(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;

            // Epic writes one of these .item files for everything it's ever installed, including
            // non-game redistributables (engine prereqs, plugins) and installs that were started
            // but never finished - skip both rather than cluttering the library with them.
            if (root.TryGetProperty("bIsApplication", out var isApp) && !isApp.GetBoolean())
                return null;
            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.GetBoolean())
                return null;

            var appName = GetString(root, "AppName");
            var displayName = GetString(root, "DisplayName");
            var installLocation = GetString(root, "InstallLocation");
            var launchExecutable = GetString(root, "LaunchExecutable");

            if (appName is null || displayName is null || installLocation is null || launchExecutable is null)
                return null;

            var exePath = Path.Combine(installLocation, launchExecutable);
            return File.Exists(exePath) ? new EpicGameInfo(appName, displayName, exePath) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A malformed or half-written manifest (Epic was mid-update when this ran) shouldn't
            // take down the whole scan - just skip that one entry.
            return null;
        }
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
