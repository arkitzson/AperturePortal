using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ApertureOS.Services;

public sealed record SteamAppInfo(string AppId, string Name);

/// <summary>
/// Local install/download state for one Steam app, read straight from its appmanifest_&lt;id&gt;.acf -
/// the same file Steam's own client updates live while downloading, so this reflects real progress
/// without any API. <see cref="IsInstalled"/> is only true once nothing is left pending
/// (StateFlags 4, confirmed against a real fully-installed manifest); while a download or update is
/// in progress it's false and <see cref="PercentComplete"/> reflects how far along it is.
/// </summary>
public sealed record SteamInstallProgress(bool IsInstalled, long BytesDownloaded, long BytesToDownload)
{
    public double? PercentComplete => BytesToDownload > 0 ? 100.0 * BytesDownloaded / BytesToDownload : null;
}

/// <summary>Thrown when a stored embedded-browser Steam session cookie has expired or been revoked.</summary>
public sealed class SteamSessionExpiredException : Exception
{
    public SteamSessionExpiredException() : base("Your Steam session has expired.")
    {
    }
}

/// <summary>Discovers Steam games either from local install records or the account's full owned library.</summary>
public sealed class SteamLibraryService
{
    /// <summary>
    /// Scans Steam's own local install records (steamapps/appmanifest_*.acf across every
    /// library folder) - no Steam Web API key or network access needed, but only finds games
    /// actually installed on this PC.
    /// </summary>
    public List<SteamAppInfo> GetInstalledGames()
    {
        var steamPath = FindSteamInstallPath();
        if (steamPath is null)
            return [];

        var games = new List<SteamAppInfo>();
        foreach (var libraryFolder in GetLibraryFolders(steamPath))
        {
            var steamAppsDir = Path.Combine(libraryFolder, "steamapps");
            if (!Directory.Exists(steamAppsDir))
                continue;

            foreach (var manifestFile in Directory.GetFiles(steamAppsDir, "appmanifest_*.acf"))
            {
                if (ParseAppManifest(manifestFile) is { } app)
                    games.Add(app);
            }
        }

        return games;
    }

    /// <summary>
    /// Every game the account owns, installed or not, authenticated with a manually-created Steam
    /// Web API key (steamcommunity.com/dev/apikey). Requires the profile's "game details" to be
    /// set to Public. Prefer <see cref="GetOwnedGamesViaSessionAsync"/> when a browser login is
    /// available - this is only the fallback for when it isn't.
    /// </summary>
    public async Task<List<SteamAppInfo>> GetOwnedGamesAsync(string steamId64, string webApiKey, CancellationToken ct = default)
    {
        var url = BuildOwnedGamesUrl(steamId64, "key", webApiKey);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Steam Web API request failed ({(int)response.StatusCode} {response.StatusCode}). Double-check your Steam Web API key.");
        }

