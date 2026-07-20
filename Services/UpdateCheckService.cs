using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ApertureOS.Services;

public sealed record UpdateInfo(string Version, string ReleaseUrl);

/// <summary>
/// Checks GitHub Releases for a version newer than the one currently running. There's no
/// auto-download/auto-install - this only ever surfaces a link to the release page, since the
/// first shipped build had no update signal at all and silently replacing an installer under
/// the same version number left early downloaders with no way to know a fix existed.
/// </summary>
public static class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/arkitzson/AperturePortal/releases/latest";

    /// <summary>
    /// Returns info about a newer release if one exists, or null if already up to date or the
    /// check couldn't complete for any reason (offline, GitHub unreachable, rate-limited, a
    /// malformed response) - none of which are worth ever interrupting the user over.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // The GitHub REST API rejects requests with no User-Agent header.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ApertureOS-UpdateCheck");

            var json = await client.GetStringAsync(LatestReleaseApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp) || !root.TryGetProperty("html_url", out var urlProp))
                return null;

            var tagName = tagProp.GetString();
            var releaseUrl = urlProp.GetString();
            if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(releaseUrl))
                return null;

            if (!Version.TryParse(tagName.TrimStart('v', 'V'), out var latestVersion))
                return null;

            // The SDK appends "+{git commit sha}" to InformationalVersion by default for any
            // build made inside a git repo (no SourceLink package needed to trigger it) - strip
            // it back to the plain "1.0.1" before parsing, or Version.TryParse always fails.
            var currentVersionText = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0];
            if (!Version.TryParse(currentVersionText, out var currentVersion))
                return null;

            return latestVersion > currentVersion
                ? new UpdateInfo(latestVersion.ToString(), releaseUrl)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
