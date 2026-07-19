using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ApertureOS.Models;
using ApertureOS.Services;
using Microsoft.Win32;

namespace ApertureOS;

public partial class AddGameWindow : Window
{
    private readonly GameLibraryService _libraryService;
    private readonly SettingsService _settingsService;
    private readonly Game? _editingGame;

    public Game? ResultGame { get; private set; }

    public AddGameWindow(GameLibraryService libraryService, SettingsService settingsService, Game? gameToEdit = null)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _settingsService = settingsService;
        _editingGame = gameToEdit;

        if (gameToEdit is not null)
        {
            Title = "Edit Game";
            HeaderText.Text = "Edit Game";
            SaveButton.Content = "Save Changes";

            NameTextBox.Text = gameToEdit.Name;
            ExePathTextBox.Text = gameToEdit.ExePath;
            ImagePathTextBox.Text = gameToEdit.CoverImagePath;
            HeaderImagePathTextBox.Text = gameToEdit.HeaderImagePath;
            SelectPlatform(gameToEdit.Platform);

            SetPreview(CoverPreviewImage, gameToEdit.CoverImagePath);
            SetPreview(HeaderPreviewImage, gameToEdit.HeaderImagePath);
        }
    }

    /// <summary>Checks whichever ComboBoxItem's Tag matches the game's platform - mirrors the RadioButton Tag-parsing pattern used for library filter tabs elsewhere in this app.</summary>
    private void SelectPlatform(GamePlatform platform)
    {
        foreach (var item in PlatformComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && Enum.TryParse<GamePlatform>(tag, out var itemPlatform) && itemPlatform == platform)
            {
                PlatformComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static void SetPreview(Image target, string imagePath)
    {
        if (!File.Exists(imagePath))
            return;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath);
        bitmap.EndInit();
        target.Source = bitmap;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Game Executable",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            ExePathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Cover Image",
            Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            ImagePathTextBox.Text = dialog.FileName;
            SetPreview(CoverPreviewImage, dialog.FileName);
        }
    }

    private void BrowseHeaderImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Header Image",
            Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            HeaderImagePathTextBox.Text = dialog.FileName;
            SetPreview(HeaderPreviewImage, dialog.FileName);
        }
    }

    private void FindCoverArt_Click(object sender, RoutedEventArgs e) =>
        FindArt(SteamGridImageKind.Cover, ImagePathTextBox, CoverPreviewImage);

    private void FindHeaderArt_Click(object sender, RoutedEventArgs e) =>
        FindArt(SteamGridImageKind.Header, HeaderImagePathTextBox, HeaderPreviewImage);

    private void FindArt(SteamGridImageKind kind, TextBox pathBox, Image previewImage)
    {
        var apiKey = _settingsService.Load().SteamGridDbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Prompt for the key right here instead of just telling the user to go find
            // Settings themselves, then fall straight through into the picker once it's saved.
            var settingsWindow = new SettingsWindow(_settingsService, _libraryService) { Owner = this };
            if (settingsWindow.ShowDialog() != true)
                return;

            apiKey = _settingsService.Load().SteamGridDbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                return;
        }

        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a game name first so we know what to search for.", "Missing Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var service = new SteamGridDbService(apiKey);
        var picker = new SteamGridPickerWindow(service, kind, name) { Owner = this };

        if (picker.ShowDialog() == true && picker.ResultImagePath is { } resultPath)
        {
            pathBox.Text = resultPath;
            SetPreview(previewImage, resultPath);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var exePath = ExePathTextBox.Text.Trim();
        var imagePath = ImagePathTextBox.Text.Trim();
        var headerImagePath = HeaderImagePathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Please enter a game name.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Steam-synced games store a steam://rungameid/{appid} URI rather than a real file path.
        var isSteamUri = exePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(exePath) || (!isSteamUri && !File.Exists(exePath)))
        {
            MessageBox.Show(this, "Please select a valid game executable.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            MessageBox.Show(this, "Please select a cover image.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Reuse the existing stored image instead of copying a new one when the user
        // didn't change it, so editing a game doesn't leave orphaned duplicate files behind.
        var storedImagePath = imagePath == _editingGame?.CoverImagePath
            ? imagePath
            : _libraryService.StoreCoverImage(imagePath, name);

        // Header image is optional.
        var storedHeaderImagePath = string.IsNullOrWhiteSpace(headerImagePath) || !File.Exists(headerImagePath)
            ? string.Empty
            : headerImagePath == _editingGame?.HeaderImagePath
                ? headerImagePath
                : _libraryService.StoreHeaderImage(headerImagePath, name);

        var platform = PlatformComboBox.SelectedItem is ComboBoxItem { Tag: string platformTag } &&
                       Enum.TryParse<GamePlatform>(platformTag, out var selectedPlatform)
            ? selectedPlatform
            : GamePlatform.Manual;

        ResultGame = new Game
        {
            Name = name,
            ExePath = exePath,
            CoverImagePath = storedImagePath,
            HeaderImagePath = storedHeaderImagePath,
            Platform = platform,
            // Editing replaces the whole Game object (see MainWindow.EditGame_Click) rather than
            // mutating the existing one in place, so anything not exposed on this form has to be
            // carried over explicitly here or it silently resets - this was quietly wiping a
            // game's "Recently Played" status/timestamp any time it got edited.
            LastPlayedAt = _editingGame?.LastPlayedAt
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
