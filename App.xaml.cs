using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace ApertureOS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // This app leans heavily on Process/registry/P-Invoke calls from timer ticks to track
        // arbitrary third-party games (elevated anti-cheat, protocol-handler launches, etc.), and
        // those can fail in ways that are impossible to fully enumerate up front. Without this,
        // an unhandled exception on any DispatcherTimer tick silently takes down the entire app -
        // exactly what happened when a Steam game's exit tracking hit an access-denied case. Losing
        // whatever that one operation was trying to do is far better than the launcher vanishing
        // while a game is still running.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
    }
}

