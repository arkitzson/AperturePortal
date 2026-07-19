using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ApertureOS.Services;

public enum SteamGridImageKind
{
    Cover,
    Header
}

public sealed class SteamGridDbGame
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class SteamGridDbImage
{
    public int Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string ThumbUrl { get; init; } = string.Empty;
}

/// <summary>Thin client for the parts of the SteamGridDB v2 API this app uses to fetch cover/header art.</summary>
public sealed class SteamGridDbService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    // Separate, unauthenticated client for downloading the actual image bytes: those URLs
    // point at SteamGridDB's CDN host (cdn2.steamgriddb.com), not the API host, and the CDN
    // rejects requests carrying our API bearer token with a 401 - so it must not inherit
    // _http's Authorization header.
    private readonly HttpClient _downloadHttp = new();

    public SteamGridDbService(string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<List<SteamGridDbGame>> SearchGamesAsync(string term, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"search/autocomplete/{Uri.EscapeDataString(term)}", ct);
        var payload = JsonSerializer.Deserialize<ApiResponse<List<GameDto>>>(json, JsonOptions);
        return payload?.Data?.Select(d => new SteamGridDbGame { Id = d.Id, Name = d.Name }).ToList() ?? [];
    }

    /// <summary>Looks up SteamGridDB's own game id from a Steam AppID, e.g. to auto-fetch art for a Steam-synced game. Null if SteamGridDB has no entry for it.</summary>
    public async Task<int?> GetGameIdBySteamAppIdAsync(string steamAppId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"games/steam/{Uri.EscapeDataString(steamAppId)}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("SteamGridDB rejected the API key. Check it in Settings.");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var payload = JsonSerializer.Deserialize<ApiResponse<GameDto>>(json, JsonOptions);
        return payload?.Data?.Id;
    }

    public Task<List<SteamGridDbImage>> GetGridsAsync(int gameId, CancellationToken ct = default) =>
        GetImagesAsync($"grids/game/{gameId}?dimensions=600x900,342x482,660x930", ct);

    public Task<List<SteamGridDbImage>> GetHeroesAsync(int gameId, CancellationToken ct = default) =>
        GetImagesAsync($"heroes/game/{gameId}", ct);

    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        using var response = await _downloadHttp.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<List<SteamGridDbImage>> GetImagesAsync(string path, CancellationToken ct)
    {
        var json = await GetJsonAsync(path, ct);
        var payload = JsonSerializer.Deserialize<ApiResponse<List<ImageDto>>>(json, JsonOptions);
        return payload?.Data?.Select(d => new SteamGridDbImage { Id = d.Id, Url = d.Url, ThumbUrl = d.Thumb }).ToList() ?? [];
    }

    private async Task<string> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);

        // SteamGridDB returns 401 for a missing/invalid key and 404 when a search/lookup
        // simply has no matches - surface the former distinctly since it needs a different fix.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("SteamGridDB rejected the API key. Check it in Settings.");

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public void Dispose()
    {
        _http.Dispose();
        _downloadHttp.Dispose();
    }

    private sealed class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
    }

    private sealed class GameDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ImageDto
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Thumb { get; set; } = string.Empty;
    }
}
