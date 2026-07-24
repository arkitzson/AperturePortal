using System.IO;
using System.Text.Json;
using ApertureOS.Models;

namespace ApertureOS.Services;

public class AppSettings
{
    public string SteamGridDbApiKey { get; set; } = string.Empty;

    /// <summary>SteamID64 read from the embedded-browser login's session cookie, or empty if never logged in.</summary>
    public string SteamId64 { get; set; } = string.Empty;

    /// <summary>Fallback for full-library sync when not logged in via the embedded browser: a free key from steamcommunity.com/dev/apikey.</summary>
    public string SteamWebApiKey { get; set; } = string.Empty;

    /// <summary>"steamLoginSecure" cookie captured from an embedded-browser Steam login, or empty if never logged in that way.</summary>
    public string SteamLoginSecureCookie { get; set; } = string.Empty;

    /// <summary>True = skip the normal window on launch and go straight into Console Mode.</summary>
    public bool LaunchInConsoleMode { get; set; }

    /// <summary>Name of the LibraryFilter tab last selected (desktop tabs or Console Mode's LB/RB), restored on the next launch.</summary>
    public string LastLibraryFilter { get; set; } = "All";

    /// <summary>Id (as a string) of the category chip last selected, empty for "All Categories" - restored the same way as LastLibraryFilter.</summary>
    public string LastCategoryFilterId { get; set; } = string.Empty;

    /// <summary>Configured emulators, each with its own exe, ROM folder, and console - used by the "Scan for Games" auto-add flow in Settings.</summary>
    public List<EmulatorConfig> Emulators { get; set; } = new();

    /// <summary>Folders the user has pointed the "Installed Games" scan at, to auto-detect PC games that aren't on Steam/Epic/GOG/Battle.net.</summary>
    public List<string> InstalledGameFolders { get; set; } = new();
}

public class SettingsService
{
    private readonly string _settingsFile;

    public SettingsService()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApertureOS");
        Directory.CreateDirectory(dataDir);
        _settingsFile = Path.Combine(dataDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFile))
            return new AppSettings();

        var json = File.ReadAllText(_settingsFile);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFile, json);
    }
}
