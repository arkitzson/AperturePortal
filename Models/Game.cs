using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ApertureOS.Models;

/// <summary>
/// Where a game came from. Manual (the default, including every game written before this field
/// existed - see GameLibraryService.LoadGames for the back-compat fixup) covers plain local exes
/// with no platform tracking, same as today. Steam/Epic/Gog are auto-synced from that platform's
/// local install records; BattleNet/Xbox have no reliable local auto-detection (see AddGameWindow),
/// so they only ever get set when the user picks them by hand.
/// </summary>
public enum GamePlatform
{
    Manual,
    Steam,
    Epic,
    Gog,
    BattleNet,
    Xbox
}

public class Game : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string CoverImagePath { get; set; } = string.Empty;
    public string HeaderImagePath { get; set; } = string.Empty;

    // Without this, System.Text.Json serializes the enum as a raw integer by default - fine until
    // GamePlatform's members ever get reordered or one's inserted in the middle, which would then
    // silently reinterpret every existing game.json's saved Platform as the wrong platform. Storing
    // the name instead makes the file (a) immune to that and (b) readable/debuggable by a human.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GamePlatform Platform { get; set; } = GamePlatform.Manual;

    /// <summary>When this game was last launched from ApertureOS, or null if never - persisted, unlike the runtime-only install-state fields below.</summary>
    public DateTime? LastPlayedAt { get; set; }

    /// <summary>The specific console this copy of the game originally released on (e.g. "PlayStation 5"), distinct from Platform, which is the launcher/sync source, not the originating console. Typed in manually, or carried over from an Emulator config's Console field for scanned ROMs - there's no external metadata API driving this (SteamGridDB is art-only, no platform data). Empty until set.</summary>
    public string Console { get; set; } = string.Empty;

    /// <summary>Custom category membership (many-to-many) - Guids into categories.json. Auto-derived category membership (console or platform) is NOT tracked here; see GameCategory.Kind and CategoryService.</summary>
    public List<Guid> CategoryIds { get; set; } = new();

    /// <summary>True if this game is launched through an emulator rather than run directly.</summary>
    public bool IsEmulated { get; set; }

    /// <summary>The emulator's own executable - only meaningful when IsEmulated is true. ExePath still holds the game file itself (ROM/ISO/etc.) in that case, same "the thing this launch is about" convention it already uses for Steam URIs; it's passed to EmulatorPath as a launch argument instead of being executed directly.</summary>
    public string EmulatorPath { get; set; } = string.Empty;

    private bool _isInstalled = true;

    /// <summary>
    /// Runtime-only, never persisted: whether this Steam game is actually installed on this PC.
    /// Always true for non-Steam games, which don't get install tracking.
    /// </summary>
    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value)
                return;

            _isInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInstallOverlay));
            OnPropertyChanged(nameof(ShowInstallIcon));
            OnPropertyChanged(nameof(ShowDownloadProgress));
        }
    }

    private double? _downloadPercent;

    /// <summary>Runtime-only: 0-100 while Steam is actively downloading this game, otherwise null.</summary>
    [JsonIgnore]
    public double? DownloadPercent
    {
        get => _downloadPercent;
        set
        {
            if (_downloadPercent == value)
                return;

            _downloadPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(ShowInstallIcon));
            OnPropertyChanged(nameof(ShowDownloadProgress));
        }
    }

    /// <summary>Whether the not-installed badge should show at all - the idle icon and the "Installing" state are mutually exclusive sub-states of this.</summary>
    [JsonIgnore]
    public bool ShowInstallOverlay => !IsInstalled;

    [JsonIgnore]
    public bool IsDownloading => DownloadPercent.HasValue;

    [JsonIgnore]
    public bool ShowInstallIcon => !IsInstalled && !IsDownloading;

    [JsonIgnore]
    public bool ShowDownloadProgress => !IsInstalled && IsDownloading;

    /// <summary>Steam is the app's implicit default platform and Manual has no platform to show, so only the less-common ones get a corner badge - keeps the common case visually quiet.</summary>
    [JsonIgnore]
    public bool ShowPlatformBadge => Platform is GamePlatform.Epic or GamePlatform.Gog or GamePlatform.BattleNet or GamePlatform.Xbox;

    [JsonIgnore]
    public string PlatformBadgeText => Platform switch
    {
        GamePlatform.Epic => "EPIC",
        GamePlatform.Gog => "GOG",
        GamePlatform.BattleNet => "BATTLE.NET",
        GamePlatform.Xbox => "XBOX",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
