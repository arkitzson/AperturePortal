using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ApertureOS.Models;

namespace ApertureOS;

public partial class LaunchingWindow : Window
{
    private const double HeaderBackdropBlurRadius = 18;
    private const double CoverBackdropBlurRadius = 55;

    public LaunchingWindow()
    {
        InitializeComponent();
    }

    public void SetGame(Game game)
    {
        GameNameText.Text = game.Name;

        var hasHeader = !string.IsNullOrWhiteSpace(game.HeaderImagePath) && File.Exists(game.HeaderImagePath);
        var backdropPath = hasHeader ? game.HeaderImagePath : game.CoverImagePath;

        // Steam-synced games can have no art yet (fetched automatically only when a SteamGridDB
        // key is configured), so unlike the pause overlay this can't assume a cover always exists.
        if (!string.IsNullOrWhiteSpace(backdropPath) && File.Exists(backdropPath))
        {
            BackdropImage.Source = new BitmapImage(new Uri(backdropPath));
        }
        BackdropBlur.Radius = hasHeader ? HeaderBackdropBlurRadius : CoverBackdropBlurRadius;

        if (!string.IsNullOrWhiteSpace(game.CoverImagePath) && File.Exists(game.CoverImagePath))
        {
            CoverImage.Source = new BitmapImage(new Uri(game.CoverImagePath));
        }
    }
}
