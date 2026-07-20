using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

public partial class OverlayWindow : Window
{
    // The header image is already roughly the right aspect ratio for a full-screen backdrop, so it
    // only needs a light blur; the cover art fallback is a portrait image stretched to fill a
    // landscape screen and needs a much heavier blur to hide how distorted that stretch looks.
    private const double HeaderBackdropBlurRadius = 18;
    private const double CoverBackdropBlurRadius = 55;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    // DispatcherPriority.Input keeps this responsive even if the overlay doesn't hold
    // foreground activation over an exclusive-fullscreen game.
    private readonly DispatcherTimer _inputTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly GamepadRepeater _upRepeater = new();
    private readonly GamepadRepeater _downRepeater = new();
    private readonly GamepadEdge _confirmEdge = new();

    private Button[] MenuButtons => [ResumeButton, ExitGameButton, SleepButton];

    public event EventHandler? ResumeRequested;
    public event EventHandler? ExitGameRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        IsVisibleChanged += OverlayWindow_IsVisibleChanged;
        _inputTimer.Tick += InputTimer_Tick;
    }

    /// <summary>Binds the suspend screen's art and title to the game currently being paused.</summary>
    public void SetGame(Game game)
    {
        DataContext = game;

        var hasHeader = !string.IsNullOrWhiteSpace(game.HeaderImagePath);
        var backdropPath = hasHeader ? game.HeaderImagePath : game.CoverImagePath;

        BackdropImage.Source = new BitmapImage(new Uri(backdropPath));
        BackdropBlur.Radius = hasHeader ? HeaderBackdropBlurRadius : CoverBackdropBlurRadius;
    }

    /// <summary>
    /// Reflects whether the game actually got frozen. A non-elevated Aperture Portal can't open
    /// thread handles on an elevated (or DRM/anti-tamper-protected) game process, so without this
    /// the menu would just say "Paused" over a game that's still fully running underneath it.
    /// </summary>
    public void SetSuspendStatus(bool suspended)
    {
        StatusText.Text = suspended
            ? "Paused"
            : "Couldn't pause: this game may be running with higher privileges than Aperture Portal";
        StatusText.Foreground = suspended
            ? (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
            : (System.Windows.Media.Brush)FindResource("CloseHoverBrush");
    }

    private void OverlayWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ResumeButton.Focus();
            _inputTimer.Start();

            // Topmost only wins the z-order fight; it does not take real input focus away
            // from a running game, so without this the D-pad/A press meant for this menu
            // keeps reaching the game underneath. This only works when the game is running
            // at the same (or lower) integrity level as this app - an elevated game still
            // wins, since a non-elevated process can never steal focus from one.
            ForceForeground(new WindowInteropHelper(this).Handle);
        }
        else
        {
            _inputTimer.Stop();
        }
    }

    private static void ForceForeground(IntPtr targetHwnd)
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        uint currentThreadId = GetCurrentThreadId();

        if (foregroundThreadId != currentThreadId)
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
            SetForegroundWindow(targetHwnd);
            BringWindowToTop(targetHwnd);
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
        else
        {
            SetForegroundWindow(targetHwnd);
        }
    }

    private void InputTimer_Tick(object? sender, EventArgs e)
    {
        var pad = GamepadService.Poll();
        _upRepeater.Update(pad.Up, () => MoveSelection(-1));
        _downRepeater.Update(pad.Down, () => MoveSelection(1));
        _confirmEdge.Update(pad.A, ActivateFocusedButton);
    }

    private void MoveSelection(int direction)
    {
        var buttons = MenuButtons;
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

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        ResumeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitGameButton_Click(object sender, RoutedEventArgs e)
    {
        ExitGameRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SleepButton_Click(object sender, RoutedEventArgs e)
    {
        SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
    }
}
