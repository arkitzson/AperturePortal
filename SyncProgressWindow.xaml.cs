using System.Windows;

namespace ApertureOS;

/// <summary>
/// A visible, blocking-by-convention progress indicator for the Steam sync + art-fetch flow.
/// Deliberately has no close/cancel affordance - the caller owns showing/closing it and disabling
/// whatever triggered the sync (see SettingsWindow.SyncSteamGamesAsync) for the same duration, so
/// there's no path for the operation to keep running invisibly after the window it was launched
/// from gets dismissed.
/// </summary>
public partial class SyncProgressWindow : Window
{
    public SyncProgressWindow()
    {
        InitializeComponent();
    }

    /// <summary>Updates the status line, and switches the bar from indeterminate to a real percentage once a current/total item count is known.</summary>
    public void UpdateStatus(string text, int? current = null, int? total = null)
    {
        StatusText.Text = text;

        if (current is { } c && total is { } t and > 0)
        {
            ProgressIndicator.IsIndeterminate = false;
            ProgressIndicator.Minimum = 0;
            ProgressIndicator.Maximum = t;
            ProgressIndicator.Value = c;
            ProgressCountText.Text = $"{c} / {t}";
        }
        else
        {
            ProgressIndicator.IsIndeterminate = true;
            ProgressCountText.Text = string.Empty;
        }
    }
}
