using System.Windows;
using System.Windows.Input;
using ApertureOS.Services;

namespace ApertureOS;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly GameLibraryService _libraryService;

    public SettingsWindow(SettingsService settingsService, GameLibraryService libraryService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _libraryService = libraryService;

        var settings = _settingsService.Load();
        ApiKeyTextBox.Text = settings.SteamGridDbApiKey;
        StartWithWindowsCheckBox.IsChecked = StartupService.IsEnabled();
        LaunchConsoleModeCheckBox.IsChecked = settings.LaunchInConsoleMode;
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

    /// <summary>
    /// A plain reload from disk - the main window already does this (and re-checks install state)
    /// automatically the instant Settings closes, but that's invisible while this dialog is still
    /// open. This gives an explicit, immediate "yes, it's current" without needing to close first.
    /// </summary>
    private void RefreshLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var games = _libraryService.LoadGames();
        RefreshStatusText.Text = $"Library refreshed - {games.Count} game{(games.Count == 1 ? "" : "s")} on file.";
    }
}
