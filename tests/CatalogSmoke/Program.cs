using Ron1n.Mode;

foreach (var query in new[] { "Pokemon Pokopia", "Pokémon Pokopia" })
{
    var results = await GameCatalog.SearchAsync(query);
    var game = results.FirstOrDefault()
        ?? throw new InvalidOperationException($"Nessun risultato per {query}.");
    if (game.Title != "Pokémon Pokopia" || game.SteamId.HasValue)
        throw new InvalidOperationException($"Primo risultato inatteso per {query}: {game.Label}.");
    var artwork = await GameCatalog.GetAsync(game);
    if (artwork?.Cover is null || string.IsNullOrWhiteSpace(artwork.Description))
        throw new InvalidOperationException($"Scheda incompleta per {query}.");
    var image = await GameCatalog.DownloadAsync(artwork.Cover, CancellationToken.None);
    if (image is null) throw new InvalidOperationException($"Copertina non scaricabile per {query}.");
    File.Delete(image);
    if (artwork.Background is null || artwork.Background == artwork.Cover)
        throw new InvalidOperationException($"Sfondo ufficiale non trovato per {query}.");
    var background = await GameCatalog.DownloadAsync(artwork.Background, CancellationToken.None);
    if (background is null) throw new InvalidOperationException($"Sfondo non scaricabile per {query}.");
    File.Delete(background);
    Console.WriteLine($"{query}: titolo, descrizione, copertina e sfondo verificati.");
}
