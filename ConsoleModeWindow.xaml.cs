using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

public partial class ConsoleModeWindow : Window
{
    private readonly MainWindow _owner;
    private readonly DispatcherTimer _inputTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };

    private readonly GamepadRepeater _upRepeater = new();
    private readonly GamepadRepeater _downRepeater = new();
    private readonly GamepadRepeater _leftRepeater = new();
    private readonly GamepadRepeater _rightRepeater = new();
    private readonly GamepadEdge _aEdge = new();
    private readonly GamepadEdge _bEdge = new();
    private readonly GamepadEdge _lbEdge = new();
    private readonly GamepadEdge _rbEdge = new();

    // Same order the filter tabs appear in, so LB/RB can step through them as a simple ring.
    private static readonly LibraryFilter[] FilterOrder =
    [
        LibraryFilter.Installed, LibraryFilter.RecentlyPlayed, LibraryFilter.All, LibraryFilter.NotInstalled
    ];

    private int _columns = 5;
    private int _selectedIndex;
    private bool _dialogOpen;

    public ConsoleModeWindow(MainWindow owner)
    {
        // Assigned before InitializeComponent() - the XAML no longer sets IsChecked="True" on a
        // filter tab for exactly this reason (see the comment on that markup), but this ordering
        // is cheap extra safety against the same class of bug if that ever changes again.
        _owner = owner;

        InitializeComponent();

        // Shares MainWindow's filtered/sorted view rather than the raw list, so a filter picked in
        // either window - desktop tabs or these LB/RB-driven ones - applies to both consistently.
        GameListBox.ItemsSource = _owner.GamesView;

        _inputTimer.Tick += InputTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _owner.GameSessionEnded += Owner_GameSessionEnded;

        SyncFilterTabToOwner();
        UpdateColumns();
        if (GameListBox.Items.Count > 0)
        {
            SelectIndex(0);
        }

        ResetInputTrackers();
        _inputTimer.Start();
    }

    /// <summary>Reflects whichever filter was already active (e.g. picked from the desktop tabs before entering Console Mode) in this window's own tab row, without re-triggering a redundant filter change.</summary>
    private void SyncFilterTabToOwner()
    {
        var button = _owner.CurrentFilter switch
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

    private void FilterTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse<LibraryFilter>(tag, out var filter))
            return;

        _owner.SetLibraryFilter(filter);
        _selectedIndex = 0;
        if (GameListBox.Items.Count > 0)
        {
            SelectIndex(0);
        }
    }

    private void CycleFilter(int direction)
    {
        int currentIndex = Array.IndexOf(FilterOrder, _owner.CurrentFilter);
        int newIndex = (currentIndex + direction + FilterOrder.Length) % FilterOrder.Length;
        var button = FilterOrder[newIndex] switch
        {
            LibraryFilter.Installed => FilterInstalledButton,
            LibraryFilter.NotInstalled => FilterNotInstalledButton,
            LibraryFilter.RecentlyPlayed => FilterRecentButton,
            _ => FilterAllButton
        };

        // Setting IsChecked fires FilterTab_Checked, which does the actual filtering/reselection.
        button.IsChecked = true;
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _inputTimer.Stop();
        _inputTimer.Tick -= InputTimer_Tick;
        _owner.GameSessionEnded -= Owner_GameSessionEnded;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColumns();
    }

    private void UpdateColumns()
    {
        // Mirrors the tile width + margin used in the ItemTemplate/ItemContainerStyle below, so
        // Up/Down navigation math lines up with how WrapPanel actually wraps rows: 210 content +
        // 12 padding (6x2) + 6 border (3x2) + 20 margin (10x2) = 248. This was previously 230,
        // which overestimated the column count and made Up/Down overshoot past the tile directly
        // above/below. Uses the list's own ActualWidth rather than the window's, since that
        // already excludes the surrounding Grid's margin - the actual space WrapPanel wraps within.
        const double slotWidth = 248;
        _columns = Math.Max(1, (int)(GameListBox.ActualWidth / slotWidth));
    }

    private void Owner_GameSessionEnded(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            Activate();

            // The gap between hiding for a game and reappearing here is an entire play session,
            // not a split-second - so unlike the overlay's Exit Game case, there's no real risk of
            // a still-held button meaning "fire immediately". Resetting means a press that arrived
            // while hidden (and so got tracked as "held" without its gated action running - see
            // InputTimer_Tick) doesn't require an extra release-then-press to register now.
            ResetInputTrackers();
        });
    }

    private void ResetInputTrackers()
    {
        _upRepeater.Reset();
        _downRepeater.Reset();
        _leftRepeater.Reset();
        _rightRepeater.Reset();
        _aEdge.Reset();
        _bEdge.Reset();
        _lbEdge.Reset();
        _rbEdge.Reset();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Move(0, -1);
                e.Handled = true;
                break;
            case Key.Down:
                Move(0, 1);
                e.Handled = true;
                break;
            case Key.Left:
                Move(-1, 0);
                e.Handled = true;
                break;
            case Key.Right:
                Move(1, 0);
                e.Handled = true;
                break;
            case Key.Enter:
                LaunchSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                GoBack();
                e.Handled = true;
                break;
        }
    }

    private void InputTimer_Tick(object? sender, EventArgs e)
    {
        var pad = GamepadService.Poll();

        // LaunchSelected() calls Hide() rather than Close(), so this window (and its timer)
        // stays alive for the whole game session to catch Owner_GameSessionEnded. Without this
        // guard, XInput state would keep reaching LaunchSelected()/GoBack() while hidden and
        // the same controller the player is using in-game would launch more games underneath it.
        //
        // _dialogOpen additionally covers the Install/Launch confirmation popup: it polls the
        // same raw XInput state through its own input loop, so without this an A press meant for
        // its Confirm button would also reach this grid and move/relaunch selection underneath it.
        bool inputAllowed = IsVisible && !_dialogOpen;

        // Feed the repeaters/edges the real button state and gate the action instead of
        // ANDing it into "pressed" - see the equivalent comment in MainWindow.GamepadTimer_Tick
        // for why: it stops a button held through a hidden period from firing the instant
        // this window becomes visible again.
        _upRepeater.Update(pad.Up, () => { if (inputAllowed) Move(0, -1); });
        _downRepeater.Update(pad.Down, () => { if (inputAllowed) Move(0, 1); });
        _leftRepeater.Update(pad.Left, () => { if (inputAllowed) Move(-1, 0); });
        _rightRepeater.Update(pad.Right, () => { if (inputAllowed) Move(1, 0); });
        _aEdge.Update(pad.A, () => { if (inputAllowed) LaunchSelected(); });
        _bEdge.Update(pad.B, () => { if (inputAllowed) GoBack(); });
        _lbEdge.Update(pad.LeftShoulder, () => { if (inputAllowed) CycleFilter(-1); });
        _rbEdge.Update(pad.RightShoulder, () => { if (inputAllowed) CycleFilter(1); });
    }

    private void Move(int columnDelta, int rowDelta)
    {
        // GameListBox.Items.Count (the current filtered view) rather than _owner.Games.Count (the
        // full library) - see the equivalent fix/comment in MainWindow.MoveTileFocus.
        if (GameListBox.Items.Count == 0)
            return;

        int newIndex = _selectedIndex + columnDelta + rowDelta * _columns;
        if (newIndex < 0 || newIndex >= GameListBox.Items.Count)
            return;

        SelectIndex(newIndex);
    }

    private void SelectIndex(int index)
    {
        _selectedIndex = index;
        GameListBox.SelectedIndex = index;
        GameListBox.ScrollIntoView(GameListBox.SelectedItem);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (GameListBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
            {
                container.Focus();
            }
        }), DispatcherPriority.Input);
    }

    private void LaunchSelected()
    {
        // Read the game back out of the filtered view (GameListBox.Items), not _owner.Games by
        // raw index - _selectedIndex is a position within whatever's currently filtered/sorted,
        // which very rarely lines up with the same index in the full, unfiltered library.
        if (_selectedIndex < 0 || _selectedIndex >= GameListBox.Items.Count ||
            GameListBox.Items[_selectedIndex] is not Game game)
        {
            return;
        }

        _dialogOpen = true;
        var launched = _owner.ConfirmAndActOnGame(game, this);
        _dialogOpen = false;

        // Only a real launch should hide this grid for the game session - installing just kicks
        // off a background download and the user should stay right here to keep browsing/watch
        // the tile's progress badge, same as in the normal window.
        if (launched)
        {
            Hide();
        }
    }

    private void GoBack()
    {
        Close();
    }
}
