using System.Windows;
using System.Windows.Input;
using ApertureOS.Models;
using Microsoft.Win32;

namespace ApertureOS;

public partial class EmulatorConfigWindow : Window
{
    private readonly EmulatorConfig? _existing;

    public EmulatorConfig? Result { get; private set; }

    public EmulatorConfigWindow(EmulatorConfig? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is not null)
        {
            Title = "Edit Emulator";
            HeaderText.Text = "Edit Emulator";
            OkButton.Content = "Save";
            NameTextBox.Text = existing.Name;
            EmulatorPathTextBox.Text = existing.EmulatorPath;
            RomFolderTextBox.Text = existing.RomFolder;
            ConsoleTextBox.Text = existing.Console;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BrowseEmulator_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Emulator Executable",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            EmulatorPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseRomFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select ROM Folder"
        };

        if (dialog.ShowDialog(this) == true)
        {
            RomFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var emulatorPath = EmulatorPathTextBox.Text.Trim();
        var romFolder = RomFolderTextBox.Text.Trim();
        var console = ConsoleTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(emulatorPath) ||
            string.IsNullOrWhiteSpace(romFolder) || string.IsNullOrWhiteSpace(console))
        {
            MessageBox.Show(this, "Fill in every field - the console name is what lets scanned ROMs get categorized automatically.",
                "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new EmulatorConfig
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            Name = name,
            EmulatorPath = emulatorPath,
            RomFolder = romFolder,
            Console = console
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
