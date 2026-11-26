using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ron1n.Mode;

internal sealed class Game
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string LaunchUri { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WatchProcess { get; set; } = "";
    public string Source { get; set; } = "Manuale";
    public uint? SteamId { get; set; }
    [JsonIgnore] public string Title => string.IsNullOrWhiteSpace(Name)
        ? System.IO.Path.GetFileNameWithoutExtension(Path) : Name;
}

internal sealed class Presentation
{
    public string Description { get; set; } = "";
    public string Card { get; set; } = "";
    public string Background { get; set; } = "";
    public uint? ArtworkId { get; set; }
    public string CatalogId { get; set; } = "";
}

internal sealed class Preferences
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static readonly string ExecutableDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    internal static readonly string Data = File.Exists(Path.Combine(ExecutableDirectory, "ConsoleMode.installed"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleMode")
        : Path.Combine(ExecutableDirectory, "ConsoleMode_Data");
    private static readonly string FilePath = Path.Combine(Data, "ron1n.json");

    public string GamesFolder { get; set; } = "";
    public string LastPlayed { get; set; } = "";
    public string Screen { get; set; } = "";
    public bool CheckUpdates { get; set; } = true;
    public bool BetaUpdates { get; set; }
    public string KeyboardShortcut { get; set; } = "F11";
    public int ControllerShortcut { get; set; } = 0x8020;
    public List<Game> Manual { get; set; } = [];
    public Dictionary<string, string> Watched { get; set; } = [];
    public Dictionary<string, Presentation> Art { get; set; } = [];

    public static Preferences Load()
    {
        if (File.Exists(FilePath))
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath), Json) ?? new();
        var preferences = new Preferences();
        var legacy = Path.Combine(Data, "config.json");
        if (!File.Exists(legacy)) return preferences;
        var old = JsonSerializer.Deserialize<Legacy>(File.ReadAllText(legacy), Json);
        if (old is null) return preferences;
        preferences.Manual = old.CustomApps.Where(game => !string.IsNullOrWhiteSpace(game.Path)).ToList();
        if (preferences.Manual.Count == 0 && !string.IsNullOrWhiteSpace(old.CustomAppPath))
            preferences.Manual.Add(new Game { Id = "1", Path = old.CustomAppPath, Arguments = old.CustomAppArguments,
                WatchProcess = old.CustomAppWatchProcess });
        preferences.LastPlayed = old.CustomAppId;
        preferences.ControllerShortcut = old.MenuShortcut;
        preferences.CheckUpdates = old.CheckUpdates;
        preferences.BetaUpdates = old.BetaUpdates;
        foreach (var game in preferences.Manual)
            if (old.LaunchImages.TryGetValue("customApp:" + game.Id, out var art)) preferences.Art[game.Id] = art;
        return preferences;
    }

    public void Save()
    {
        Directory.CreateDirectory(Data);
        var stage = FilePath + ".tmp";
        File.WriteAllText(stage, JsonSerializer.Serialize(this, Json));
        File.Move(stage, FilePath, overwrite: true);
    }

    public Presentation For(Game game)
    {
        if (!Art.TryGetValue(game.Id, out var art)) Art[game.Id] = art = new();
        return art;
    }

    public static string StoreImage(string id, string kind, string source)
    {
        var folder = Path.Combine(Data, "ron1n-art");
        Directory.CreateDirectory(folder);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        var destination = Path.Combine(folder, $"{key}-{kind}-{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}");
        File.Copy(source, destination + ".tmp", overwrite: true);
        File.Move(destination + ".tmp", destination, overwrite: true);
        return destination;
    }

    private sealed class Legacy
    {
        public List<Game> CustomApps { get; set; } = [];
        public Dictionary<string, Presentation> LaunchImages { get; set; } = [];
        public string CustomAppId { get; set; } = "";
        public string CustomAppPath { get; set; } = "";
        public string CustomAppArguments { get; set; } = "";
        public string CustomAppWatchProcess { get; set; } = "";
        public int MenuShortcut { get; set; }
        public bool CheckUpdates { get; set; } = true;
        public bool BetaUpdates { get; set; }
    }
}
