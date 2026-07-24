using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ApertureOS.Models;
using ApertureOS.Services;

namespace ApertureOS;

public partial class ManageCategoriesWindow : Window
{
    private readonly CategoryService _categoryService = new();
    private readonly GameLibraryService _libraryService;
    private readonly List<Game> _games;
    private readonly ObservableCollection<CategoryRowViewModel> _categoryRows = new();

    public ManageCategoriesWindow(GameLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _games = _libraryService.LoadGames();

        CategoriesListBox.ItemsSource = _categoryRows;
        RefreshCategoryRows();
    }

    private void RefreshCategoryRows(Guid? selectId = null)
    {
        var categories = _categoryService.LoadCategories().OrderBy(c => c.SortOrder).ToList();
        _categoryRows.Clear();
        foreach (var category in categories)
        {
            _categoryRows.Add(new CategoryRowViewModel(category, CountGamesInCategory(category)));
        }

        CategoriesEmptyState.Visibility = _categoryRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CategoriesListBox.Visibility = _categoryRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (selectId is { } id)
        {
            CategoriesListBox.SelectedItem = _categoryRows.FirstOrDefault(r => r.Id == id);
        }
    }

    private int CountGamesInCategory(GameCategory category) => category.Kind switch
    {
        CategoryKind.AutoConsole => _games.Count(g => g.Console == category.SourceKey),
        CategoryKind.AutoPlatform => _games.Count(g => CategoryService.GetPlatformDisplayName(g.Platform) == category.SourceKey),
        _ => _games.Count(g => g.CategoryIds.Contains(category.Id))
    };

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void NewCategory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CategoryNameWindow("New Category") { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultName is not { } name)
            return;

        var categories = _categoryService.LoadCategories();
        var nextSortOrder = categories.Count == 0 ? 0 : categories.Max(c => c.SortOrder) + 1;
        var newCategory = new GameCategory { Name = name, Kind = CategoryKind.Custom, SortOrder = nextSortOrder };
        categories.Add(newCategory);
        _categoryService.SaveCategories(categories);

        RefreshCategoryRows(newCategory.Id);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CategoryRowViewModel row })
            return;

        var dialog = new CategoryNameWindow("Rename Category", row.Name) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultName is not { } newName)
            return;

        var categories = _categoryService.LoadCategories();
        var category = categories.FirstOrDefault(c => c.Id == row.Id);
        if (category is null)
            return;

        category.Name = newName;
        _categoryService.SaveCategories(categories);

        RefreshCategoryRows(row.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CategoryRowViewModel row } || !row.IsCustom)
            return;

        var result = MessageBox.Show(this,
            $"Delete the category \"{row.Name}\"?\n\nGames in it aren't affected - they just won't be tagged with this category anymore.",
            "Delete Category", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        var categories = _categoryService.LoadCategories();
        categories.RemoveAll(c => c.Id == row.Id);
        _categoryService.SaveCategories(categories);

        foreach (var game in _games)
        {
            game.CategoryIds.Remove(row.Id);
        }
        _libraryService.SaveGames(_games);

        GamesItemsControl.ItemsSource = null;
        AssignmentPlaceholder.Visibility = Visibility.Visible;
        AssignmentContent.Visibility = Visibility.Collapsed;
        RefreshCategoryRows();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int direction)
    {
        if (sender is not FrameworkElement { Tag: CategoryRowViewModel row })
            return;

        var categories = _categoryService.LoadCategories().OrderBy(c => c.SortOrder).ToList();
        var index = categories.FindIndex(c => c.Id == row.Id);
        var swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= categories.Count)
            return;

        (categories[index].SortOrder, categories[swapIndex].SortOrder) = (categories[swapIndex].SortOrder, categories[index].SortOrder);
        _categoryService.SaveCategories(categories);

        RefreshCategoryRows(row.Id);
    }

    private void CategoriesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CategoriesListBox.SelectedItem is not CategoryRowViewModel row)
        {
            AssignmentPlaceholder.Visibility = Visibility.Visible;
            AssignmentContent.Visibility = Visibility.Collapsed;
            return;
        }

        AssignmentPlaceholder.Visibility = Visibility.Collapsed;
        AssignmentContent.Visibility = Visibility.Visible;

        if (row.IsCustom)
        {
            AssignmentHeaderText.Text = $"Games in \"{row.Name}\" - check to assign";
            var categoryId = row.Id;
            GamesItemsControl.ItemTemplate = (DataTemplate)Resources["GameCheckRowTemplate"];
            GamesItemsControl.ItemsSource = _games
                .OrderBy(g => g.Name)
                .Select(g => new GameCheckRowViewModel(g, categoryId, () => OnGameToggled(categoryId)))
                .ToList();

            // Every game shows up here regardless of membership (as an unchecked box) - so an
            // empty result means the library itself has nothing in it yet, not that this category
            // happens to be empty. Worth a real explanation instead of a blank checkbox list.
            NoGamesInLibraryText.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            // Auto membership is derived, not stored - same matching CountGamesInCategory already
            // uses, just projected to names since there's nothing here for the user to toggle.
            var names = row.Kind == CategoryKind.AutoConsole
                ? _games.Where(g => g.Console == row.SourceKey).Select(g => g.Name)
                : _games.Where(g => CategoryService.GetPlatformDisplayName(g.Platform) == row.SourceKey).Select(g => g.Name);

            AssignmentHeaderText.Text = $"Games in \"{row.Name}\" (auto-detected, read-only)";
            GamesItemsControl.ItemTemplate = (DataTemplate)Resources["GameNameRowTemplate"];
            GamesItemsControl.ItemsSource = names.OrderBy(n => n).ToList();
            NoGamesInLibraryText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnGameToggled(Guid categoryId)
    {
        _libraryService.SaveGames(_games);

        var row = _categoryRows.FirstOrDefault(r => r.Id == categoryId);
        if (row is not null)
        {
            row.GameCount = _games.Count(g => g.CategoryIds.Contains(categoryId));
        }
    }
}

