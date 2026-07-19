using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace ApertureOS;

/// <summary>
/// Logs into real Steam inside an embedded browser (the same approach Playnite uses), then reuses
/// the resulting session cookies for library requests instead of an unauthenticated scrape - Valve
/// now rejects those outright regardless of profile privacy. Nothing entered here is seen by
/// ApertureOS itself; the credentials go straight to steamcommunity.com like any other browser tab.
/// </summary>
public partial class SteamWebLoginWindow : Window
{
    public string? LoginSecureCookie { get; private set; }
    public string? SteamId64 { get; private set; }

    public SteamWebLoginWindow()
    {
        InitializeComponent();
        Loaded += SteamWebLoginWindow_Loaded;
        Closed += (_, _) => LoginWebView.Dispose();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void SteamWebLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // A dedicated profile folder under our own AppData, rather than the default (next to
            // the exe), so this keeps working regardless of where ApertureOS is installed.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApertureOS", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await LoginWebView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            StatusOverlayText.Text =
                $"Couldn't start the embedded browser: {ex.Message}\n\nMake sure the Microsoft Edge WebView2 Runtime is installed.";
            return;
        }

        StatusOverlayText.Visibility = Visibility.Collapsed;
        LoginWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        LoginWebView.CoreWebView2.Navigate("https://steamcommunity.com/login/home/");
    }

    /// <summary>
    /// Steam's login page redirects through a few intermediate steps after a successful sign-in
    /// (2FA, "stay signed in", etc.), so this just checks after every completed navigation whether
    /// the auth cookie has shown up yet rather than trying to match a specific final URL.
    /// </summary>
    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
            return;

        var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://steamcommunity.com");
        var loginSecure = cookies.FirstOrDefault(c => c.Name == "steamLoginSecure");
        if (loginSecure is null || string.IsNullOrEmpty(loginSecure.Value))
            return;

        LoginSecureCookie = loginSecure.Value;

        // The cookie value is "{steamid}||{JWT}", still percent-encoded as the browser received it
        // (e.g. "%7C%7C" for "||"). Reading the SteamID straight out of it means this one login is
        // enough to both identify the account and authorize library requests - no separate sign-in
        // step needed.
        var decoded = Uri.UnescapeDataString(loginSecure.Value);
        var separatorIndex = decoded.IndexOf("||", StringComparison.Ordinal);
        if (separatorIndex < 0)
            return; // Cookie doesn't look like the expected "{steamid}||{JWT}" shape yet; keep waiting.

        SteamId64 = decoded[..separatorIndex];
        DialogResult = true;
        Close();
    }
}
