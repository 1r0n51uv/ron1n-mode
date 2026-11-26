using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Ron1n.Mode;

internal static partial class GameCatalog
{
    private static readonly Uri LaunchBoxSite = new("https://gamesdb.launchbox-app.com");
    private const string LaunchBoxDetails = "/games/details/";
    private static readonly TimeSpan LaunchBoxMatchTimeout = TimeSpan.FromSeconds(1);

    private static async Task<List<ArtworkMatch>> SearchLaunchBox(string name, CancellationToken token)
    {
        var html = await Client.GetStringAsync(new Uri(LaunchBoxSite, "/games/results/" + Uri.EscapeDataString(name)), token);
        var results = new List<ArtworkMatch>();
        var links = Regex.Matches(html, """<a\b[^>]*href="(/games/details/\d+(?:-[^"?#/]+)?)"[^>]*>(.*?)</a>""",
            RegexOptions.Singleline, LaunchBoxMatchTimeout);
        foreach (Match link in links)
        {
            var body = link.Groups[2].Value;
            var label = Regex.Match(body, """<h3\b[^>]*>(.*?)</h3>\s*<p\b[^>]*>(.*?)</p>""",
                RegexOptions.Singleline, LaunchBoxMatchTimeout);
            if (!label.Success) continue;
            var title = LaunchBoxText(label.Groups[1].Value);
            var platform = LaunchBoxText(label.Groups[2].Value);
            if (title.Length > 0 && Uri.TryCreate(LaunchBoxSite, link.Groups[1].Value, out var page))
                results.Add(new ArtworkMatch(title, page, platform));
        }
        return results.DistinctBy(result => result.Id).Take(15).ToList();
    }

    private static async Task<GameArtwork> GetLaunchBoxArtwork(Uri page, CancellationToken token)
    {
        var gallery = new Uri(LaunchBoxSite, "/games/images/" + page.Segments[^1]);
        var detailsRequest = Client.GetStringAsync(page, token);
        var galleryRequest = Client.GetStringAsync(gallery, token);
        await Task.WhenAll(detailsRequest, galleryRequest);
        var details = await detailsRequest;
        var images = Regex.Matches(await galleryRequest,
            """<a\b[^>]*href="(https://images\.launchbox-app\.com/[^"]+)"[^>]*?data-title="([^"]+)"[^>]*?data-footer="(\d+) x (\d+)""",
            RegexOptions.IgnoreCase, LaunchBoxMatchTimeout);
        var media = new List<LaunchBoxImage>();
        foreach (Match image in images)
        {
            var url = AllowedImage(WebUtility.HtmlDecode(image.Groups[1].Value));
            if (url is null || !int.TryParse(image.Groups[3].Value, out var width)
                || !int.TryParse(image.Groups[4].Value, out var height) || width <= 0 || height <= 0) continue;
            media.Add(new LaunchBoxImage(url, WebUtility.HtmlDecode(image.Groups[2].Value), width, height));
        }

        var cover = media.Where(image => image.HasType("Box - Front"))
            .OrderByDescending(image => image.Label.Contains("(Europe)", StringComparison.OrdinalIgnoreCase))
            .ThenBy(image => Math.Abs(Math.Log((double)image.Width / image.Height)))
            .FirstOrDefault();
        var background = media.Where(image => image.Width >= image.Height * 1.4
                && (image.HasType("Fanart - Background") || image.HasType("Screenshot - Gameplay")))
            .OrderByDescending(image => image.HasType("Fanart - Background"))
            .ThenByDescending(image => image.Width).FirstOrDefault();
        cover ??= background;
        var description = Regex.Match(details, """<meta\s+name="description"\s+content="([^"]*)["]""",
            RegexOptions.IgnoreCase, LaunchBoxMatchTimeout).Groups[1].Value;
        var time = Regex.Match(details, """<time\b[^>]*\bdatetime="([^"]+)["]""",
            RegexOptions.IgnoreCase, LaunchBoxMatchTimeout).Groups[1].Value;
        var release = DateTime.TryParse(time, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : time;
        var developer = LaunchBoxDetail(details, "Developers");
        var metadata = Details(LaunchBoxDetail(details, "Platform"), developer.Length > 0 ? developer : LaunchBoxDetail(details, "Publishers"),
            TranslateGenres(LaunchBoxDetail(details, "Genre")), LaunchBoxDetail(details, "ESRB"), release);
        return new GameArtwork(LaunchBoxText(description), cover?.Url, background?.Url, cover?.Aspect ?? 0, metadata);
    }

    private static string TranslateGenres(string genres) => string.Join(", ", genres.Split(", ", StringSplitOptions.RemoveEmptyEntries)
        .Select(genre => genre switch
        {
            "Action" => "Azione",
            "Adventure" => "Avventura",
            "Fighting" => "Picchiaduro",
            "Puzzle" => "Rompicapo",
            "Racing" => "Corse",
            "Role-Playing" => "Gioco di ruolo",
            "Shooter" => "Sparatutto",
            "Simulation" => "Simulazione",
            "Sports" => "Sport",
            "Strategy" => "Strategia",
            _ => genre
        }));

    private static string LaunchBoxDetail(string html, string label)
    {
        var value = Regex.Match(html, $@"<dt\b[^>]*>\s*{label}\s*</dt>\s*<dd\b[^>]*>(.*?)</dd>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, LaunchBoxMatchTimeout).Groups[1].Value;
        var links = Regex.Matches(value, @"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline, LaunchBoxMatchTimeout);
        return links.Count == 0 ? LaunchBoxText(value)
            : string.Join(", ", links.Take(2).Select(link => LaunchBoxText(link.Groups[1].Value)));
    }

    private static string LaunchBoxText(string text)
    {
        var plain = Regex.Replace(text, "<!--.*?-->|<[^>]+>", "", RegexOptions.Singleline, LaunchBoxMatchTimeout);
        return Regex.Replace(WebUtility.HtmlDecode(plain), @"\s+", " ", RegexOptions.None, LaunchBoxMatchTimeout).Trim();
    }

    private sealed record LaunchBoxImage(Uri Url, string Label, int Width, int Height)
    {
        public double Aspect => (double)Width / Height;
        public bool HasType(string type) => Label.Contains($" - {type} Image", StringComparison.OrdinalIgnoreCase);
    }
}
