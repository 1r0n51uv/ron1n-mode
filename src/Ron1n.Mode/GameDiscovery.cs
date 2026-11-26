using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ron1n.Mode;

internal static class GameDiscovery
{
    public static List<Game> Scan(string root)
    {
        var games = new List<Game>();
        Collect(() =>
        {
            foreach (var folder in Directories(root))
                Collect(() =>
                {
                    if (FindExecutable(folder, Path.GetFileName(folder)) is { } exe)
                        games.Add(Entry("folder:" + folder.ToUpperInvariant(), Path.GetFileName(folder), exe));
                });
        });
        Collect(() => ScanSteam(games));
        Collect(() => ScanEpic(games));
        return games.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ScanSteam(List<Game> games)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string root || !File.Exists(Path.Combine(root, "steam.exe")))
            return;

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var config = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(config))
            foreach (Match match in Regex.Matches(File.ReadAllText(config), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));

        foreach (var library in libraries)
            foreach (var manifest in Files(Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
                Collect(() =>
                {
                    var document = File.ReadAllText(manifest);
                    string Read(string name) => Regex.Match(document, $"\"{name}\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    var id = Read("appid");
                    var name = Read("name");
                    var install = Read("installdir");
                    if (!uint.TryParse(id, out _) || name.Length == 0 || install.Length == 0 || name == "Steamworks Common Redistributables") return;
                    var location = Path.Combine(library, "steamapps", "common", install);
                    if (FindExecutable(location, name) is { } exe)
                        games.Add(Entry("steam:" + id, name, exe, $"steam://rungameid/{id}"));
                });
    }

    private static void ScanEpic(List<Game> games)
    {
        using var protocol = Registry.ClassesRoot.OpenSubKey(@"com.epicgames.launcher\shell\open\command");
        if (protocol?.GetValue(null) is not string command || string.IsNullOrWhiteSpace(command)) return;
        var executable = command.StartsWith('"') ? command.Split('"')[1] : command.Split(' ')[0];
        if (!File.Exists(Environment.ExpandEnvironmentVariables(executable))) return;
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        foreach (var file in Files(manifests, "*.item"))
            Collect(() =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var item = document.RootElement;
                string Read(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
                var app = Read("AppName");
                var name = Read("DisplayName");
                var launcher = Read("LaunchExecutable");
                if (app.Length == 0 || name.Length == 0 || launcher.Length == 0 || Read("InstallLocation").Length == 0) return;
                if (item.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return;
                var exe = Path.Combine(Read("InstallLocation"), launcher);
                if (!File.Exists(exe)) return;
                var identity = string.Join(":", Read("CatalogNamespace"), Read("CatalogItemId"), app);
                games.Add(Entry("epic:" + identity, name, exe,
                    $"com.epicgames.launcher://apps/{Uri.EscapeDataString(identity)}?action=launch&silent=true"));
            });
    }

    private static Game Entry(string key, string name, string exe, string uri = "") => new()
    {
        Id = "auto-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24],
        Name = name,
        Path = exe,
        Source = key.StartsWith("steam:") ? "Steam" : key.StartsWith("epic:") ? "Epic" : "Cartella",
        SteamId = key.StartsWith("steam:") && uint.TryParse(key[6..], out var id) ? id : null,
        LaunchUri = uri
    };

    private static string? FindExecutable(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 7,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var hint = Normalize(name);
        return Directory.EnumerateFiles(root, "*.exe", options)
            .Select(path => (Path: path, Name: Normalize(Path.GetFileNameWithoutExtension(path))))
            .Where(file => !Regex.IsMatch(file.Name,
                "^(setup|unins|install|redist|crash|vcredist|dxsetup|easyanticheat|unitycrash|ueprereq)|crashreport"))
            .OrderByDescending(file => file.Name == hint)
            .ThenByDescending(file => file.Name.EndsWith("shipping"))
            .ThenBy(file => file.Path.Length)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.Path).FirstOrDefault();
    }

    private static string Normalize(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string[] Directories(string path) => Directory.Exists(path) ? Directory.GetDirectories(path) : [];
    private static string[] Files(string path, string pattern) => Directory.Exists(path) ? Directory.GetFiles(path, pattern) : [];

    private static void Collect(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }
}
