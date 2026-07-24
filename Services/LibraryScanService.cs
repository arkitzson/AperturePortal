using System.IO;
using System.Text.RegularExpressions;

namespace ApertureOS.Services;

/// <summary>One scan result: the exe to launch and the name it should be added under.</summary>
public record ScannedGameCandidate(string ExePath, string DisplayName);

/// <summary>
/// Pure filesystem scanning for the "auto add" Settings flows - finds candidate games and leaves
/// filtering/confirmation to the caller (ScanResultsWindow's checklist). Takes the existing-paths
/// exclusion set as a parameter rather than loading the library itself, same as
/// CategoryService.SyncAutoCategories takes games in rather than reaching for GameLibraryService.
/// </summary>
public class LibraryScanService
{
    private const int MaxInstalledScanDepth = 3;
    private const int MaxInstalledScanResults = 500;

    // Substrings (case-insensitive) that mark a file as almost certainly not a game itself - a
    // cheap first-pass filter, not the primary defense (see ScanForInstalledExecutables for why
    // that's the largest-file heuristic instead). Doesn't need to be exhaustive; the review
    // checklist is the real safety net.
    private static readonly string[] NonGameFilenameMarkers =
    [
        "unins", "setup", "redist", "crashpad", "dxsetup", "dotnet", "installer", "vc_redist",
        "crs-handler", "crs-uploader", "quicksfv", "easyanticheat", "battleye"
    ];

