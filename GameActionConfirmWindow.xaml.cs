using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

/// <summary>
/// Small "Install this game?" / "Launch this game?" confirmation, used identically from the normal
/// mouse-driven window and gamepad-driven Console Mode. Left/Right switches focus between the two
/// buttons and A activates whichever is focused, mirroring OverlayWindow's pause-menu pattern -
/// necessary because gamepad state is polled directly from XInput rather than routed through WPF's
/// normal focus system, so it needs its own input loop instead of just relying on IsDefault/IsCancel.
/// </summary>
public partial class GameActionConfirmWindow : Window
{
    private readonly DispatcherTimer _inputTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly GamepadEdge _leftEdge = new();
    private readonly GamepadEdge _rightEdge = new();
    private readonly GamepadEdge _confirmEdge = new();
    private readonly GamepadEdge _cancelEdge = new();

    // Only the buttons actually shown - info-only mode (see below) collapses CancelButton, and a
    // collapsed button should never be a valid Left/Right landing spot for the gamepad loop.
    private Button[] Buttons => new[] { ConfirmButton, CancelButton }.Where(b => b.Visibility == Visibility.Visible).ToArray();

    /// <summary>
    /// Normal "Install/Launch this game?" confirmation with both buttons. The gamepad button-legend
    /// hint only makes sense in Console Mode - the desktop window is a mouse/keyboard surface (it
    /// still quietly accepts gamepad input underneath, since the desktop grid itself does too, but
    /// doesn't advertise it) - so isConsoleMode controls whether that hint line shows at all.
    /// </summary>
    public GameActionConfirmWindow(Game game, string actionVerb, bool isConsoleMode)
        : this(game, $"{actionVerb} \"{game.Name}\"?", actionVerb, infoOnly: false, isConsoleMode)
    {
    }

    /// <summary>
    /// Info-only variant with just a single dismiss button - used when a game is already mid-download,
    /// so pressing its tile again explains that instead of re-offering to install it from scratch.
    /// </summary>
    public static GameActionConfirmWindow CreateInfoOnly(Game game, string message, bool isConsoleMode) =>
        new(game, message, "OK", infoOnly: true, isConsoleMode);

    private GameActionConfirmWindow(Game game, string message, string confirmButtonText, bool infoOnly, bool isConsoleMode)
    {
        InitializeComponent();

        MessageText.Text = message;
        ConfirmButton.Content = confirmButtonText;
        if (infoOnly)
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }

        HintText.Visibility = isConsoleMode ? Visibility.Visible : Visibility.Collapsed;
        if (isConsoleMode && infoOnly)
        {
            HintText.Text = "A or B to close";
        }

        if (File.Exists(game.CoverImagePath))
        {
            CoverImage.Source = new BitmapImage(new Uri(game.CoverImagePath));
        }

        Loaded += (_, _) =>
        {
            ConfirmButton.Focus();

            // Prime the trackers to the *actual* current physical state rather than assuming
            // nothing's held: this dialog is opened by an A press on a tile (see
            // ConsoleModeWindow.LaunchSelected/MainWindow.ActivateFocusedElement), and that same
            // press is very often still physically down for this window's first tick or two.
            // Without this, that residual hold reads as a brand-new edge the moment polling starts
            // and instantly activates Confirm on a press the user never intended as "confirm this" -
            // or doesn't, depending on exactly how fast they release, which is what made the number
            // of presses actually needed to launch a game feel inconsistent. Syncing first makes it
            // deterministic: always a real release of the opening press, then a deliberate second
            // press to confirm.
            var pad = GamepadService.Poll();
            _leftEdge.Sync(pad.Left);
            _rightEdge.Sync(pad.Right);
            _confirmEdge.Sync(pad.A);
            _cancelEdge.Sync(pad.B);

            _inputTimer.Start();
        };
        Closed += (_, _) => _inputTimer.Stop();
        _inputTimer.Tick += InputTimer_Tick;
    }

    private void InputTimer_Tick(object? sender, EventArgs e)
    {
        var pad = GamepadService.Poll();
        _leftEdge.Update(pad.Left, () => MoveSelection(-1));
        _rightEdge.Update(pad.Right, () => MoveSelection(1));
        _confirmEdge.Update(pad.A, ActivateFocusedButton);
        _cancelEdge.Update(pad.B, () =>
        {
            DialogResult = false;
            Close();
        });
    }

    private void MoveSelection(int direction)
    {
        var buttons = Buttons;
        int currentIndex = Array.IndexOf(buttons, Keyboard.FocusedElement as Button);
        int newIndex = Math.Clamp((currentIndex < 0 ? 0 : currentIndex) + direction, 0, buttons.Length - 1);
        buttons[newIndex].Focus();
    }

    private static void ActivateFocusedButton()
    {
        if (Keyboard.FocusedElement is Button button)
        {
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
