using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ApertureOS.Services;

namespace ApertureOS;

public partial class ScanResultsWindow : Window
{
    private readonly List<ScanCandidateViewModel> _candidates;

    public List<ScannedGameCandidate> SelectedCandidates { get; private set; } = [];

    public ScanResultsWindow(string title, string description, List<ScannedGameCandidate> candidates)
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        DescriptionText.Text = description;

        _candidates = candidates.Select(c => new ScanCandidateViewModel(c)).ToList();
        CandidatesItemsControl.ItemsSource = _candidates;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates)
        {
            candidate.IsSelected = true;
        }
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates)
        {
            candidate.IsSelected = false;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedCandidates = _candidates.Where(c => c.IsSelected).Select(c => c.Candidate).ToList();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>One checkable row in the scan-results checklist.</summary>
public class ScanCandidateViewModel : INotifyPropertyChanged
{
    public ScannedGameCandidate Candidate { get; }
    public string Path => Candidate.ExePath;
    public string DisplayName => Candidate.DisplayName;

    private bool _isSelected = true;

    public ScanCandidateViewModel(ScannedGameCandidate candidate)
    {
        Candidate = candidate;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
