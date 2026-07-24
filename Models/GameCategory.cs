using System.Text.Json.Serialization;

namespace ApertureOS.Models;

/// <summary>
/// What a category is derived from. Custom categories are freeform and user-managed; the two Auto
/// kinds are derived from a Game field (Game.Console for AutoConsole, CategoryService's platform
/// display-name mapping for AutoPlatform) - see GameCategory.SourceKey for how that join actually
/// works without pinning down the user-facing Name. Auto categories still can't be deleted (the
/// next game added/synced under that console/platform would just recreate one), but can be renamed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CategoryKind
{
    Custom,
    AutoConsole,
    AutoPlatform
}

public class GameCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public CategoryKind Kind { get; set; }

    /// <summary>
    /// Auto categories only: the actual Game.Console/platform-display-name string this category is
    /// derived from, set once at creation and never touched by a rename - Name is purely the
    /// user-facing label from that point on. Keeping these separate (rather than Name doing double
    /// duty as both label and join key, as it originally did) is what makes renaming an auto
    /// category safe: CategoryService.SyncAutoCategories matches/creates/prunes by SourceKey, so a
    /// rename can't make it think the category vanished (recreating a fresh one under the old name)
    /// or that it's an orphan with zero real members (pruning the very category you just renamed).
    /// Empty for Custom categories, which have no such join to begin with.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Explicit sort position, not array order - lets categories.json stay self-describing/debuggable rather than relying on incidental list order, same reasoning as everywhere else this codebase stores an enum by name instead of by index.</summary>
    public int SortOrder { get; set; }
}
