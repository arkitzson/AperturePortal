using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly GameLibraryService _libraryService;
    private readonly SteamLibraryService _steamLibraryService = new();
    private readonly EpicGamesLibraryService _epicLibraryService = new();
    private readonly GogLibraryService _gogLibraryService = new();

    public SettingsWindow(SettingsService settingsService, GameLibraryService libraryService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _libraryService = libraryService;

        var settings = _settingsService.Load();
        ApiKeyTextBox.Text = settings.SteamGridDbApiKey;
        SteamWebApiKeyTextBox.Text = settings.SteamWebApiKey;
        StartWithWindowsCheckBox.IsChecked = StartupService.IsEnabled();
        LaunchConsoleModeCheckBox.IsChecked = settings.LaunchInConsoleMode;
        RefreshSteamStatus();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        settings.SteamGridDbApiKey = ApiKeyTextBox.Text.Trim();
        settings.SteamWebApiKey = SteamWebApiKeyTextBox.Text.Trim();
        settings.LaunchInConsoleMode = LaunchConsoleModeCheckBox.IsChecked == true;
        _settingsService.Save(settings);

        StartupService.SetEnabled(StartWithWindowsCheckBox.IsChecked == true);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RefreshSteamStatus()
    {
        var settings = _settingsService.Load();
        var loggedIn = !string.IsNullOrWhiteSpace(settings.SteamLoginSecureCookie) && !string.IsNullOrWhiteSpace(settings.SteamId64);

        SteamLoggedOutPanel.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        SteamLoggedInPanel.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;

        if (loggedIn)
        {
            SteamIdText.Text = $"Logged in · SteamID {settings.SteamId64}";
        }
    }

    private void SteamWebLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new SteamWebLoginWindow { Owner = this };
        if (loginWindow.ShowDialog() != true ||
            loginWindow.LoginSecureCookie is not { } cookie ||
            loginWindow.SteamId64 is not { } steamId64)
        {
            return;
        }

        // An async external action the user just completed, not a pending edit - persist it
        // immediately rather than waiting for the Save button, so hitting Cancel afterward
        // doesn't silently throw away a successful sign-in.
        var settings = _settingsService.Load();
        settings.SteamLoginSecureCookie = cookie;
        settings.SteamId64 = steamId64;
        _settingsService.Save(settings);

        RefreshSteamStatus();
    }

    private void SteamLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        settings.SteamId64 = string.Empty;
        settings.SteamLoginSecureCookie = string.Empty;
        _settingsService.Save(settings);

        SteamSyncStatusText.Text = string.Empty;
        RefreshSteamStatus();
    }

    private void RemoveSteamLibraryButton_Click(object sender, RoutedEventArgs e) =>
        RemovePlatformLibrary(GamePlatform.Steam, "Steam", SteamSyncStatusText);

    private void RemoveEpicLibraryButton_Click(object sender, RoutedEventArgs e) =>
        RemovePlatformLibrary(GamePlatform.Epic, "Epic", EpicSyncStatusText);

    private void RemoveGogLibraryButton_Click(object sender, RoutedEventArgs e) =>
        RemovePlatformLibrary(GamePlatform.Gog, "GOG", GogSyncStatusText);

    private void RemovePlatformLibrary(GamePlatform platform, string platformLabel, TextBlock statusText)
    {
        var allGames = _libraryService.LoadGames();
        var platformGames = allGames.Where(g => g.Platform == platform).ToList();

        if (platformGames.Count == 0)
        {
            MessageBox.Show(this, $"There are no {platformLabel}-synced games in your library.",
                "Nothing to Remove", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(this,
            $"Remove all {platformGames.Count} {platformLabel}-synced games from your library?\n\n" +
            $"This only removes the library entries - it won't uninstall anything from {platformLabel} or touch any game files, " +
            "and manually-added games are left alone.",
            $"Remove {platformLabel} Library", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        foreach (var game in platformGames)
        {
            DeleteOwnedImage(game.CoverImagePath);
            DeleteOwnedImage(game.HeaderImagePath);
        }

        _libraryService.SaveGames(allGames.Except(platformGames));
        statusText.Text = $"Removed {platformGames.Count} {platformLabel}-synced games from your library.";
    }

    /// <summary>
    /// A plain reload from disk - the main window already does this (and re-checks install state)
    /// automatically the instant Settings closes, but that's invisible while this dialog is still
    /// open. This gives an explicit, immediate "yes, it's current" without needing to close first.
    /// </summary>
    private void RefreshLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var games = _libraryService.LoadGames();
        SteamSyncStatusText.Text = $"Library refreshed - {games.Count} game{(games.Count == 1 ? "" : "s")} on file.";
    }

    private static void DeleteOwnedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup - a locked/in-use file shouldn't block removing the library entry.
        }
    }

    private async void SyncInstalledGamesButton_Click(object sender, RoutedEventArgs e) =>
        await SyncSteamGamesAsync(fullLibrary: false);

    private async void SyncFullLibraryButton_Click(object sender, RoutedEventArgs e) =>
        await SyncSteamGamesAsync(fullLibrary: true);

    private async Task SyncSteamGamesAsync(bool fullLibrary)
    {
        var settings = _settingsService.Load();

        var webApiKey = SteamWebApiKeyTextBox.Text.Trim();
        var hasSession = !string.IsNullOrWhiteSpace(settings.SteamLoginSecureCookie) && !string.IsNullOrWhiteSpace(settings.SteamId64);

        if (fullLibrary && !hasSession && string.IsNullOrWhiteSpace(webApiKey))
        {
            MessageBox.Show(this,
                "Click \"Log in via Steam\" above, or enter a Steam Web API key, then try again.",
                "Full Library Access Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // The API-key fallback still needs a SteamID to query - only available once logged in
        // via browser at least once (even if the resulting session cookie later expires).
        if (fullLibrary && !hasSession && string.IsNullOrWhiteSpace(settings.SteamId64))
        {
            MessageBox.Show(this,
                "Log in via Steam above at least once first, so ApertureOS knows which account to sync.",
                "Steam Account Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Persist immediately so a sync right after pasting the key doesn't get lost if Save is
        // never clicked. Only when non-empty - an empty box just means "rely on the browser
        // session instead" and shouldn't wipe out a key saved earlier.
        if (fullLibrary && !string.IsNullOrWhiteSpace(webApiKey) && settings.SteamWebApiKey != webApiKey)
        {
            settings.SteamWebApiKey = webApiKey;
            _settingsService.Save(settings);
        }

        SyncInstalledGamesButton.IsEnabled = false;
        SyncFullLibraryButton.IsEnabled = false;
        SteamSyncStatusText.Text = "Syncing...";

        try
        {
            var steamGames = fullLibrary
                ? await FetchFullLibraryAsync(settings, webApiKey, hasSession)
                : _steamLibraryService.GetInstalledGames();

            var existingGames = _libraryService.LoadGames();
            var existingExePaths = existingGames.Select(g => g.ExePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newEntries = steamGames
                .DistinctBy(sg => sg.AppId)
                .Where(sg => !existingExePaths.Contains(SteamExePath(sg.AppId)))
                .Select(sg => (Info: sg, Game: new Game { Name = sg.Name, ExePath = SteamExePath(sg.AppId) }))
                .ToList();

            existingGames.AddRange(newEntries.Select(e => e.Game));
            _libraryService.SaveGames(existingGames);

            SteamSyncStatusText.Text = newEntries.Count switch
            {
                0 => "No new games to add - your library is already up to date.",
                1 => "Added 1 new game.",
                _ => $"Added {newEntries.Count} new games."
            };

            if (newEntries.Count > 0 && !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            {
                using var steamGridService = new SteamGridDbService(settings.SteamGridDbApiKey);
                for (var i = 0; i < newEntries.Count; i++)
                {
                    SteamSyncStatusText.Text = $"Fetching art... {i + 1}/{newEntries.Count}";
                    try
                    {
                        await FetchArtAsync(steamGridService, newEntries[i].Info, newEntries[i].Game);
                    }
                    catch
                    {
                        // Best-effort: one game's art failing (rate limit, no SteamGridDB entry,
                        // network hiccup) shouldn't stop the rest or fail an otherwise-successful sync.
                    }
                }

                _libraryService.SaveGames(existingGames);
                SteamSyncStatusText.Text = newEntries.Count == 1
                    ? "Added 1 new game and fetched its art."
                    : $"Added {newEntries.Count} new games and fetched art where available.";
            }
        }
        catch (Exception ex)
        {
            SteamSyncStatusText.Text = $"Steam sync failed: {ex.Message}";
        }
        finally
        {
            SyncInstalledGamesButton.IsEnabled = true;
            SyncFullLibraryButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Prefers a real logged-in browser session (covers Family Sharing games, no key needed) and
    /// only falls back to the Web API key if there's no session or the session has expired.
    /// </summary>
    private async Task<List<SteamAppInfo>> FetchFullLibraryAsync(AppSettings settings, string webApiKey, bool hasSession)
    {
        if (hasSession)
        {
            try
            {
                return await _steamLibraryService.GetOwnedGamesViaSessionAsync(settings.SteamId64, settings.SteamLoginSecureCookie);
            }
            catch (SteamSessionExpiredException)
            {
                settings.SteamLoginSecureCookie = string.Empty;
                _settingsService.Save(settings);
                RefreshSteamStatus();

                if (string.IsNullOrWhiteSpace(webApiKey))
                {
                    throw new InvalidOperationException(
                        "Your Steam session expired. Click \"Log in via Steam\" again, or enter a Web API key.");
                }
            }
        }

        return await _steamLibraryService.GetOwnedGamesAsync(settings.SteamId64, webApiKey);
    }

    /// <summary>Fetches the first cover grid and first header image SteamGridDB has for this Steam game, if any, and stores them on the Game.</summary>
    private async Task FetchArtAsync(SteamGridDbService steamGridService, SteamAppInfo steamGame, Game game)
    {
        var sgdbGameId = await steamGridService.GetGameIdBySteamAppIdAsync(steamGame.AppId);
        if (sgdbGameId is not { } gameId)
            return;

        var grids = await steamGridService.GetGridsAsync(gameId);
        if (grids.Count > 0)
        {
            var tempPath = await DownloadToTempFileAsync(steamGridService, grids[0].Url);
            game.CoverImagePath = _libraryService.StoreCoverImage(tempPath, game.Name);
            File.Delete(tempPath);
        }

        var heroes = await steamGridService.GetHeroesAsync(gameId);
        if (heroes.Count > 0)
        {
            var tempPath = await DownloadToTempFileAsync(steamGridService, heroes[0].Url);
            game.HeaderImagePath = _libraryService.StoreHeaderImage(tempPath, game.Name);
            File.Delete(tempPath);
        }
    }

    private async void SyncEpicButton_Click(object sender, RoutedEventArgs e) =>
        await SyncLocalPlatformAsync(
            GamePlatform.Epic, "Epic",
            () => _epicLibraryService.GetInstalledGames().Select(g => (Key: g.AppName, g.Name, g.ExePath)).ToList(),
            SyncEpicButton, EpicSyncStatusText);

    private async void SyncGogButton_Click(object sender, RoutedEventArgs e) =>
        await SyncLocalPlatformAsync(
            GamePlatform.Gog, "GOG",
            () => _gogLibraryService.GetInstalledGames().Select(g => (Key: g.GameId, g.Name, g.ExePath)).ToList(),
            SyncGogButton, GogSyncStatusText);

    /// <summary>
    /// Shared by Epic and GOG: both are a pure local scan with no login/API key (unlike Steam's
    /// two-tier installed-vs-full-library sync), and both store a direct exe path rather than a
    /// launcher URI, so - unlike Steam-synced games - they need no corresponding install/download
    /// tracking in MainWindow afterward; they behave exactly like a manually-added game once synced.
    /// </summary>
    private async Task SyncLocalPlatformAsync(
        GamePlatform platform, string platformLabel,
        Func<List<(string Key, string Name, string ExePath)>> getInstalledGames,
        Button syncButton, TextBlock statusText)
    {
        syncButton.IsEnabled = false;
        statusText.Text = "Syncing...";

        try
        {
            var foundGames = getInstalledGames();

            var existingGames = _libraryService.LoadGames();
            var existingExePaths = existingGames.Select(g => g.ExePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newEntries = foundGames
                .DistinctBy(g => g.Key)
                .Where(g => !existingExePaths.Contains(g.ExePath))
                .Select(g => new Game { Name = g.Name, ExePath = g.ExePath, Platform = platform })
                .ToList();

            existingGames.AddRange(newEntries);
            _libraryService.SaveGames(existingGames);

            statusText.Text = newEntries.Count switch
            {
                0 => "No new games to add - your library is already up to date.",
                1 => "Added 1 new game.",
                _ => $"Added {newEntries.Count} new games."
            };

            var settings = _settingsService.Load();
            if (newEntries.Count > 0 && !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            {
                using var steamGridService = new SteamGridDbService(settings.SteamGridDbApiKey);
                for (var i = 0; i < newEntries.Count; i++)
                {
                    statusText.Text = $"Fetching art... {i + 1}/{newEntries.Count}";
                    try
                    {
                        await FetchArtByNameAsync(steamGridService, newEntries[i]);
                    }
                    catch
                    {
                        // Best-effort: one game's art failing shouldn't stop the rest or fail an
                        // otherwise-successful sync, same rationale as the Steam sync's art loop.
                    }
                }

                _libraryService.SaveGames(existingGames);
                statusText.Text = newEntries.Count == 1
                    ? "Added 1 new game and fetched its art."
                    : $"Added {newEntries.Count} new games and fetched art where available.";
            }
        }
        catch (Exception ex)
        {
            statusText.Text = $"{platformLabel} sync failed: {ex.Message}";
        }
        finally
        {
            syncButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Best-effort art fetch for Epic/GOG syncs, by name search rather than the AppID-keyed lookup
    /// FetchArtAsync uses for Steam - SteamGridDB has no Epic/GOG catalog ID mapping wired up here,
    /// so this is the same search a user would get from AddGameWindow's "Find Art" button, just
    /// automatic and taking the first result.
    /// </summary>
    private async Task FetchArtByNameAsync(SteamGridDbService steamGridService, Game game)
    {
        var results = await steamGridService.SearchGamesAsync(game.Name);
        if (results.Count == 0)
            return;

        var gameId = results[0].Id;

        var grids = await steamGridService.GetGridsAsync(gameId);
        if (grids.Count > 0)
        {
            var tempPath = await DownloadToTempFileAsync(steamGridService, grids[0].Url);
            game.CoverImagePath = _libraryService.StoreCoverImage(tempPath, game.Name);
            File.Delete(tempPath);
        }

        var heroes = await steamGridService.GetHeroesAsync(gameId);
        if (heroes.Count > 0)
        {
            var tempPath = await DownloadToTempFileAsync(steamGridService, heroes[0].Url);
            game.HeaderImagePath = _libraryService.StoreHeaderImage(tempPath, game.Name);
            File.Delete(tempPath);
        }
    }

    private static async Task<string> DownloadToTempFileAsync(SteamGridDbService steamGridService, string url)
    {
        var bytes = await steamGridService.DownloadImageAsync(url);
        var extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(extension))
            extension = ".png";

        var tempPath = Path.Combine(Path.GetTempPath(), $"sgdb_{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(tempPath, bytes);
        return tempPath;
    }

    private static string SteamExePath(string appId) => $"steam://rungameid/{appId}";
}
