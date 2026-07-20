using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

public enum LibraryFilter
{
    All,
    Installed,
    NotInstalled,
    RecentlyPlayed
}

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4001;
    private const uint ModControl = 0x0002;
    private const uint VkP = 0x50;
    private const int WmHotkey = 0x0312;
    private const double TileSlotWidth = 176; // 160 tile width + 16 right margin, matches the ItemTemplate below.

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    // A launched process that exits this quickly is almost certainly a bootstrapper/dispatcher
    // handing off to the real game rather than the game itself being closed (e.g. Stellar Blade's
    // SB.exe exiting once SB-Win64-Shipping.exe takes over, or steam:// URIs: Steam hands the
    // request to its own already-running client and the process we started exits in well under a
    // second - long before the actual game has even created a window).
    private static readonly TimeSpan BootstrapperExitThreshold = TimeSpan.FromSeconds(8);

    // How long to keep watching for the real game to take over the foreground after a
    // suspiciously-fast exit before giving up and treating the session as genuinely over.
    private static readonly TimeSpan AdoptionGracePeriod = TimeSpan.FromSeconds(90);

    private readonly GameLibraryService _libraryService = new();
    private readonly SettingsService _settingsService = new();
    private readonly SteamLibraryService _steamLibraryService = new();
    private readonly ObservableCollection<Game> _games = new();
    private ICollectionView _gamesView = null!;
    private LibraryFilter _currentFilter = LibraryFilter.All;
    private string _searchText = string.Empty;

    // DispatcherPriority.Input (rather than the default Background) keeps this ticking promptly
    // even while the window is unfocused behind a running game, which is exactly when the
    // Back+Start pause combo needs to work.
    private readonly DispatcherTimer _gamepadTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };

    // Background priority and a slower interval - this only drives download-progress tiles, so it
    // doesn't need gamepad-level responsiveness and shouldn't compete with it.
    private readonly DispatcherTimer _installProgressTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly GamepadRepeater _padUpRepeater = new();
    private readonly GamepadRepeater _padDownRepeater = new();
    private readonly GamepadRepeater _padLeftRepeater = new();
    private readonly GamepadRepeater _padRightRepeater = new();
    private readonly GamepadEdge _padAEdge = new();
    private readonly GamepadEdge _overlayComboEdge = new();
    private int? _focusedTileIndex;

    private readonly ProcessSuspender _gameSuspender = new();
    private IntPtr _suspendedWindowHandle;

    private static readonly TimeSpan LaunchingSplashTimeout = TimeSpan.FromSeconds(30);

    private HwndSource? _hwndSource;
    private Process? _runningGameProcess;
    private Game? _runningGame;
    private DateTime _runningProcessStartedAt;
    private DateTime? _adoptionDeadline;
    private bool _isChildDialogOpen;
    private OverlayWindow? _overlayWindow;
    private LaunchingWindow? _launchingWindow;
    private DateTime _launchingWindowShownAt;
    private bool _hotkeyRegistered;

    public ObservableCollection<Game> Games => _games;

    /// <summary>The live filtered/sorted/searched view Console Mode's grid binds to as well, so filter state is shared across both UIs.</summary>
    public ICollectionView GamesView => _gamesView;

    public LibraryFilter CurrentFilter => _currentFilter;

    public event EventHandler? GameSessionEnded;

    public MainWindow()
    {
        // Restore whichever tab was selected last session, before anything below reads
        // _currentFilter - falls back to the field initializer's All if this is the first
        // launch ever or the saved value is somehow no longer a valid LibraryFilter name.
        if (Enum.TryParse<LibraryFilter>(_settingsService.Load().LastLibraryFilter, out var savedFilter))
        {
            _currentFilter = savedFilter;
        }

        // Built before InitializeComponent(): none of the filter tabs set IsChecked="True" in XAML
        // (SyncFilterTabToSetting checks the right one in code, once InitializeComponent() is done
        // and _currentFilter's restored value is known), but GamesItemsControl.ItemsSource below
        // still needs InitializeComponent() to have run first to exist, so _gamesView has to be
        // built ahead of it regardless.
        _gamesView = CollectionViewSource.GetDefaultView(_games);
        _gamesView.Filter = MatchesCurrentFilter;
        ApplySort(_currentFilter);

        // Without live shaping, a game finishing its download while the "Not Installed" filter is
        // active would just sit there until something else forces a re-filter (e.g. Refresh()) -
        // this makes the view react the instant IsInstalled flips, same tick the polling timer sets it.
        if (_gamesView is ICollectionViewLiveShaping liveShaping && liveShaping.CanChangeLiveFiltering)
        {
            liveShaping.LiveFilteringProperties.Add(nameof(Game.IsInstalled));
            liveShaping.IsLiveFiltering = true;
        }

        InitializeComponent();

        SyncFilterTabToSetting();

        GamesItemsControl.ItemsSource = _gamesView;

        foreach (var game in _libraryService.LoadGames())
        {
            _games.Add(game);
        }
        RefreshInstallStates();

        _gamepadTimer.Tick += GamepadTimer_Tick;
        _gamepadTimer.Start();

        _installProgressTimer.Tick += InstallProgressTimer_Tick;
        _installProgressTimer.Start();
    }

    private bool MatchesCurrentFilter(object obj)
    {
        if (obj is not Game game)
            return false;

        bool matchesCategory = _currentFilter switch
        {
            LibraryFilter.Installed => game.IsInstalled,
            LibraryFilter.NotInstalled => !game.IsInstalled,
            LibraryFilter.RecentlyPlayed => game.LastPlayedAt.HasValue,
            _ => true
        };

        if (!matchesCategory)
            return false;

        return string.IsNullOrWhiteSpace(_searchText) ||
               game.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void FilterTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<LibraryFilter>(tag, out var filter))
        {
            SetLibraryFilter(filter);
        }
    }

    /// <summary>Shared by the desktop filter tabs and Console Mode's LB/RB cycling, so both stay in sync on one filter state.</summary>
    public void SetLibraryFilter(LibraryFilter filter)
    {
        _currentFilter = filter;
        ApplySort(filter);
        _gamesView.Refresh();

        // The grid just reshuffled under whatever tile/index the keyboard or a controller had
        // focused - stale coordinates from the old layout would send Up/Down/Left/Right off in
        // the wrong direction until the next full press-from-nothing.
        _focusedTileIndex = null;

        // Load-modify-save rather than holding a long-lived settings instance, matching every
        // other write to settings.json in this app - avoids clobbering a change made elsewhere
        // (e.g. SettingsWindow) between this window's launch and now.
        var settings = _settingsService.Load();
        settings.LastLibraryFilter = filter.ToString();
        _settingsService.Save(settings);
    }

    /// <summary>Checks whichever tab matches the LastLibraryFilter setting restored in the constructor - mirrors ConsoleModeWindow.SyncFilterTabToOwner.</summary>
    private void SyncFilterTabToSetting()
    {
        var button = _currentFilter switch
        {
            LibraryFilter.Installed => FilterInstalledButton,
            LibraryFilter.NotInstalled => FilterNotInstalledButton,
            LibraryFilter.RecentlyPlayed => FilterRecentButton,
            _ => FilterAllButton
        };

        if (!button.IsChecked!.Value)
        {
            button.IsChecked = true;
        }
    }

    /// <summary>Alphabetical by default; Recently Played instead sorts by most-recently-launched.</summary>
    private void ApplySort(LibraryFilter filter)
    {
        _gamesView.SortDescriptions.Clear();
        _gamesView.SortDescriptions.Add(filter == LibraryFilter.RecentlyPlayed
            ? new SortDescription(nameof(Game.LastPlayedAt), ListSortDirection.Descending)
            : new SortDescription(nameof(Game.Name), ListSortDirection.Ascending));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text.Trim();
        _gamesView.Refresh();
        _focusedTileIndex = null;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Re-derives IsInstalled/DownloadPercent for every game from Steam's local manifests - call after the library list changes (load, sync, add).</summary>
    private void RefreshInstallStates()
    {
        foreach (var game in _games)
        {
            UpdateInstallState(game);
        }
    }

    private void UpdateInstallState(Game game)
    {
        if (SteamLibraryService.TryGetSteamAppId(game.ExePath) is not { } appId)
        {
            // Not a Steam game - no manifest to track, always considered installed.
            game.IsInstalled = true;
            game.DownloadPercent = null;
            return;
        }

        var progress = _steamLibraryService.GetInstallProgress(appId);
        if (progress is null)
        {
            // No manifest anywhere - never installed, nothing downloading.
            game.IsInstalled = false;
            game.DownloadPercent = null;
            return;
        }

        game.IsInstalled = progress.IsInstalled;
        game.DownloadPercent = progress.IsInstalled ? null : progress.PercentComplete;
    }

    /// <summary>
    /// Only re-checks games already known to be not-installed, rather than every game every tick -
    /// with a library in the hundreds, re-parsing every manifest 0.67 times a second for tiles that
    /// haven't changed would be pure waste. Catches downloads Steam itself started too, not just
    /// ones triggered from here, since it's a plain manifest re-read rather than tracking "our" installs.
    /// </summary>
    private void InstallProgressTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var game in _games)
        {
            if (!game.IsInstalled)
            {
                UpdateInstallState(game);
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        _gamepadTimer.Stop();
        _installProgressTimer.Stop();
        UnregisterGameHotkey();
        _hwndSource?.RemoveHook(WndProc);
        _launchingWindow?.Close();
        base.OnClosed(e);
    }

    private void GamepadTimer_Tick(object? sender, EventArgs e)
    {
        // The "Starting..." splash should disappear the moment something other than us (the real
        // game, or its loading screen) takes real OS foreground - no need to wait for adoption or
        // session tracking, since this is purely about whether the splash still has anything
        // useful to show. Also time it out in case the game never visibly takes over (e.g. a
        // background utility with no window of its own), so it can't get stuck open forever.
        if (_launchingWindow is not null &&
            (!IsThisAppForeground() || DateTime.UtcNow - _launchingWindowShownAt > LaunchingSplashTimeout))
        {
            _launchingWindow.Close();
            _launchingWindow = null;
        }

        // A tracked process exited suspiciously fast (see GameProcess_Exited) and we're waiting
        // to see whether the real game takes over the foreground before giving up on it.
        if (_adoptionDeadline is { } deadline)
        {
            if (TryAdoptForegroundProcess())
            {
                _adoptionDeadline = null;
            }
            else if (DateTime.UtcNow >= deadline)
            {
                EndGameSession();
            }
        }

        var pad = GamepadService.Poll();

        // Works no matter which window has focus, so a paused game can still be
        // resumed or exited via controller even while it owns the foreground.
        _overlayComboEdge.Update(pad.Back && pad.Start, ToggleOverlay);

        // XInput isn't exclusive: the same controller state is visible to every process,
        // including a running game. While a game is up, the launcher must ignore everything
        // except the Back+Start combo above, or button presses meant for the game (e.g. A)
        // will also drive our grid and launch other tiles in the background.
        //
        // This deliberately checks the real OS foreground window rather than trusting
        // _runningGameProcess/IsActive: many games launch via a bootstrapper that hands off
        // to a child process and then exits (e.g. Stellar Blade's SB.exe exits once
        // SB-Win64-Shipping.exe takes over), which fires our Exited handler and clears
        // _runningGameProcess even though the actual game is still running and focused.
        // The overlay handles its own A/B input while visible, so skip here too.
        //
        // IsThisAppForeground() only proves *some* window of this process is foreground, not
        // this one - Console Mode Hides this window (not Closes it) for the whole game session,
        // so its timer keeps running right alongside ConsoleModeWindow's. Without the IsVisible
        // check, this hidden grid would still react to the same controller and could launch a
        // second game underneath the one already running (see the equivalent guard/comment on
        // ConsoleModeWindow.InputTimer_Tick).
        //
        // _isChildDialogOpen covers the Install/Launch confirmation: that dialog polls the same
        // raw XInput state through its own input loop (see GameActionConfirmWindow), so without
        // this an A press meant for its Confirm button would also reach this grid underneath and
        // re-activate whatever tile is focused.
        bool gridInputAllowed = IsVisible && _overlayWindow is not { IsVisible: true } && !_isChildDialogOpen && IsThisAppForeground();

        // Always feed the repeaters/edge the *real* button state (not ANDed with
        // gridInputAllowed) and only gate the resulting action instead. Otherwise a button
        // held through a blocked period (e.g. A still down right as Exit Game closes the
        // overlay) looks like a brand-new press the instant input re-arms, firing immediately
        // instead of requiring an actual release-then-press.
        _padUpRepeater.Update(pad.Up, () => { if (gridInputAllowed) MoveTileFocus(0, -1); });
        _padDownRepeater.Update(pad.Down, () => { if (gridInputAllowed) MoveTileFocus(0, 1); });
        _padLeftRepeater.Update(pad.Left, () => { if (gridInputAllowed) MoveTileFocus(-1, 0); });
        _padRightRepeater.Update(pad.Right, () => { if (gridInputAllowed) MoveTileFocus(1, 0); });
        _padAEdge.Update(pad.A, () => { if (gridInputAllowed) ActivateFocusedElement(); });
    }

    /// <summary>True only when a window belonging to this process is the real OS foreground window.</summary>
    private static bool IsThisAppForeground()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out uint foregroundProcessId);
        return foregroundProcessId == (uint)Environment.ProcessId;
    }

    private void MoveTileFocus(int columnDelta, int rowDelta)
    {
        // If focus is currently on a header button, Down re-enters the grid and
        // Left/Right hops between the two buttons; Up/Down otherwise falls through.
        if (Keyboard.FocusedElement == ConsoleModeButton || Keyboard.FocusedElement == AddGameButton)
        {
            if (rowDelta > 0)
            {
                FocusTile(_focusedTileIndex ?? 0);
            }
            else if (columnDelta != 0)
            {
                (Keyboard.FocusedElement == ConsoleModeButton ? AddGameButton : ConsoleModeButton).Focus();
            }

            return;
        }

        // GamesItemsControl.Items.Count (the current filtered view), not _games.Count (the full,
        // unfiltered library) - otherwise Up/Down/Left/Right math would assume tiles are present
        // that the active filter is actually hiding.
        if (GamesItemsControl.Items.Count == 0)
            return;

        // Nothing in the grid has been focused yet (e.g. right after launch, before any
        // keyboard/controller input) - the first press just establishes a starting point on
        // the first tile rather than computing an offset from a stale/assumed index.
        if (_focusedTileIndex is not { } currentIndex)
        {
            _focusedTileIndex = 0;
            FocusTile(0);
            return;
        }

        int columns = Math.Max(1, (int)(GamesItemsControl.ActualWidth / TileSlotWidth));
        int newIndex = currentIndex + columnDelta + rowDelta * columns;

        if (newIndex < 0 && rowDelta < 0)
        {
            // Walked off the top row; hand focus up to the header buttons.
            AddGameButton.Focus();
            return;
        }

        if (newIndex < 0 || newIndex >= GamesItemsControl.Items.Count)
            return;

        _focusedTileIndex = newIndex;
        FocusTile(newIndex);
    }

    private void FocusTile(int index)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (GamesItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter presenter)
                return;

            presenter.ApplyTemplate();
            if (FindVisualChild<Button>(presenter) is { } button)
            {
                button.Focus();
            }
        }), DispatcherPriority.Input);
    }

    private static void ActivateFocusedElement()
    {
        if (Keyboard.FocusedElement is Button button)
        {
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            if (FindVisualChild<T>(child) is { } found)
                return found;
        }

        return null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void RegisterGameHotkey()
    {
        if (_hotkeyRegistered)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        _hotkeyRegistered = RegisterHotKey(handle, HotkeyId, ModControl, VkP);
    }

    private void UnregisterGameHotkey()
    {
        if (!_hotkeyRegistered)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    private void ToggleOverlay()
    {
        if (_runningGameProcess is null || _runningGameProcess.HasExited)
            return;

        if (_overlayWindow is null)
        {
            _overlayWindow = new OverlayWindow();
            _overlayWindow.ResumeRequested += Overlay_ResumeRequested;
            _overlayWindow.ExitGameRequested += Overlay_ExitGameRequested;
        }

        if (_overlayWindow.IsVisible)
        {
            HideOverlayAndResumeGame();
        }
        else
        {
            // Set every time the overlay is about to be shown, not just when the window is first
            // created: this instance is reused across game sessions, so if a previous session
            // ever left it hanging around (e.g. "Exit Game" not fully ending the session), it
            // would otherwise keep showing whichever game was set the last time it was created.
            if (_runningGame is not null)
            {
                _overlayWindow.SetGame(_runningGame);
            }

            bool suspended = SuspendCurrentGame();
            _overlayWindow.SetSuspendStatus(suspended);
            _overlayWindow.Show();
            _overlayWindow.Activate();
        }
    }

    /// <summary>
    /// Freezes whatever process currently owns the OS foreground window (the game), not
    /// necessarily <see cref="_runningGameProcess"/>: some games launch via a bootstrapper that
    /// hands off to a child process and exits, which already makes _runningGameProcess stale for
    /// other purposes (see the comment in GamepadTimer_Tick). Suspending the real foreground
    /// process is the only reliable way to stop a game from reacting to controller input while
    /// paused, since XInput has no exclusive-access concept - see ProcessSuspender.
    /// </summary>
    /// <returns>
    /// False if nothing could actually be frozen (e.g. the game runs elevated, or under DRM/
    /// anti-tamper protection, while this app doesn't), so the caller can tell the user the
    /// pause didn't really take instead of showing a "Paused" screen over a game that's still
    /// running underneath it.
    /// </returns>
    private bool SuspendCurrentGame()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            return false;

        try
        {
            var process = Process.GetProcessById((int)pid);

            // A true DirectX exclusive-fullscreen game owns the display output directly,
            // bypassing the normal window manager entirely - no Topmost window (ours included)
            // can render over that, no matter how forcefully we steal OS foreground focus.
            // Minimizing is what normally makes a game release exclusive fullscreen (the same
            // thing that happens on Alt+Tab), but that only happens if the game's own message
            // loop is still alive to react to it. Do this *before* suspending, or its threads
            // freeze before it ever gets the chance and the display stays stuck on the game's
            // last frame with the overlay invisible underneath it.
            //
            // A fixed sleep here used to be enough to mask this, back when Suspend() silently
            // failed on most games (elevated/DRM-protected ones - see ProcessSuspender) and their
            // message loops stayed alive regardless. Now that Suspend() actually works, a heavy
            // engine that hasn't finished processing WM_SIZE within a fixed window gets its
            // threads frozen mid-transition, stuck showing its last frame with nothing able to
            // render on top - so poll for the minimize to really take effect instead of guessing
            // at a delay, and fall through best-effort if it never does.
            ShowWindow(foregroundWindow, SwMinimize);
            var minimizeDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            while (!IsIconic(foregroundWindow) && DateTime.UtcNow < minimizeDeadline)
            {
                Thread.Sleep(20);
            }

            _gameSuspender.Suspend(process);
            _suspendedWindowHandle = foregroundWindow;
            return _gameSuspender.LastSuspendSucceeded;
        }
        catch (ArgumentException)
        {
            // Process exited between the foreground lookup and GetProcessById; nothing to suspend.
            return false;
        }
    }

    private void HideOverlayAndResumeGame()
    {
        _overlayWindow?.Hide();
        _gameSuspender.Resume();

        if (_suspendedWindowHandle != IntPtr.Zero)
        {
            ShowWindow(_suspendedWindowHandle, SwRestore);
            SetForegroundWindow(_suspendedWindowHandle);
            _suspendedWindowHandle = IntPtr.Zero;
        }
        else if (_runningGameProcess is { HasExited: false } && _runningGameProcess.MainWindowHandle != IntPtr.Zero)
        {
            SetForegroundWindow(_runningGameProcess.MainWindowHandle);
        }
    }

    private void Overlay_ResumeRequested(object? sender, EventArgs e)
    {
        HideOverlayAndResumeGame();
    }

    private void Overlay_ExitGameRequested(object? sender, EventArgs e)
    {
        // Prefer the process we actually suspended (the real foreground game) over
        // _runningGameProcess, which can be a bootstrapper that already exited - see the
        // comment on SuspendCurrentGame. The threads are still suspended; Discard() just
        // releases our handles to them since termination works fine without resuming first.
        var process = _gameSuspender.SuspendedProcess ?? _runningGameProcess;
        _gameSuspender.Discard();
        _suspendedWindowHandle = IntPtr.Zero;

        if (process is { HasExited: false })
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited; nothing to do.
            }
            catch (Exception ex)
            {
                // Most likely the game is running elevated (as administrator) while this
                // launcher is not, so we don't have permission to terminate it directly.
                // Fall back to an elevated taskkill, which will prompt for UAC itself.
                var (succeeded, details) = TryElevatedKill(process.Id);
                if (!succeeded)
                {
                    MessageBox.Show(this,
                        $"Failed to exit the game.\n\nOriginal error: {ex.Message}\nElevated taskkill: {details}",
                        "Exit Failed", MessageBoxButton.OK, MessageBoxImage.Error);

                    // Genuinely couldn't confirm the game died - leave the session state alone
                    // so the user can still pause/exit again, rather than ending a session whose
                    // game might still actually be running.
                    _overlayWindow?.Hide();
                    return;
                }
            }
        }

        // Don't rely on process.Exited to end the session here: the process we just killed
        // (found via a live foreground lookup in SuspendCurrentGame) isn't always the same
        // object instance as _runningGameProcess - e.g. if the real game hadn't been adopted yet
        // (see TryAdoptForegroundProcess) - so its Exited event may never fire for the tracked
        // reference, leaving the session stuck "active" forever: Console Mode's window would
        // never come back and the pause hotkey would stay registered for a game that's gone.
        if (_runningGameProcess is { HasExited: false })
        {
            _runningGameProcess.Exited -= GameProcess_Exited;
        }

        EndGameSession();
    }

    private static (bool Succeeded, string Details) TryElevatedKill(int processId)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/F /T /PID {processId}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var taskkill = Process.Start(startInfo);
            if (taskkill is null)
                return (false, "taskkill did not start.");

            if (!taskkill.WaitForExit(10000))
                return (false, "taskkill timed out after 10 seconds.");

            return taskkill.ExitCode == 0
                ? (true, "taskkill reported success.")
                : (false, $"taskkill exited with code {taskkill.ExitCode} (the game's anti-cheat/anti-tamper protection may be blocking external termination).");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void GameProcess_Exited(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            bool wasShortLived = DateTime.UtcNow - _runningProcessStartedAt < BootstrapperExitThreshold;

            if (wasShortLived && TryAdoptForegroundProcess())
                return;

            if (wasShortLived)
            {
                // Give the real game a bit longer to appear (checked every gamepad-timer tick via
                // TryAdoptForegroundProcess) instead of immediately treating this as the game
                // closing and popping the launcher UI back up while it's still starting.
                _adoptionDeadline = DateTime.UtcNow + AdoptionGracePeriod;
                return;
            }

            EndGameSession();
        });
    }

    /// <summary>
    /// Adopts whatever process currently owns the real OS foreground window as the new
    /// <see cref="_runningGameProcess"/>, so its eventual exit (rather than a short-lived
    /// bootstrapper's) is what ends the session. Won't adopt Steam's own client/overlay chrome,
    /// which can legitimately hold foreground for a while during the launch handoff but isn't the
    /// game itself.
    /// </summary>
    private bool TryAdoptForegroundProcess()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            return false;

        try
        {
            var process = Process.GetProcessById((int)pid);
            if (IsSteamClientProcess(process.ProcessName))
                return false;

            if (_runningGameProcess is { HasExited: false })
            {
                _runningGameProcess.Exited -= GameProcess_Exited;
            }

            _runningGameProcess = process;
            _runningProcessStartedAt = DateTime.UtcNow;
            process.EnableRaisingEvents = true;
            process.Exited += GameProcess_Exited;
            return true;
        }
        catch (ArgumentException)
        {
            // Process exited between the foreground lookup and GetProcessById.
            return false;
        }
        catch (Exception)
        {
            // EnableRaisingEvents needs SYNCHRONIZE/QUERY_INFORMATION access to the process,
            // which throws Win32Exception "Access is denied" for anti-cheat-protected or
            // otherwise elevated games (this app itself isn't elevated) - same reason
            // TryElevatedKill exists below. Losing exit-tracking for this one tick is much
            // better than an unhandled exception here taking down the whole app, since this
            // runs on the gamepad timer's tick.
            return false;
        }
    }

    private static bool IsSteamClientProcess(string processName) =>
        processName.Equals("steam", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase);

    private void EndGameSession()
    {
        _adoptionDeadline = null;
        _overlayWindow?.Close();
        _overlayWindow = null;
        _runningGameProcess = null;
        _runningGame = null;
        UnregisterGameHotkey();
        ForceGameGridRedraw();
        GameSessionEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returning from a game - especially one that ran true exclusive-fullscreen, which forces a
    /// GPU context switch - can leave WPF's hardware-accelerated bitmap caches for the tiles' blur
    /// and drop-shadow effects stale, showing up as thin dark seams around the tiles. Toggling
    /// visibility tears down and rebuilds those cached bitmaps from scratch; it's a single
    /// imperceptible frame, not a visible flicker.
    /// </summary>
    private void ForceGameGridRedraw()
    {
        GamesItemsControl.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(() => GamesItemsControl.Visibility = Visibility.Visible), DispatcherPriority.Render);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore_Click(sender, e);
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        var addGameWindow = new AddGameWindow(_libraryService, _settingsService) { Owner = this };

        if (addGameWindow.ShowDialog() == true && addGameWindow.ResultGame is { } newGame)
        {
            _games.Add(newGame);
            _libraryService.SaveGames(_games);
            UpdateInstallState(newGame);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settingsService, _libraryService) { Owner = this };
        settingsWindow.ShowDialog();

        // A Steam sync may have added games directly to the library file behind our back;
        // reload so the grid picks them up regardless of whether Settings was saved or cancelled.
        _games.Clear();
        foreach (var game in _libraryService.LoadGames())
        {
            _games.Add(game);
        }
        RefreshInstallStates();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        // Button.Click bubbles, and raising it here doesn't mark it Handled the way
        // MouseLeftButtonDown/Up do - without this, the click keeps bubbling to the
        // outer tile Button's own Click handler and launches the game underneath.
        e.Handled = true;

        if (sender is Button { ContextMenu: { } menu } button)
        {
            // Opening a ContextMenu programmatically (vs. the default right-click gesture)
            // doesn't inherit DataContext from PlacementTarget on its own, so the Game
            // wouldn't reach the MenuItem click handlers below without this.
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void EditGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Game game })
            return;

        int index = _games.IndexOf(game);
        if (index < 0)
            return;

        var editWindow = new AddGameWindow(_libraryService, _settingsService, game) { Owner = this };
        if (editWindow.ShowDialog() == true && editWindow.ResultGame is { } updatedGame)
        {
            _games[index] = updatedGame;
            _libraryService.SaveGames(_games);

            // updatedGame is a fresh Game (see AddGameWindow.Add_Click), so its runtime-only
            // IsInstalled defaults to true regardless of the old tile's actual state - without
            // this, editing a not-installed game would show it as installed until something else
            // forced a full refresh, since InstallProgressTimer_Tick only re-checks games already
            // known to be not-installed. AddGameButton_Click already does this for new games.
            UpdateInstallState(updatedGame);
        }
    }

    private void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Game game })
            return;

        var result = MessageBox.Show(this,
            $"Remove '{game.Name}' from your library?\n\nThis won't delete the game's files, only the library entry.",
            "Remove Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _games.Remove(game);
            _libraryService.SaveGames(_games);
        }
    }

    private void GameTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Game game })
            return;

        ConfirmAndActOnGame(game, this);
    }

    /// <summary>
    /// Shared by the mouse-driven grid and Console Mode: asks "Install/Launch this game?" first
    /// rather than acting immediately on a click/A-press, then does whichever is appropriate.
    /// Takes the owner explicitly since Console Mode needs the dialog centered on itself (a
    /// separate fullscreen window), not on this one while it's hidden behind it. Returns true only
    /// when a game session actually started (confirmed AND already installed) - Console Mode uses
    /// that, not just "wasn't cancelled", to decide whether to hide itself for the session, since
    /// installing shouldn't hide it and cancelling obviously shouldn't either.
    /// </summary>
    public bool ConfirmAndActOnGame(Game game, Window ownerWindow)
    {
        var isConsoleMode = ownerWindow is ConsoleModeWindow;

        // Already mid-download: re-showing the Install prompt here would just re-trigger
        // steam://install on top of a download already running, so this explains the actual
        // state instead of asking a question that doesn't apply anymore.
        if (game.IsDownloading)
        {
            var infoWindow = GameActionConfirmWindow.CreateInfoOnly(game, $"\"{game.Name}\" is currently installing...", isConsoleMode);
            infoWindow.Owner = ownerWindow;

            _isChildDialogOpen = true;
            infoWindow.ShowDialog();
            _isChildDialogOpen = false;
            return false;
        }

        var actionVerb = game.IsInstalled ? "Launch" : "Install";
        var confirmWindow = new GameActionConfirmWindow(game, actionVerb, isConsoleMode) { Owner = ownerWindow };

        _isChildDialogOpen = true;
        var confirmed = confirmWindow.ShowDialog() == true;
        _isChildDialogOpen = false;

        if (!confirmed)
            return false;

        if (game.IsInstalled)
        {
            LaunchGame(game);
            return true;
        }

        InstallGame(game);
        return false;
    }

    /// <summary>
    /// Hands the actual download off to the real Steam client via steam://install - ApertureOS
    /// doesn't (and shouldn't) reimplement Steam's own CDN/depot download machinery. The tile's
    /// progress display comes separately from polling the manifest Steam writes as it downloads
    /// (see InstallProgressTimer_Tick), not from anything tracked here.
    /// </summary>
    public void InstallGame(Game game)
    {
        if (SteamLibraryService.TryGetSteamAppId(game.ExePath) is not { } appId)
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = $"steam://install/{appId}", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start install for '{game.Name}':\n{ex.Message}",
                "Install Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Show *something* moving immediately rather than waiting for the next timer tick to find
        // a manifest that may not exist yet - the timer's own poll corrects this to the real
        // number as soon as Steam creates/updates the file.
        game.DownloadPercent = 0;
    }

    public void LaunchGame(Game game)
    {
        // Steam-synced games store a steam://rungameid/{appid} URI rather than a real file path
        // (Steam itself resolves the actual exe), so File.Exists would always fail for them.
        var isSteamUri = game.ExePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

        if (!isSteamUri && !File.Exists(game.ExePath))
        {
            MessageBox.Show(this, $"Could not find the executable for '{game.Name}'.\n{game.ExePath}",
                "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        game.LastPlayedAt = DateTime.UtcNow;
        _libraryService.SaveGames(_games);
        _gamesView.Refresh();

        try
        {
            if (_runningGameProcess is { HasExited: false })
            {
                _runningGameProcess.Exited -= GameProcess_Exited;
            }
            _adoptionDeadline = null;

            _launchingWindow?.Close();
            _launchingWindow = new LaunchingWindow();
            _launchingWindow.SetGame(game);
            _launchingWindow.Show();
            _launchingWindowShownAt = DateTime.UtcNow;

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = game.ExePath,
                WorkingDirectory = isSteamUri ? null : Path.GetDirectoryName(game.ExePath),
                UseShellExecute = true
            });

            if (process != null)
            {
                _runningGameProcess = process;
                _runningGame = game;
                _runningProcessStartedAt = DateTime.UtcNow;
                process.EnableRaisingEvents = true;
                process.Exited += GameProcess_Exited;
                RegisterGameHotkey();
            }
            else
            {
                // steam:// URIs handled via IPC by an already-running Steam client return null -
                // there's no process to track at all, so go straight to watching the foreground
                // for the real game window (same as a fast-exiting bootstrapper below).
                _runningGame = game;
                _runningProcessStartedAt = DateTime.UtcNow;
                _adoptionDeadline = DateTime.UtcNow + AdoptionGracePeriod;
                RegisterGameHotkey();
            }
        }
        catch (Exception ex)
        {
            _launchingWindow?.Close();
            _launchingWindow = null;

            MessageBox.Show(this, $"Failed to launch '{game.Name}':\n{ex.Message}",
                "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settingsService.Load().LaunchInConsoleMode)
        {
            EnterConsoleMode();
        }
    }

    private void ConsoleModeButton_Click(object sender, RoutedEventArgs e)
    {
        EnterConsoleMode();
    }

    private void EnterConsoleMode()
    {
        var consoleWindow = new ConsoleModeWindow(this);
        consoleWindow.Closed += (_, _) =>
        {
            Show();

            // Hide() stops this window from getting any input at all, including the mouse-move
            // events WPF normally uses to recompute IsMouseOver - so whatever tile had it at the
            // moment we hid stays stuck "live" internally, and real hover on other tiles after
            // Show() doesn't register until the next actual move updates it. Feeding WPF a
            // synthetic move at the cursor's current position forces that recompute immediately.
            Dispatcher.BeginInvoke(new Action(ResyncMouseOverState), DispatcherPriority.Input);
        };

        Hide();
        consoleWindow.Show();
    }

    private static void ResyncMouseOverState()
    {
        if (InputManager.Current.PrimaryMouseDevice is not { } mouseDevice)
            return;

        var args = new MouseEventArgs(mouseDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent };
        InputManager.Current.ProcessInput(args);
    }
}
