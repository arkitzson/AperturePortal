using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ApertureOS.Services;

namespace ApertureOS;

public partial class SteamGridPickerWindow : Window
{
    private readonly SteamGridDbService _service;
    private readonly SteamGridImageKind _kind;
    private readonly ObservableCollection<SteamGridDbGame> _matches = new();
    private readonly ObservableCollection<SteamGridDbImage> _images = new();
    private bool _isBusy;

    public string? ResultImagePath { get; private set; }

    public SteamGridPickerWindow(SteamGridDbService service, SteamGridImageKind kind, string initialSearchTerm)
    {
        InitializeComponent();
        _service = service;
        _kind = kind;

        var title = kind == SteamGridImageKind.Cover ? "Find Cover Art" : "Find Header Art";
        Title = title;
        HeaderText.Text = title;
        ImagesItemsControl.ItemTemplate =
            (DataTemplate)FindResource(kind == SteamGridImageKind.Cover ? "CoverThumbTemplate" : "HeaderThumbTemplate");

        MatchesItemsControl.ItemsSource = _matches;
        ImagesItemsControl.ItemsSource = _images;

        SearchTextBox.Text = initialSearchTerm;
        Loaded += async (_, _) => await SearchAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        var term = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(term) || _isBusy)
            return;

        _matches.Clear();
        _images.Clear();
        SetBusy(true, "Searching...");

        try
        {
            var results = await _service.SearchGamesAsync(term);
            foreach (var match in results)
            {
                _matches.Add(match);
            }

            if (results.Count == 0)
            {
                StatusText.Text = "No matches found.";
            }
            else
            {
                await LoadImagesAsync(results[0]);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void MatchOption_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { DataContext: SteamGridDbGame game })
            return;

        await LoadImagesAsync(game);
    }

    private async Task LoadImagesAsync(SteamGridDbGame game)
    {
        _images.Clear();
        SetBusy(true, "Loading art...");

        try
        {
            var images = _kind == SteamGridImageKind.Cover
                ? await _service.GetGridsAsync(game.Id)
                : await _service.GetHeroesAsync(game.Id);

            foreach (var image in images)
            {
                _images.Add(image);
            }

            StatusText.Text = images.Count == 0 ? "No art found for this game." : string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load art: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ImageOption_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { DataContext: SteamGridDbImage image })
            return;

        SetBusy(true, "Downloading...");

        try
        {
            var bytes = await _service.DownloadImageAsync(image.Url);
            var extension = Path.GetExtension(new Uri(image.Url).AbsolutePath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";

            var tempPath = Path.Combine(Path.GetTempPath(), $"sgdb_{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(tempPath, bytes);

            ResultImagePath = tempPath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download failed: {ex.Message}";
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _isBusy = busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