    /// <summary>
    /// Scans a ROM folder, which can either hold loose ROM files directly or - just as common for a
    /// large library - one subfolder per game (potentially hundreds of them), each holding the ROM
    /// alongside save data, box art, or metadata with no predictable naming/extension. Loose
    /// top-level files are each their own candidate as before; each subfolder becomes exactly one
    /// candidate named after the folder, picking the largest file found under it (recursively, in
    /// case it's nested a level or two deeper still) as the ROM itself - the same "largest file
    /// wins" reasoning ScanForInstalledExecutables already uses, since ROMs have no fixed extension
    /// to filter on the way a PC install's *.exe does, but are still virtually always much bigger
    /// than anything else sitting next to them.
    /// </summary>
    public List<ScannedGameCandidate> ScanEmulatorFolder(string romFolder, ISet<string> existingExePaths)
    {
        if (string.IsNullOrWhiteSpace(romFolder) || !Directory.Exists(romFolder))
            return [];

        var results = new List<ScannedGameCandidate>();

        foreach (var path in SafeEnumerateFiles(romFolder, "*"))
        {
            if (existingExePaths.Contains(path))
                continue;

            results.Add(new ScannedGameCandidate(path, CleanCandidateName(path)));
            if (results.Count >= MaxInstalledScanResults)
                return results;
        }

        foreach (var subFolder in SafeEnumerateDirectories(romFolder))
        {
            // Same "already claimed" skip as ScanForInstalledExecutables - see its comment for why
            // this matters (otherwise a re-scan keeps finding "the next largest un-added file" in an
            // already-added game's folder and re-adding it as a duplicate).
            var alreadyClaimed = existingExePaths.Any(path =>
                path.StartsWith(subFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (alreadyClaimed)
                continue;

            var fileCandidates = new List<string>();
            CollectFiles(subFolder, "*", depth: 0, fileCandidates);

            var best = fileCandidates.OrderByDescending(SafeFileLength).FirstOrDefault();
            if (best is not null)
            {
                // Unlike ScanForInstalledExecutables' subfolder case, ROM folder names routinely
                // carry the same No-Intro/scene-dump tags as the files inside them (a per-game folder
                // is often just the ROM's own filename with the extension chopped off) - so this runs
                // it through the same cleanup as loose files instead of using the raw folder name.
                results.Add(new ScannedGameCandidate(best, CleanFolderDisplayName(subFolder)));
                if (results.Count >= MaxInstalledScanResults)
                    return results;
            }
        }

        return results;
    }

    private static string CleanFolderDisplayName(string folder)
    {
        var cleaned = CleanCandidateName(folder);
        return string.IsNullOrWhiteSpace(cleaned) ? Path.GetFileName(folder) : cleaned;
    }

    /// <summary>
    /// Scans for installed-but-unsynced PC games. A configured folder is expected to hold either
    /// loose game exes directly, or - far more commonly - one subfolder per game (exactly how Steam/
    /// itch.io/manual installs already organize things, e.g. "C:\Games\Stellar Blade\SB.exe"). Each
    /// subfolder becomes exactly one candidate, named after the folder rather than whatever the exe
    /// itself is called: a real game's own exe is one file among several bundled alongside it
    /// (anti-cheat installers, crash reporters, redistributables, checksum utilities) with no
    /// predictable naming convention, so picking by filename alone routinely surfaces the wrong
    /// thing. Picking the largest exe found under that folder is far more reliable than trying to
    /// denylist every publisher's bundled tool by name - real game binaries are near-universally
    /// dramatically bigger than the utilities shipped alongside them.
    /// </summary>
    public List<ScannedGameCandidate> ScanForInstalledExecutables(IEnumerable<string> folders, ISet<string> existingExePaths)
    {
        var results = new List<ScannedGameCandidate>();

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            foreach (var path in SafeEnumerateFiles(folder, "*.exe"))
            {
                if (existingExePaths.Contains(path) || IsLikelyNonGameExecutable(path))
                    continue;

                results.Add(new ScannedGameCandidate(path, CleanCandidateName(path)));
                if (results.Count >= MaxInstalledScanResults)
                    return results;
            }

            foreach (var subFolder in SafeEnumerateDirectories(folder))
            {
                // If any existing game's exe already lives under this subfolder, it's already been
                // added - skip it entirely rather than picking a "next largest" exe among what's
                // left and treating that as a separate new game. Without this, a folder scanned
                // more than once keeps finding smaller bundled auxiliary exes (Unreal Engine ships
                // several multi-MB ones - CrashReportClient.exe, UnrealCEFSubProcess.exe, etc.) and
                // re-adding them as duplicate entries for the same game every time.
                var alreadyClaimed = existingExePaths.Any(path =>
                    path.StartsWith(subFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (alreadyClaimed)
                    continue;

                var exeCandidates = new List<string>();
                CollectFiles(subFolder, "*.exe", depth: 0, exeCandidates);

                var best = exeCandidates
                    .Where(path => !IsLikelyNonGameExecutable(path))
                    .OrderByDescending(SafeFileLength)
                    .FirstOrDefault();

                if (best is not null)
                {
                    results.Add(new ScannedGameCandidate(best, Path.GetFileName(subFolder)));
                    if (results.Count >= MaxInstalledScanResults)
                        return results;
                }
            }
        }

        return results;
    }

    /// <summary>Turns a raw filename into a readable game name - used for loose top-level files and
    /// emulator ROMs, where there's no enclosing per-game folder name to prefer instead. Strips
    /// bracketed/parenthesized tags (No-Intro/scene ROM dump convention: "[TitleID][v0]", "(USA)",
    /// "(Rev 1)") since those are pure display noise - this is the only cleanup scanned names get
    /// now (no external metadata API to apply a canonical title on top afterward).</summary>
    public static string CleanCandidateName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        name = Regex.Replace(name, @"\[[^\]]*\]", " ");
        name = Regex.Replace(name, @"\([^)]*\)", " ");
        name = name.Replace('_', ' ').Replace('.', ' ');
        while (name.Contains("  "))
        {
            name = name.Replace("  ", " ");
        }

        return name.Trim();
    }

    private static bool IsLikelyNonGameExecutable(string path)
    {
        var fileName = Path.GetFileName(path);
        return NonGameFilenameMarkers.Any(marker => fileName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectFiles(string folder, string pattern, int depth, List<string> results)
    {
        if (depth > MaxInstalledScanDepth)
            return;

        results.AddRange(SafeEnumerateFiles(folder, pattern));

        foreach (var subFolder in SafeEnumerateDirectories(folder))
        {
            CollectFiles(subFolder, pattern, depth + 1, results);
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long SafeFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
