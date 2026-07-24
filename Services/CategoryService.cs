using System.IO;
using System.Text.Json;
using ApertureOS.Models;

namespace ApertureOS.Services;

/// <summary>Manages categories.json - both auto-derived (console/platform) and user-created custom categories, and their shared reorderable sort position.</summary>
public class CategoryService
{
    private readonly string _categoriesFile;

    public CategoryService()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApertureOS");
        Directory.CreateDirectory(dataDir);
        _categoriesFile = Path.Combine(dataDir, "categories.json");
    }

    public List<GameCategory> LoadCategories()
    {
        if (!File.Exists(_categoriesFile))
            return [];

        var json = File.ReadAllText(_categoriesFile);
        var categories = JsonSerializer.Deserialize<List<GameCategory>>(json) ?? [];

        // Migrates categories.json written before SourceKey existed: Name used to double as the
        // join key for auto categories (see GameCategory's doc comment), so that's exactly what
        // SourceKey would have held for every one of them. Persisted immediately, not left to
        // whichever caller happens to save next - a rename only ever touches Name, so SourceKey
        // has to already be durable on disk before that can safely happen even once.
        var migrated = false;
        foreach (var category in categories)
        {
            if (category.Kind != CategoryKind.Custom && string.IsNullOrEmpty(category.SourceKey))
            {
                category.SourceKey = category.Name;
                migrated = true;
            }
        }

        if (migrated)
        {
            SaveCategories(categories);
        }

        return categories;
    }

    public void SaveCategories(IEnumerable<GameCategory> categories)
    {
        var json = JsonSerializer.Serialize(categories, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_categoriesFile, json);
    }

    /// <summary>
    /// Ensures an AutoConsole row exists for every distinct non-empty Game.Console, and an
    /// AutoPlatform row for every distinct Game.Platform, across the given games. Called from
    /// GameLibraryService.SaveGames rather than from any individual caller that creates/edits a
    /// game (AddGameWindow, Steam/Epic/GOG sync, etc.) - centralizing it there means every one of
    /// those paths keeps categories in sync automatically without each needing to remember to call
    /// this themselves.
    /// </summary>
    public void SyncAutoCategories(IEnumerable<Game> games)
    {
        var gamesList = games as IList<Game> ?? games.ToList();
        var categories = LoadCategories();
        var nextSortOrder = categories.Count == 0 ? 0 : categories.Max(c => c.SortOrder) + 1;
        var changed = false;

        var consoles = gamesList.Select(g => g.Console).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToHashSet();
        foreach (var console in consoles)
        {
            if (categories.Any(c => c.Kind == CategoryKind.AutoConsole && c.SourceKey == console))
                continue;

            categories.Add(new GameCategory { Name = console, SourceKey = console, Kind = CategoryKind.AutoConsole, SortOrder = nextSortOrder++ });
            changed = true;
        }

        var platforms = gamesList.Select(g => GetPlatformDisplayName(g.Platform)).Distinct().ToHashSet();
        foreach (var platform in platforms)
        {
            if (categories.Any(c => c.Kind == CategoryKind.AutoPlatform && c.SourceKey == platform))
                continue;

            categories.Add(new GameCategory { Name = platform, SourceKey = platform, Kind = CategoryKind.AutoPlatform, SortOrder = nextSortOrder++ });
            changed = true;
        }

        // Auto categories with no current members get pruned rather than kept forever. This used to
        // preserve them indefinitely (so a category emptied by editing/removing games would keep its
        // sort position if repopulated later), but in practice that just accumulated ghost
        // categories - a re-detection landing on a slightly different platform string than a
        // previous one (e.g. "Nintendo" vs "Nintendo Switch" for the same actual console, from two
        // different scans) stranded the old name at zero members permanently instead of the two
        // ever converging. Matched by SourceKey, not Name, so a rename doesn't make a still-live
        // category look orphaned (and get pruned) or a fresh duplicate get created under its old name.
        if (categories.RemoveAll(c =>
                (c.Kind == CategoryKind.AutoConsole && !consoles.Contains(c.SourceKey)) ||
                (c.Kind == CategoryKind.AutoPlatform && !platforms.Contains(c.SourceKey))) > 0)
        {
            changed = true;
        }

        if (changed)
        {
            SaveCategories(categories);
        }
    }

    /// <summary>The fixed display-name mapping for GamePlatform, used as the AutoPlatform join key - mirrors the strings already used in AddGameWindow.xaml's Platform ComboBox items, duplicated here rather than shared since those are static XAML and this is a tiny, unlikely-to-change fixed mapping (same tradeoff Game.PlatformBadgeText already makes with its own separate switch).</summary>
    public static string GetPlatformDisplayName(GamePlatform platform) => platform switch
    {
        GamePlatform.Manual => "PC / Other",
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Gog => "GOG",
        GamePlatform.BattleNet => "Battle.net",
        GamePlatform.Xbox => "Xbox",
        _ => platform.ToString()
    };
}
