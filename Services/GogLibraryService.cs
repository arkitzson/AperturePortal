using System.IO;
using Microsoft.Win32;

namespace ApertureOS.Services;

public sealed record GogGameInfo(string GameId, string Name, string ExePath);

/// <summary>
/// Finds games installed via GOG Galaxy by reading the registry entries it writes per game -
/// no login or API key needed, same local-only approach as SteamLibraryService.GetInstalledGames.
/// GOG games are DRM-free, so - unlike Steam - there's no launcher URI or install manifest worth
/// tracking afterward: synced entries store a direct path to the game's own exe and behave exactly
/// like a manually-added game (always considered installed, launched directly, no GOG Galaxy
/// process required to play).
/// </summary>
public sealed class GogLibraryService
{
    public List<GogGameInfo> GetInstalledGames()
    {
        var games = new List<GogGameInfo>();

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var gogGamesKey = hklm.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (gogGamesKey is null)
            return games;

        foreach (var gameId in gogGamesKey.GetSubKeyNames())
        {
            using var gameKey = gogGamesKey.OpenSubKey(gameId);
            if (gameKey is null)
                continue;

            var name = gameKey.GetValue("gameName") as string;
            var installPath = gameKey.GetValue("path") as string;
            var exeRelative = gameKey.GetValue("exe") as string;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(exeRelative))
                continue;

            var exePath = Path.IsPathRooted(exeRelative) ? exeRelative : Path.Combine(installPath, exeRelative);
            if (!File.Exists(exePath))
                continue;

            games.Add(new GogGameInfo(gameId, name, exePath));
        }

        return games;
    }
}
