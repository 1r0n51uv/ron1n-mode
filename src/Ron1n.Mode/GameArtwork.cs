using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ron1n.Mode;

internal sealed record ArtworkMatch(uint SteamId, string Title)
{
    public override string ToString() => Title;
}

internal sealed record GameArtwork(string Description, Uri? Cover, Uri? Background);

internal static class SteamStoreArtwork
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<List<ArtworkMatch>> SearchAsync(string name, CancellationToken cancellationToken = default)
    {
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(name)}&l=english&cc=US";
        using var data = JsonDocument.Parse(await Client.GetStringAsync(url, cancellationToken));
        if (!data.RootElement.TryGetProperty("items", out var items)) return [];
        var results = new List<ArtworkMatch>();
        foreach (var item in items.EnumerateArray())
            if (item.TryGetProperty("type", out var type) && type.GetString() == "app"
                && item.TryGetProperty("id", out var id) && id.TryGetUInt32(out var steamId)
                && item.TryGetProperty("name", out var title) && title.GetString() is { } displayName)
                results.Add(new ArtworkMatch(steamId, displayName));
        return results;
    }

    public static async Task<GameArtwork?> GetAsync(ArtworkMatch match, CancellationToken cancellationToken = default)
    {
        const string language = "italian";
        var id = match.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var payload = JsonDocument.Parse(await Client.GetStringAsync(
            $"https://store.steampowered.com/api/appdetails?appids={id}&l={language}", cancellationToken));
        if (!payload.RootElement.TryGetProperty(id, out var response)
            || !response.TryGetProperty("data", out var data)) return null;
        string Read(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
        var description = WebUtility.HtmlDecode(Regex.Replace(Read(data, "short_description"), "<[^>]+>", ""));
        var cover = SteamImage(Read(data, "capsule_imagev5")) ?? SteamImage(Read(data, "header_image"));
        var background = SteamImage(Read(data, "background_raw"));
        if (background is null && data.TryGetProperty("screenshots", out var shots) && shots.GetArrayLength() > 0)
            background = SteamImage(Read(shots[0], "path_full"));
        return new GameArtwork(description, cover, background);
    }

    public static async Task<string?> DownloadAsync(Uri? uri, CancellationToken cancellationToken)
    {
        if (uri is null) return null;
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => null
        };
        if (!response.IsSuccessStatusCode || extension is null || response.RequestMessage?.RequestUri is not { } final
            || SteamImage(final.AbsoluteUri) is null) return null;
        const int maximumBytes = 10 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes) return null;
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var contents = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (contents.Length + count > maximumBytes) return null;
            contents.Write(buffer, 0, count);
        }
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(path, contents.ToArray(), cancellationToken);
        return path;
    }

    private static Uri? SteamImage(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("steamcdn-a.akamaihd.net", StringComparison.OrdinalIgnoreCase))
            ? uri : null;
}
