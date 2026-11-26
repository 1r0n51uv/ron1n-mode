using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ron1n.Mode;

internal sealed record ArtworkMatch(uint? SteamId, string Title, string? Article = null, string Description = "", Uri? Thumbnail = null)
{
    public string Label => $"{Title} · {(SteamId.HasValue ? "Steam" : "Wikipedia")}";
    public string Id => SteamId.HasValue ? $"steam:{SteamId}" : $"wikipedia:{Article}";
    public override string ToString() => Label;
}

internal sealed record GameArtwork(string Description, Uri? Cover, Uri? Background);

internal static class GameCatalog
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RON1N-Mode/0.1 (+https://github.com/1r0n51uv/ron1n-mode)");
        return client;
    }

    public static async Task<List<ArtworkMatch>> SearchAsync(string name, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        var steam = TrySearch(() => SearchSteam(name, token), token);
        var wiki = TrySearch(() => SearchWikipedia(name, token), token);
        await Task.WhenAll(steam, wiki);
        if (steam.Result is null && wiki.Result is null)
            throw new HttpRequestException("I cataloghi sono temporaneamente irraggiungibili.");
        return (steam.Result ?? []).Concat(wiki.Result ?? [])
            .OrderByDescending(match => Normalize(match.Title) == Normalize(name))
            .ToList();
    }

    private static async Task<List<ArtworkMatch>?> TrySearch(Func<Task<List<ArtworkMatch>>> search, CancellationToken token)
    {
        try { return await search(); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static string Normalize(string input) => string.Concat(input.Normalize(System.Text.NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static async Task<List<ArtworkMatch>> SearchWikipedia(string name, CancellationToken token)
    {
        var url = $"https://en.wikipedia.org/w/rest.php/v1/search/page?q={Uri.EscapeDataString(name)}&limit=10";
        using var document = JsonDocument.Parse(await Client.GetStringAsync(url, token));
        var results = new List<ArtworkMatch>();
        foreach (var page in document.RootElement.GetProperty("pages").EnumerateArray())
        {
            var description = Read(page, "description");
            if (!description.Contains("video game", StringComparison.OrdinalIgnoreCase)
                || description.Contains("video game series", StringComparison.OrdinalIgnoreCase)
                || description.Contains("video game character", StringComparison.OrdinalIgnoreCase)
                || description.Contains("video game awards show", StringComparison.OrdinalIgnoreCase)) continue;
            var thumbnail = page.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.Object
                ? Read(thumb, "url") : "";
            var image = AllowedImage(thumbnail.StartsWith("//", StringComparison.Ordinal) ? "https:" + thumbnail : thumbnail);
            results.Add(new ArtworkMatch(null, Read(page, "title"), Read(page, "key"), description, image));
        }
        return results;
    }

    private static async Task<List<ArtworkMatch>> SearchSteam(string name, CancellationToken cancellationToken)
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
        if (match.SteamId is null)
        {
            var wikiCover = match.Thumbnail;
            var summary = match.Description;
            Uri? publisherBackground = null;
            if (!string.IsNullOrWhiteSpace(match.Article))
            {
                try
                {
                    var article = Uri.EscapeDataString(match.Article);
                    var html = await Client.GetStringAsync($"https://en.wikipedia.org/w/rest.php/v1/page/{article}/html", cancellationToken);
                    wikiCover ??= FindInfoboxCover(html);
                    summary = ReadLeadSentence(html) ?? summary;
                    publisherBackground = await FindPublisherBackgroundAsync(html, cancellationToken);
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }
            return new GameArtwork(summary, wikiCover, publisherBackground);
        }
        const string language = "italian";
        var id = match.SteamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var payload = JsonDocument.Parse(await Client.GetStringAsync(
            $"https://store.steampowered.com/api/appdetails?appids={id}&l={language}", cancellationToken));
        if (!payload.RootElement.TryGetProperty(id, out var response)
            || !response.TryGetProperty("data", out var data)) return null;
        var description = WebUtility.HtmlDecode(Regex.Replace(Read(data, "short_description"), "<[^>]+>", ""));
        var cover = AllowedImage(Read(data, "capsule_imagev5")) ?? AllowedImage(Read(data, "header_image"));
        var background = AllowedImage(Read(data, "background_raw"));
        if (background is null && data.TryGetProperty("screenshots", out var shots) && shots.GetArrayLength() > 0)
            background = AllowedImage(Read(shots[0], "path_full"));
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
            "image/webp" => ".webp",
            _ => null
        };
        if (!response.IsSuccessStatusCode || extension is null || response.RequestMessage?.RequestUri is not { } final
            || AllowedImage(final.AbsoluteUri) is null) return null;
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

    private static async Task<Uri?> FindPublisherBackgroundAsync(string article, CancellationToken token)
    {
        var timeout = TimeSpan.FromSeconds(1);
        var candidates = Regex.Matches(article, """href="(https://[^"<>]+)""",
                RegexOptions.IgnoreCase, timeout)
            .Select(match => Uri.TryCreate(WebUtility.HtmlDecode(match.Groups[1].Value), UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>().Where(IsPublisherPage).Distinct()
            .OrderByDescending(uri => uri.Host.EndsWith(".pokemon.com", StringComparison.OrdinalIgnoreCase))
            .Take(3);
        foreach (var candidate in candidates)
        {
            try
            {
                using var response = await Client.GetAsync(candidate, token);
                if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is not { } final || !IsPublisherPage(final)) continue;
                var html = await response.Content.ReadAsStringAsync(token);
                foreach (Match meta in Regex.Matches(html, @"<meta\b[^>]+>", RegexOptions.IgnoreCase, timeout))
                {
                    if (!meta.Value.Contains("og:image", StringComparison.OrdinalIgnoreCase)) continue;
                    var image = Regex.Match(meta.Value, """\bcontent\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase, timeout);
                    if (!image.Success) continue;
                    var value = WebUtility.HtmlDecode(image.Groups[1].Value);
                    // Some publisher pages generate metadata with their local development origin.
                    if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.IsLoopback)
                        value = absolute.PathAndQuery;
                    if (Uri.TryCreate(final, value, out var resolved) && AllowedImage(resolved.AbsoluteUri) is { } result)
                        return result;
                }
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
        return null;
    }

    private static bool IsPublisherPage(Uri uri) => uri.Scheme == "https" &&
        (uri.Host.EndsWith(".pokemon.com", StringComparison.OrdinalIgnoreCase)
         && uri.Host is not ("www.pokemon.com" or "support.pokemon.com")
         || uri.Host.Equals("www.nintendo.com", StringComparison.OrdinalIgnoreCase)
         && (uri.AbsolutePath.Contains("/store/products/", StringComparison.OrdinalIgnoreCase)
             || uri.AbsolutePath.Contains("/Games/", StringComparison.OrdinalIgnoreCase)));

    private static string? ReadLeadSentence(string html)
    {
        var timeout = TimeSpan.FromSeconds(1);
        foreach (Match paragraph in Regex.Matches(html, @"<p\b[^>]*>(.*?)</p>", RegexOptions.Singleline, timeout))
        {
            var withoutNotes = Regex.Replace(paragraph.Groups[1].Value, @"<sup\b[^>]*>.*?</sup>", "", RegexOptions.Singleline, timeout);
            var plain = WebUtility.HtmlDecode(Regex.Replace(withoutNotes, "<[^>]+>", "", RegexOptions.None, timeout));
            plain = Regex.Replace(plain, @"\s+", " ", RegexOptions.None, timeout).Trim();
            if (plain.Length < 60) continue;
            var sentence = plain.IndexOf(". ", StringComparison.Ordinal);
            if (sentence > 0 && sentence < 250) return plain[..(sentence + 1)];
            if (plain.Length <= 250) return plain;
            var breakAt = plain.LastIndexOf(' ', 250);
            return plain[..(breakAt > 0 ? breakAt : 250)].TrimEnd() + "…";
        }
        return null;
    }

    private static Uri? FindInfoboxCover(string html)
    {
        var table = Regex.Match(html, """<table\b[^>]*\bclass="[^"]*\binfobox\b[^"]*"[^>]*>(.*?)</table>""",
            RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!table.Success) return null;
        foreach (Match image in Regex.Matches(table.Groups[1].Value, """<img\b[^>]*\bsrc="([^"]+)""",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var url = WebUtility.HtmlDecode(image.Groups[1].Value);
            if (url.Contains(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (AllowedImage(url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url) is { } cover)
                return cover;
        }
        return null;
    }

    private static string Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static Uri? AllowedImage(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("steamcdn-a.akamaihd.net", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("thumb.wikimedia.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".pokemon.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".nintendo.net", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".nintendo.com", StringComparison.OrdinalIgnoreCase))
            ? uri : null;
}