        return ParseOwnedGames(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Same result as <see cref="GetOwnedGamesAsync"/> but authenticated with a real embedded-browser
    /// Steam login (see SteamWebLoginWindow) instead of a manually-created Web API key - this is
    /// what Playnite's default "web login" library import does. The community site's own React
    /// frontend calls this exact same official endpoint using the JWT embedded in the
    /// "steamLoginSecure" cookie as a bearer access_token instead of a developer key, so this just
    /// replicates that rather than scraping any community page directly (Valve retired the old
    /// unauthenticated XML/HTML scraping approaches entirely).
    /// </summary>
    public async Task<List<SteamAppInfo>> GetOwnedGamesViaSessionAsync(
        string steamId64, string steamLoginSecureCookie, CancellationToken ct = default)
    {
        var accessToken = ExtractAccessToken(steamLoginSecureCookie);
        var url = BuildOwnedGamesUrl(steamId64, "access_token", accessToken);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            throw new SteamSessionExpiredException();

        return ParseOwnedGames(await response.Content.ReadAsStringAsync(ct));
    }

    private static string BuildOwnedGamesUrl(string steamId64, string authParamName, string authParamValue) =>
        "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
        $"?{authParamName}={Uri.EscapeDataString(authParamValue)}&steamid={Uri.EscapeDataString(steamId64)}" +
        "&include_appinfo=1&include_played_free_games=1&format=json";

    private static List<SteamAppInfo> ParseOwnedGames(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // A profile whose "game details" aren't Public (or an otherwise unqueryable account) comes
        // back as an empty "response": {} rather than an explicit error, so this is worth
        // distinguishing for the user from a genuinely empty library.
        if (!doc.RootElement.TryGetProperty("response", out var responseElement) ||
            !responseElement.TryGetProperty("games", out var gamesElement))
        {
            throw new InvalidOperationException("No games found. Your Steam profile's \"game details\" privacy must be set to Public.");
        }

        return gamesElement.EnumerateArray()
            .Select(game => new SteamAppInfo(
                game.GetProperty("appid").GetInt32().ToString(),
                game.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Unknown" : "Unknown"))
            .ToList();
    }

    /// <summary>
    /// The "steamLoginSecure" cookie's value is "{steamid}\|\|{JWT}" - still percent-encoded as the
    /// browser received it verbatim in Set-Cookie, since cookie values are echoed back byte-for-byte
    /// rather than auto-decoded. The JWT itself is what the community site passes around internally
    /// as a bearer access_token.
    /// </summary>
    private static string ExtractAccessToken(string steamLoginSecureCookie)
    {
        var decoded = Uri.UnescapeDataString(steamLoginSecureCookie);
        var separatorIndex = decoded.IndexOf("||", StringComparison.Ordinal);
        return separatorIndex >= 0 ? decoded[(separatorIndex + 2)..] : decoded;
    }

    /// <summary>Pulls the app ID back out of a "steam://rungameid/{appid}" ExePath, or null for a non-Steam game.</summary>
    public static string? TryGetSteamAppId(string exePath)
    {
        var match = Regex.Match(exePath, @"^steam://rungameid/(\d+)$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Looks up the given app's local appmanifest across every Steam library folder. Null means no
    /// manifest exists anywhere - the game has never been installed and nothing is downloading.
    /// </summary>
    public SteamInstallProgress? GetInstallProgress(string appId)
    {
        var steamPath = FindSteamInstallPath();
        if (steamPath is null)
            return null;

        foreach (var libraryFolder in GetLibraryFolders(steamPath))
        {
            var manifestPath = Path.Combine(libraryFolder, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(manifestPath);
            }
            catch (IOException)
            {
                // Steam has the file open for writing at this exact instant; try again next poll
                // rather than reporting a false "not installed" for a mid-write manifest.
                return null;
            }

            var stateFlags = ParseAcfLongField(content, "StateFlags") ?? 0;
            var bytesToDownload = ParseAcfLongField(content, "BytesToDownload") ?? 0;
            var bytesDownloaded = ParseAcfLongField(content, "BytesDownloaded") ?? 0;

            return new SteamInstallProgress(stateFlags == 4, bytesDownloaded, bytesToDownload);
        }

        return null;
    }

    private static long? ParseAcfLongField(string content, string fieldName)
    {
        var match = Regex.Match(content, $"\"{fieldName}\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
        return match.Success ? long.Parse(match.Groups[1].Value) : null;
    }

    private static string? FindSteamInstallPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    private static List<string> GetLibraryFolders(string steamPath)
    {
        // The registry's SteamPath uses forward slashes while libraryfolders.vdf uses escaped
        // backslashes, so the base Steam library (which is always also listed in the VDF) needs
        // normalizing before the duplicate check, or it gets scanned twice.
        var folders = new List<string>();
        void AddFolder(string path)
        {
            var normalized = Path.GetFullPath(path.Replace('/', '\\'));
            if (!folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                folders.Add(normalized);
        }

        AddFolder(steamPath);

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            var content = File.ReadAllText(vdfPath);
            foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
            {
                AddFolder(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
        }

        return folders;
    }

    private static SteamAppInfo? ParseAppManifest(string manifestPath)
    {
        var content = File.ReadAllText(manifestPath);
        var appIdMatch = Regex.Match(content, "\"appid\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
        var nameMatch = Regex.Match(content, "\"name\"\\s*\"([^\"]+)\"");

        return appIdMatch.Success && nameMatch.Success
            ? new SteamAppInfo(appIdMatch.Groups[1].Value, nameMatch.Groups[1].Value)
            : null;
    }
}
