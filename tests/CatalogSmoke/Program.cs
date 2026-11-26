using Ron1n.Mode;

foreach (var (query, title, platform) in new[]
{
    ("Pokemon Pokopia", "Pokémon Pokopia", "Nintendo Switch 2"),
    ("Pokémon Pokopia", "Pokémon Pokopia", "Nintendo Switch 2"),
    ("Portal 2", "Portal 2", "Windows"),
    ("Astro Bot", "Astro Bot", "Sony Playstation 5"),
    ("Halo 5: Guardians", "Halo 5: Guardians", "Microsoft Xbox One")
})
{
    var results = await GameCatalog.SearchAsync(query);
    var match = results.FirstOrDefault(result => result.Title == title && result.Platform == platform)
        ?? throw new InvalidOperationException($"Risultato LaunchBox mancante per {query} su {platform}.");
    var artwork = await GameCatalog.GetAsync(match);
    if (artwork.Cover is null || artwork.Background is null || artwork.CoverAspect <= 0
        || !GameCatalog.IsLaunchBoxPage(match.Page) || !artwork.Details.Contains(platform) || artwork.Description.Length == 0)
        throw new InvalidOperationException($"Scheda LaunchBox incompleta per {title}.");
    foreach (var media in new[] { artwork.Cover, artwork.Background }.Distinct())
    {
        var image = await GameCatalog.DownloadAsync(media, CancellationToken.None)
            ?? throw new InvalidOperationException($"Immagine LaunchBox non scaricabile per {title}.");
        File.Delete(image);
    }
    Console.WriteLine($"{query}: metadati, copertina e sfondo LaunchBox verificati per {platform}.");
}