/// <summary>Display wrapper around GameCategory for the reorderable list - computed labels don't belong on the persisted model itself.</summary>
public class CategoryRowViewModel : INotifyPropertyChanged
{
    private readonly GameCategory _category;
    private int _gameCount;

    public CategoryRowViewModel(GameCategory category, int gameCount)
    {
        _category = category;
        _gameCount = gameCount;
    }

    public Guid Id => _category.Id;
    public string Name => _category.Name;
    public CategoryKind Kind => _category.Kind;
    public string SourceKey => _category.SourceKey;
    public bool IsCustom => _category.Kind == CategoryKind.Custom;

    /// <summary>Only set for non-custom rows - explains why Delete is greyed out instead of just
    /// leaving it looking broken. Rename has no such restriction: auto categories can be renamed
    /// freely since GameCategory.SourceKey (not Name) is what actually ties them back to a
    /// console/platform. ShowOnDisabled is set on the Delete button in XAML since WPF tooltips
    /// don't show on disabled controls by default.</summary>
    public string? DeleteLockedTooltip => IsCustom
        ? null
        : "Auto-generated from your games' console/platform - the next matching game added would just recreate it, so this can't be deleted.";

    public string KindLabel => _category.Kind switch
    {
        CategoryKind.AutoConsole => "CONSOLE",
        CategoryKind.AutoPlatform => "PLATFORM",
        _ => "CUSTOM"
    };

    public int GameCount
    {
        get => _gameCount;
        set
        {
            if (_gameCount == value)
                return;

            _gameCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GameCountLabel));
        }
    }

    public string GameCountLabel => GameCount == 1 ? "1 game" : $"{GameCount} games";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>One checkable row in the game-assignment checklist for a selected custom category.</summary>
public class GameCheckRowViewModel : INotifyPropertyChanged
{
    private readonly Game _game;
    private readonly Guid _categoryId;
    private readonly Action _onToggled;

    public GameCheckRowViewModel(Game game, Guid categoryId, Action onToggled)
    {
        _game = game;
        _categoryId = categoryId;
        _onToggled = onToggled;
    }

    public string Name => _game.Name;

    public bool IsInCategory
    {
        get => _game.CategoryIds.Contains(_categoryId);
        set
        {
            if (value == IsInCategory)
                return;

            if (value)
            {
                _game.CategoryIds.Add(_categoryId);
            }
            else
            {
                _game.CategoryIds.Remove(_categoryId);
            }

            OnPropertyChanged();
            _onToggled();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
