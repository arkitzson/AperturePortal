using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

public partial class AddGamesWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly GameLibraryService _libraryService;
    private readonly SteamLibraryService _steamLibraryService = new();
    private readonly EpicGamesLibraryService _epicLibraryService = new();
    private readonly GogLibraryService _gogLibraryService = new();
    private readonly LibraryScanService _scanService = new();

    private readonly ObservableCollection<EmulatorConfig> _emulators = new();
    private readonly ObservableCollection<string> _installedFolders = new();

    public AddGamesWindow(GameLibraryService libraryService, SettingsService settingsService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _settingsService = settingsService;

        var settings = _settingsService.Load();
        SteamWebApiKeyTextBox.Text = settings.SteamWebApiKey;
        RefreshSteamStatus();

        foreach (var emulator in settings.Emulators)
        {
            _emulators.Add(emulator);
        }
        EmulatorsItemsControl.ItemsSource = _emulators;

        foreach (var folder in settings.InstalledGameFolders)
        {
            _installedFolders.Add(folder);
        }
        InstalledFoldersItemsControl.ItemsSource = _installedFolders;

        // Checked here rather than via IsChecked="True" in XAML - see the comment on this button
        // in AddGamesWindow.xaml for why.
        ManualTabButton.IsChecked = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Toggles which of the five panels is visible - same mechanical shape as
    /// MainWindow.FilterTab_Checked, just driving Visibility instead of a collection filter.</summary>
    private void SourceTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
            return;

        ManualPanel.Visibility = tag == "Manual" ? Visibility.Visible : Visibility.Collapsed;
        SteamPanel.Visibility = tag == "Steam" ? Visibility.Visible : Visibility.Collapsed;
        OtherLaunchersPanel.Visibility = tag == "OtherLaunchers" ? Visibility.Visible : Visibility.Collapsed;
        EmulatorsPanel.Visibility = tag == "Emulators" ? Visibility.Visible : Visibility.Collapsed;
        InstalledPanel.Visibility = tag == "Installed" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===================== Manual =====================

    private void AddGameManually_Click(object sender, RoutedEventArgs e)
    {
        var addGameWindow = new AddGameWindow(_libraryService, _settingsService) { Owner = this };
        if (addGameWindow.ShowDialog() != true || addGameWindow.ResultGame is not { } newGame)
            return;

        var games = _libraryService.LoadGames();
        games.Add(newGame);
        _libraryService.SaveGames(games);
    }

    // ===================== Steam =====================

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
        // immediately, matching every other instant-persist action in this window.
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

    private void RemovePlatformLibrary(GamePlatform platform, string platformLabel, TextBlock statusText) =>
        RemoveGamesMatching(g => g.Platform == platform, $"{platformLabel}-synced games", statusText);

    /// <summary>
    /// Shared by every "Remove Games"/"Remove X Library" button in this window: confirm → delete the
    /// removed games' owned cover/header image files → save the library minus that set → status
    /// text. Generalizes what used to be Steam/Epic/GOG-specific duplicate logic so the Emulator and
    /// Installed Folders per-source removals below don't repeat it a fourth and fifth time.
    /// </summary>
    private void RemoveGamesMatching(Func<Game, bool> predicate, string description, TextBlock statusText)
    {
        var allGames = _libraryService.LoadGames();
        var toRemove = allGames.Where(predicate).ToList();

        if (toRemove.Count == 0)
        {
            MessageBox.Show(this, $"There are no {description} in your library.",
                "Nothing to Remove", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(this,
            $"Remove all {toRemove.Count} {description} from your library?\n\n" +
            "This only removes the library entries - it won't uninstall or delete any game files, " +
            "and anything not matched by this action is left alone.",
            "Remove Games", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        foreach (var game in toRemove)
        {
            DeleteOwnedImage(game.CoverImagePath);
            DeleteOwnedImage(game.HeaderImagePath);
        }

        _libraryService.SaveGames(allGames.Except(toRemove));
        statusText.Text = $"Removed {toRemove.Count} game(s) from your library.";
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

        // Persist immediately so a sync right after pasting the key doesn't get lost.
        if (fullLibrary && !string.IsNullOrWhiteSpace(webApiKey) && settings.SteamWebApiKey != webApiKey)
        {
            settings.SteamWebApiKey = webApiKey;
            _settingsService.Save(settings);
        }

        SyncInstalledGamesButton.IsEnabled = false;
        SyncFullLibraryButton.IsEnabled = false;
        SteamSyncStatusText.Text = "Syncing...";

        // No Save/Cancel footer here (every action instant-persists), but the title-bar close still
        // needs blocking for the sync's duration - same reasoning as the old SettingsWindow: Close()
        // doesn't cancel a pending await, so the art-fetch loop below would keep running invisibly
        // in the background if the window closed mid-sync.
        TitleBarCloseButton.IsEnabled = false;
        SyncProgressWindow? progressWindow = null;

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
                progressWindow = new SyncProgressWindow { Owner = this };
                progressWindow.UpdateStatus("Installing your games and fetching art...", 0, newEntries.Count);
                progressWindow.Show();

                using var steamGridService = new SteamGridDbService(settings.SteamGridDbApiKey);
                for (var i = 0; i < newEntries.Count; i++)
                {
                    var statusText = $"Fetching art... {i + 1}/{newEntries.Count}";
                    SteamSyncStatusText.Text = statusText;
                    progressWindow.UpdateStatus(statusText, i + 1, newEntries.Count);
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
            progressWindow?.Close();
            TitleBarCloseButton.IsEnabled = true;
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

    // ===================== Epic & GOG =====================

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
    /// Best-effort art fetch by name search - used for Epic/GOG syncs and (see AddScannedGamesAsync)
    /// the Installed Folders scan, all cases where SteamGridDB has no direct catalog ID to look up by
    /// (no Steam AppID to key off), so this is the same search a user would get from AddGameWindow's
    /// "Find Art" button, just automatic and taking the first result.
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

    // ===================== Emulators =====================

    private void AddEmulator_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EmulatorConfigWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } emulator)
            return;

        _emulators.Add(emulator);
        PersistEmulators();
    }

    private void EditEmulator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EmulatorConfig existing })
            return;

        var dialog = new EmulatorConfigWindow(existing) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } updated)
            return;

        var index = _emulators.IndexOf(existing);
        if (index >= 0)
        {
            _emulators[index] = updated;
        }
        PersistEmulators();
    }

    private void RemoveEmulator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EmulatorConfig existing })
            return;

        _emulators.Remove(existing);
        PersistEmulators();
    }

    private void PersistEmulators()
    {
        var settings = _settingsService.Load();
        settings.Emulators = _emulators.ToList();
        _settingsService.Save(settings);
    }

    private async void ScanEmulator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EmulatorConfig emulator })
            return;

        var existingExePaths = _libraryService.LoadGames().Select(g => g.ExePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = _scanService.ScanEmulatorFolder(emulator.RomFolder, existingExePaths);

        if (candidates.Count == 0)
        {
            MessageBox.Show(this, $"No new files found in {emulator.RomFolder}.",
                "Nothing to Add", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var reviewDialog = new ScanResultsWindow(
            $"Found Games - {emulator.Name}",
            $"Found {candidates.Count} file(s) in the ROM folder. Uncheck anything that isn't actually a game.",
            candidates) { Owner = this };

        if (reviewDialog.ShowDialog() != true || reviewDialog.SelectedCandidates.Count == 0)
            return;

        var newGames = reviewDialog.SelectedCandidates.Select(c => new Game
        {
            Name = c.DisplayName,
            ExePath = c.ExePath,
            IsEmulated = true,
            EmulatorPath = emulator.EmulatorPath,
            Console = emulator.Console,
            Platform = GamePlatform.Manual
        }).ToList();

        await AddScannedGamesAsync(newGames, EmulatorScanStatusText);
    }

    private void RemoveEmulatorGames_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EmulatorConfig emulator })
            return;

        RemoveGamesMatching(
            g => g.IsEmulated && g.EmulatorPath == emulator.EmulatorPath,
            $"games added through {emulator.Name}",
            EmulatorScanStatusText);
    }

    // ===================== Installed Folders =====================

    private void AddInstalledFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Games Folder" };
        if (dialog.ShowDialog(this) != true)
            return;

        if (!_installedFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            _installedFolders.Add(dialog.FolderName);
            PersistInstalledFolders();
        }
    }

    private void RemoveInstalledFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder })
            return;

        _installedFolders.Remove(folder);
        PersistInstalledFolders();
    }

    private void PersistInstalledFolders()
    {
        var settings = _settingsService.Load();
        settings.InstalledGameFolders = _installedFolders.ToList();
        _settingsService.Save(settings);
    }

    private async void ScanInstalledFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_installedFolders.Count == 0)
        {
            MessageBox.Show(this, "Add at least one folder above first.",
                "No Folders Configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existingExePaths = _libraryService.LoadGames().Select(g => g.ExePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = _scanService.ScanForInstalledExecutables(_installedFolders, existingExePaths);

        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "No new executables found in the configured folders.",
                "Nothing to Add", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var reviewDialog = new ScanResultsWindow(
            "Found Games",
            $"Found {candidates.Count} executable(s) across your configured folders. Uncheck anything that isn't actually a game.",
            candidates) { Owner = this };

        if (reviewDialog.ShowDialog() != true || reviewDialog.SelectedCandidates.Count == 0)
            return;

        var newGames = reviewDialog.SelectedCandidates.Select(c => new Game
        {
            Name = c.DisplayName,
            ExePath = c.ExePath,
            Platform = GamePlatform.Manual
        }).ToList();

        await AddScannedGamesAsync(newGames, InstalledScanStatusText);
    }

    private void RemoveInstalledFolderGames_Click(object sender, RoutedEventArgs e)
    {
        RemoveGamesMatching(
            g => _installedFolders.Any(f => g.ExePath.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
            "games added from your configured folders",
            InstalledScanStatusText);
    }

    // ===================== Shared scan → save → art-fetch pipeline =====================

    /// <summary>
    /// Shared by both scan flows: saves the new entries immediately (so partial progress survives
    /// even if art-fetching is interrupted), then runs a best-effort SteamGridDB art-fetch pass
    /// through a SyncProgressWindow, same shape as SyncSteamGamesAsync's own art-fetch loop.
    /// </summary>
    private async Task AddScannedGamesAsync(List<Game> newGames, TextBlock statusText)
    {
        var existingGames = _libraryService.LoadGames();
        existingGames.AddRange(newGames);
        _libraryService.SaveGames(existingGames);

        statusText.Text = newGames.Count == 1 ? "Added 1 new game." : $"Added {newGames.Count} new games.";

        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
        {
            statusText.Text += " Set your SteamGridDB key in Settings to fetch cover art automatically.";
            return;
        }

        SyncProgressWindow? progressWindow = null;
        try
        {
            progressWindow = new SyncProgressWindow { Owner = this };
            progressWindow.UpdateStatus("Fetching art...", 0, newGames.Count);
            progressWindow.Show();

            using var steamGridService = new SteamGridDbService(settings.SteamGridDbApiKey);
            for (var i = 0; i < newGames.Count; i++)
            {
                var progressText = $"Fetching art... {i + 1}/{newGames.Count}";
                statusText.Text = progressText;
                progressWindow.UpdateStatus(progressText, i + 1, newGames.Count);
                try
                {
                    await FetchArtByNameAsync(steamGridService, newGames[i]);
                }
                catch
                {
                    // Best-effort: one game's art failing shouldn't stop the rest or fail an
                    // otherwise-successful scan, same rationale as the Steam sync's art loop.
                }
            }

            _libraryService.SaveGames(existingGames);
            statusText.Text = newGames.Count == 1
                ? "Added 1 new game and fetched its art."
                : $"Added {newGames.Count} new games and fetched art where available.";
        }
        finally
        {
            progressWindow?.Close();
        }
    }
}
