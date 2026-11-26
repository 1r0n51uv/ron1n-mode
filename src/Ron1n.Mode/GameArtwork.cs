using System.Net.Http;
using System.Text.RegularExpressions;

namespace Ron1n.Mode;

internal sealed record ArtworkMatch(string Title, Uri Page, string Platform = "")
{
    public string Label => Platform.Length == 0 ? Title : $"{Title} · {Platform}";
    public string Id => $"launchbox:{Page.Segments[^1].Split('-')[0]}";
    public override string ToString() => Label;
}

internal sealed record GameArtwork(string Description, Uri? Cover, Uri? Background, double CoverAspect, string[] Details);

internal static partial class GameCatalog
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(7) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RON1N-Mode/0.1 (+https://github.com/1r0n51uv/ron1n-mode)");
        return client;
    }

    public static async Task<List<ArtworkMatch>> SearchAsync(string name, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        var normalized = Normalize(name);
        return (await SearchLaunchBox(name, token)).OrderByDescending(match => Normalize(match.Title) == normalized).ToList();
    }

    internal static string Normalize(string input) => string.Concat(input.Normalize(System.Text.NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static Task<GameArtwork> GetAsync(ArtworkMatch match, CancellationToken token = default) =>
        GetLaunchBoxArtwork(match.Page, token);

    public static async Task<string?> DownloadAsync(Uri? uri, CancellationToken cancellationToken)
    {
        if (uri is null || AllowedImage(uri.AbsoluteUri) is null) return null;
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
        try
        {
            await File.WriteAllBytesAsync(path, contents.ToArray(), cancellationToken);
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    internal static bool IsLaunchBoxPage(Uri page) =>
        page.Scheme == "https" && page.Host == LaunchBoxSite.Host
        && page.AbsolutePath.StartsWith(LaunchBoxDetails, StringComparison.Ordinal)
        && Regex.IsMatch(page.AbsolutePath[LaunchBoxDetails.Length..], @"^\d+(?:-[^/]+)?$");

    private static string[] Details(params string[] values) => values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

    private static Uri? AllowedImage(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.Host == "images.launchbox-app.com" ? uri : null;
}
