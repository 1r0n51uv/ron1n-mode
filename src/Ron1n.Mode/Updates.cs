using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Ron1n.Mode;

internal sealed record Update(string Tag, Uri Asset, string Digest);

internal static class Updates
{
    public const string Repository = "1r0n51uv/ron1n-mode";
    private static readonly HttpClient Http = CreateClient();
    public static bool Installed => File.Exists(Path.Combine(Preferences.ExecutableDirectory, "ConsoleMode.installed"));

    public static async Task<Update?> Find(bool beta)
    {
        var currentText = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0";
        var current = Parse(currentText.Split('+')[0]) ?? (new Version(0, 0), 0, 0);
        using var releases = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=20"));
        var name = Installed ? "ConsoleMode-Setup-x64.exe" : "ConsoleMode-Portable-x64.exe";
        Update? latest = null;
        var latestVersion = current;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (release.GetProperty("draft").GetBoolean() || (release.GetProperty("prerelease").GetBoolean() && !beta && current.Item2 == 2)
                || Parse(tag.TrimStart('v')) is not { } version || version.CompareTo(latestVersion) <= 0) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name || !asset.TryGetProperty("digest", out var digestProperty)
                    || digestProperty.GetString() is not { } digest || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")
                    || !Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri)
                    || uri.Scheme != "https" || uri.Host != "github.com"
                    || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                latest = new Update(tag, uri, digest[7..]);
                latestVersion = version;
            }
        }
        return latest;
    }

    private static (Version, int, int)? Parse(string value)
    {
        var match = Regex.Match(value, @"^(\d+\.\d+\.\d+(?:\.\d+)?)(?:-(alpha|beta)\.(\d+))?$");
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return null;
        if (version.Revision < 0) version = new Version(version.Major, version.Minor, version.Build, 0);
        var level = match.Groups[2].Value switch { "alpha" => 0, "beta" => 1, _ => 2 };
        return (version, level, int.TryParse(match.Groups[3].Value, out var serial) ? serial : 0);
    }

    public static async Task Install(Update update)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Percorso dell’app sconosciuto.");
        var directory = Path.Combine(Installed ? Path.GetTempPath() : Preferences.ExecutableDirectory, ".ron1n-ota-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "update.exe");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            using var response = await Http.GetAsync(update.Asset, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using (var download = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var target = File.Create(file))
                await download.CopyToAsync(target, deadline.Token);
            await using (var completed = File.OpenRead(file))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(completed, deadline.Token));
                if (!string.Equals(update.Digest, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 dell’aggiornamento non valido.");
            }

            if (Installed)
            {
                Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
                return;
            }
            static string Escape(string path) => "'" + path.Replace("'", "''") + "'";
            var old = exe + ".previous";
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                Wait-Process -Id {{Environment.ProcessId}} -Timeout 90 -ErrorAction SilentlyContinue
                $exe = {{Escape(exe)}}
                $backup = {{Escape(old)}}
                Move-Item -LiteralPath $exe -Destination $backup -Force
                try {
                    Move-Item -LiteralPath {{Escape(file)}} -Destination $exe
                    Start-Process -FilePath $exe
                    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
                } catch {
                    Move-Item -LiteralPath $backup -Destination $exe -Force
                    Start-Process -FilePath $exe
                    throw
                }
                """;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RON1N-Mode");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}

public partial class HomeWindow
{
    private bool _checking;

    private async Task CheckUpdate(bool interactive)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var update = await Updates.Find(_preferences.BetaUpdates);
            if (update is null)
            {
                if (interactive) Status.Text = "Nessun aggiornamento disponibile.";
                return;
            }
            if (_session is not null || !IsActive)
            {
                Status.Text = "Aggiornamento disponibile · " + update.Tag;
                return;
            }
            if (MessageBox.Show(this, $"Installare {update.Tag} e riavviare RON1N Mode?", "Aggiornamento",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Status.Text = "Download dell’aggiornamento…";
            _dialog = true;
            await Updates.Install(update);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (interactive) Status.Text = "Aggiornamento non riuscito: " + ex.Message;
        }
        finally { _checking = false; _dialog = false; }
    }
}
