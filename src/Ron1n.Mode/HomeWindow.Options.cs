using System.Diagnostics;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ron1n.Mode;

public sealed partial class HomeWindow
{
    private static readonly string[] SettingsCategories = ["Libreria", "Schermo e audio", "Comandi", "Avvio", "Aggiornamenti", "Aspetto"];
    private static readonly (string Id, string Label)[] GlassMaterials =
        [("dark", "Scuro"), ("light", "Chiaro"), ("clear", "Trasparente"), ("accent", "Colore del gioco")];
    private readonly List<Border> _categoryIndicators = [];
    private int _settingsCategory = -1;
    private Button? _settingsPickerOrigin;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ConsoleMode";

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (_dialog) return;
        _hoverTimer?.Stop();
        Home.Visibility = Visibility.Collapsed;
        Settings.Visibility = Visibility.Visible;
        if (Categories.Children.Count == 0)
            for (var index = 0; index < SettingsCategories.Length; index++)
            {
                var category = index;
                var indicator = new Border { Width = 4, Height = 28, CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(-13, 0, 0, 0), Background = (Brush)Application.Current.Resources["ConsoleAccentBrush"],
                    Opacity = 0 };
                var label = new TextBlock { Text = SettingsCategories[index], FontSize = 20,
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                var row = new Grid();
                row.Children.Add(indicator);
                row.Children.Add(label);
                var button = new Button { Style = ConsoleStyle("ConsoleCategory"), Content = row };
                AutomationProperties.SetName(button, label.Text);
                button.Click += (_, _) => SelectSettingsCategory(category);
                button.GotFocus += (_, _) => SelectSettingsCategory(category);
                _categoryIndicators.Add(indicator);
                Categories.Children.Add(button);
            }
        SelectSettingsCategory(Math.Max(0, _settingsCategory), refresh: true);
        ((Button)Categories.Children[_settingsCategory]).Focus(FocusState.Programmatic);
    }

    private void CloseSettings(object sender, RoutedEventArgs e)
    {
        Settings.Visibility = Visibility.Collapsed;
        Home.Visibility = Visibility.Visible;
        FocusSelection();
    }

    private void SelectSettingsCategory(int index, bool refresh = false)
    {
        if (_settingsCategory == index && !refresh) return;
        _settingsCategory = index;
        for (var category = 0; category < _categoryIndicators.Count; category++)
        {
            var selected = category == index;
            _categoryIndicators[category].Opacity = selected ? 1 : 0;
            AutomationProperties.SetItemStatus(Categories.Children[category], selected ? "Selezionata" : "");
        }
        RenderSettings();
    }

    private void RenderSettings()
    {
        SettingsRows.Children.Clear();

        switch (_settingsCategory)
        {
            case 0:
                var folderValue = SettingValue(string.IsNullOrEmpty(_preferences.GamesFolder) ? "Non impostata" : _preferences.GamesFolder);
                AddSettingRow("Cartella giochi", folderValue, async () =>
                {
                    var panel = new StackPanel();
                    var folder = Field(panel, "Percorso", _preferences.GamesFolder);
                    if (!await ShowDialog("Cartella giochi", panel)) return;
                    await UpdateFolder(folder.Text.Trim().Trim('"'));
                });
                AddSettingAction("Sfoglia", async () =>
                {
                    var picker = new FolderPicker();
                    picker.FileTypeFilter.Add("*");
                    InitializeWithWindow.Initialize(picker, _handle);
                    var selected = await picker.PickSingleFolderAsync();
                    if (selected is not null) await UpdateFolder(selected.Path);
                });
                async Task UpdateFolder(string path)
                {
                    await SaveFolder(path);
                    folderValue.Text = string.IsNullOrEmpty(path) ? "Non impostata" : path;
                }
                break;
            case 1:
                var displays = FindDisplayAreas().Select((area, index) => new DisplayChoice(area.DisplayId.Value.ToString(),
                    $"Schermo {index + 1} · {area.OuterBounds.Width} × {area.OuterBounds.Height}" + (area.IsPrimary ? " · Principale" : ""), area.IsPrimary)).ToArray();
                var current = displays.FirstOrDefault(display => display.Id == _preferences.Screen)
                    ?? displays.FirstOrDefault(display => display.IsPrimary);
                AddSettingChoice("Schermo della home", displays, current, display =>
                {
                    _preferences.Screen = display.Id;
                    Save();
                    PlaceHome();
                });
                AddSettingAction("Uscita audio di Windows", () =>
                {
                    Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
                    return Task.CompletedTask;
                });
                break;
            case 2:
                var keys = new[] { "Disattivata", "F8", "F9", "F10", "F11", "F12" };
                AddSettingChoice("Torna alla home · Ctrl + Alt", keys, _preferences.KeyboardShortcut, shortcut =>
                {
                    _preferences.KeyboardShortcut = shortcut;
                    SetKeyboardShortcut();
                    Save();
                });
                var options = new[] { new ShortcutChoice("Disattivata", 0),
                    new ShortcutChoice("Visualizza / Crea / − + tasto in alto", Controller.View | Controller.Y),
                    new ShortcutChoice("Menu / Opzioni / + e Visualizza / Crea / −", Controller.Menu | Controller.View) };
                AddSettingChoice("Torna alla home · Controller", options,
                    options.FirstOrDefault(option => option.Mask == _preferences.ControllerShortcut), option =>
                    {
                        _preferences.ControllerShortcut = option.Mask;
                        Save();
                    });
                break;
            case 3:
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    AddToggle("Apri a schermo intero all’accesso", key?.GetValue(RunValue) is string, enabled =>
                    {
                        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                        if (enabled && Environment.ProcessPath is { } exe) run?.SetValue(RunValue, $"\"{exe}\" --home");
                        else run?.DeleteValue(RunValue, false);
                    });
                break;
            case 4:
                AddToggle("Controlla gli aggiornamenti all’avvio", _preferences.CheckUpdates, enabled => { _preferences.CheckUpdates = enabled; Save(); });
                AddToggle("Ricevi anche le beta", _preferences.BetaUpdates, enabled => { _preferences.BetaUpdates = enabled; Save(); });
                AddSettingAction("Controlla aggiornamenti adesso", () => CheckUpdate(true));
                break;
            case 5:
                var index = Math.Max(0, Array.FindIndex(GlassMaterials, option => option.Id == _preferences.CardMaterial));
                var material = SettingValue(GlassMaterials[index].Label);
                AddSettingRow("Materiale Glass", material, () =>
                {
                    index = (index + 1) % GlassMaterials.Length;
                    _preferences.CardMaterial = GlassMaterials[index].Id;
                    ApplyGlassMaterial();
                    Save();
                    material.Text = GlassMaterials[index].Label;
                    return Task.CompletedTask;
                });
                break;
        }
    }

    private sealed record DisplayChoice(string Id, string Label, bool IsPrimary) { public override string ToString() => Label; }
    private sealed record ShortcutChoice(string Label, int Mask) { public override string ToString() => Label; }

    private async Task SaveFolder(string folder)
    {
        _preferences.GamesFolder = folder;
        Save();
        await ReloadGames();
    }

    private void AddToggle(string label, bool enabled, Action<bool> change)
    {
        var value = SettingValue(enabled ? "Attivo" : "Disattivato");
        AddSettingRow(label, value, () =>
        {
            change(!enabled);
            enabled = !enabled;
            value.Text = enabled ? "Attivo" : "Disattivato";
            return Task.CompletedTask;
        });
    }

    private void AddSettingChoice<T>(string title, T[] choices, T? current, Action<T> apply) where T : class
    {
        var labels = choices.Select(choice => choice.ToString() ?? "").ToArray();
        var selected = Array.IndexOf(choices, current);
        var value = SettingValue(selected < 0 ? "Non impostato" : labels[selected]);
        var button = AddSettingRow(title, value);
        button.IsEnabled = choices.Length > 0;
        button.Click += (_, _) => OpenSettingsPicker(button, title, labels, selected, index =>
        {
            apply(choices[index]);
            selected = index;
            value.Text = labels[index];
        });
    }

    private void AddSettingAction(string title, Func<Task> action) =>
        AddSettingRow(title, new FontIcon { Glyph = "\uE76C", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right }, action);

    private Button AddSettingRow(string title, FrameworkElement accessory, Func<Task>? action = null)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = accessory is TextBlock ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(accessory, 1);
        row.Children.Add(SettingLabel(title));
        row.Children.Add(accessory);
        var button = new Button { Style = ConsoleStyle("ConsoleRow"), Content = row };
        AutomationProperties.SetName(button, title);
        if (accessory is TextBlock value)
        {
            AutomationProperties.SetItemStatus(button, value.Text);
            value.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => AutomationProperties.SetItemStatus(button, value.Text));
        }
        if (action is not null) button.Click += async (_, _) => await RunAction(action);
        SettingsRows.Children.Add(button);
        return button;
    }

    private void OpenSettingsPicker(Button origin, string title, string[] options, int selected, Action<int> apply)
    {
        if (_dialog || options.Length == 0) return;
        _dialog = true;
        _settingsPickerOrigin = origin;
        SettingsPickerTitle.Text = title;
        for (var index = 0; index < options.Length; index++)
        {
            var option = index;
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (index == selected)
                row.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 20,
                    Foreground = (Brush)Application.Current.Resources["ConsoleAccentBrush"] });
            var label = SettingLabel(options[index]);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var button = new Button { Style = ConsoleStyle("ConsoleRow"), Content = row };
            AutomationProperties.SetName(button, options[index]);
            if (index == selected) AutomationProperties.SetItemStatus(button, "Selezionata");
            button.Click += async (_, _) =>
            {
                CloseSettingsPicker();
                await RunAction(() => { apply(option); return Task.CompletedTask; });
            };
            SettingsPickerOptions.Children.Add(button);
        }
        Settings.Visibility = Visibility.Collapsed;
        SettingsPicker.Visibility = Visibility.Visible;
        var focused = (Button)SettingsPickerOptions.Children[Math.Max(0, selected)];
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_settingsPickerOrigin != origin) return;
            SettingsPicker.UpdateLayout();
            focused.Focus(FocusState.Keyboard);
        });
    }

    private void CancelSettingsPicker(object sender, RoutedEventArgs e) => CloseSettingsPicker();

    private void CloseSettingsPicker()
    {
        var origin = _settingsPickerOrigin;
        _settingsPickerOrigin = null;
        SettingsPicker.Visibility = Visibility.Collapsed;
        SettingsPickerOptions.Children.Clear();
        Settings.Visibility = Visibility.Visible;
        _dialog = false;
        Settings.UpdateLayout();
        origin?.Focus(FocusState.Keyboard);
    }

    private static TextBlock SettingLabel(string title) => new()
    {
        Text = title, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock SettingValue(string value)
    {
        var label = SettingLabel(value);
        label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        label.Foreground = (Brush)Application.Current.Resources["ConsoleAccentBrush"];
        label.TextAlignment = TextAlignment.Right;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxLines = 2;
        return label;
    }

    private static Style ConsoleStyle(string key) => (Style)Application.Current.Resources[key];

    private async Task RunAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Report(ex.Message); }
    }

    private async Task AddManual()
    {
        if (_dialog) return;
        await RunAction(async () =>
        {
            var file = await PickFile(".exe");
            if (file is null) return;
            var game = new Game { Id = Guid.NewGuid().ToString("N"), Path = file };
            var fields = new StackPanel { Spacing = 10 };
            var name = Field(fields, "Nome", game.Title);
            var arguments = Field(fields, "Argomenti (facoltativi)", "");
            if (!await ShowDialog("Aggiungi gioco", fields)) return;
            game.Name = name.Text.Trim();
            game.Arguments = arguments.Text;
            _preferences.Manual.Add(game);
            Save();
            await ReloadGames(game.Id);
        });
    }

    private async Task<string?> PickFile(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, _handle);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private void OpenGameMenu(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game || _dialog) return;
        var menu = new MenuFlyout();
        Add("Modifica gioco", () => EditGame(game));
        Add("Cerca un altro titolo", () => SearchArtwork(game));
        if (game.Source == "Manuale")
            Add("Rimuovi dalla libreria", async () =>
            {
                if (_session?.Game.Id == game.Id) return;
                _preferences.Manual.Remove(game);
                Save();
                await ReloadGames();
            });
        menu.ShowAt(More);

        void Add(string title, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += async (_, _) => { menu.Hide(); await RunAction(action); };
            menu.Items.Add(item);
        }
    }

    private async Task EditGame(Game game)
    {
        var fields = new StackPanel { Spacing = 10 };
        var name = Field(fields, "Nome", game.Title);
        var arguments = Field(fields, "Argomenti", game.Arguments);
        var path = Field(fields, "Eseguibile", game.Path);
        name.IsEnabled = arguments.IsEnabled = path.IsEnabled = game.Source == "Manuale";
        var watch = Field(fields, "Processo da seguire (facoltativo)", _preferences.Watched.GetValueOrDefault(game.Id, game.WatchProcess));
        if (!await ShowDialog("Modifica gioco", fields)) return;
        if (game.Source == "Manuale")
        {
            var executable = path.Text.Trim();
            if (!File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Report("Scegli un eseguibile esistente.");
                return;
            }
            game.Name = name.Text.Trim();
            game.Path = executable;
            game.Arguments = arguments.Text;
        }
        _preferences.Watched[game.Id] = watch.Text.Trim();
        Save();
        await ReloadGames(game.Id);
    }

    private async Task SearchArtwork(Game game)
    {
        _artwork?.Cancel();
        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        var presentation = _preferences.For(game);
        var query = Field(panel, "Titolo da cercare", CatalogSearchTitle(game, presentation));
        var search = new Button { Content = "Cerca", Style = ConsoleStyle("ConsoleDialogButton") };
        var results = new ListView { MinHeight = 100, MaxHeight = 230 };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(search);
        panel.Children.Add(message);
        panel.Children.Add(results);
        var previewTitle = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        var previewMedia = new Grid { ColumnSpacing = 12 };
        previewMedia.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        previewMedia.ColumnDefinitions.Add(new ColumnDefinition());
        var coverImage = AddPreviewImage(previewMedia, 0, "Card", 124);
        var backgroundImage = AddPreviewImage(previewMedia, 1, "Sfondo", 220);
        var previewPanel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        previewPanel.Children.Add(previewTitle);
        previewPanel.Children.Add(previewMedia);
        panel.Children.Add(previewPanel);
        using var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previewRequest = null;
        ArtworkPreview? preview = null;
        var dialog = CreateDialog("Cerca un altro titolo", panel);
        dialog.IsPrimaryButtonEnabled = false;
        results.SelectionChanged += async (_, _) =>
        {
            previewRequest?.Cancel();
            previewRequest = null;
            dialog.IsPrimaryButtonEnabled = false;
            coverImage.Source = backgroundImage.Source = null;
            preview?.Dispose();
            preview = null;
            previewPanel.Visibility = Visibility.Collapsed;
            if (cancellation.IsCancellationRequested || results.SelectedItem is not ArtworkMatch match) return;

            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            previewRequest = request;
            previewPanel.Visibility = Visibility.Visible;
            previewTitle.Text = "Caricamento anteprima…";
            ArtworkPreview? loaded = null;
            try
            {
                loaded = await PrepareArtworkPreview(match, request.Token);
                request.Token.ThrowIfCancellationRequested();
                coverImage.Source = LoadImage(loaded.Cover, 352);
                backgroundImage.Source = LoadImage(loaded.Background, 768);
                previewTitle.Text = match.Label;
                preview = loaded;
                loaded = null;
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!request.IsCancellationRequested) previewTitle.Text = "Anteprima non disponibile: " + ex.Message;
            }
            finally
            {
                loaded?.Dispose();
                if (ReferenceEquals(previewRequest, request)) previewRequest = null;
            }
        };
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false;
            results.ItemsSource = null;
            message.Text = "Ricerca in corso…";
            try
            {
                var found = await GameCatalog.SearchAsync(query.Text.Trim(), cancellation.Token);
                results.ItemsSource = found;
                results.SelectedIndex = found.Count == 1 ? 0 : -1;
                message.Text = found.Count > 0 ? "Seleziona un risultato per vedere l’anteprima." : "Nessun titolo trovato su LaunchBox.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { message.Text = "Ricerca non disponibile: " + ex.Message; }
            finally { search.IsEnabled = true; }
        };
        query.KeyDown += (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter || !search.IsEnabled) return;
            WindowsKeys.ActivateFocused(search);
            args.Handled = true;
        };
        var apply = await ShowDialog(dialog);
        cancellation.Cancel();
        coverImage.Source = backgroundImage.Source = null;
        using (preview)
            if (apply && preview is not null)
                ApplyArtwork(game, preview.Match, preview.Artwork, preview.Cover, preview.Background, replace: true);
    }

    private static Image AddPreviewImage(Grid grid, int column, string label, double width)
    {
        var image = new Image { Stretch = Stretch.Uniform };
        AutomationProperties.SetName(image, label);
        var media = new Grid { Width = width, Height = 124, CornerRadius = new CornerRadius(14),
            Background = (Brush)Application.Current.Resources["GlassFill"] };
        media.Children.Add(new TextBlock { Text = "Non disponibile", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Opacity = .67 });
        media.Children.Add(image);
        var stack = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(new TextBlock { Text = label });
        stack.Children.Add(media);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
        return image;
    }

    private static async Task<ArtworkPreview> PrepareArtworkPreview(ArtworkMatch match, CancellationToken token)
    {
        var content = await GameCatalog.GetAsync(match, token);
        string? cover = null;
        string? background = null;
        try
        {
            await Task.WhenAll(LoadCover(), LoadBackground());
            token.ThrowIfCancellationRequested();
            return new ArtworkPreview(match, content, cover, background);
        }
        catch
        {
            if (cover is not null) File.Delete(cover);
            if (background is not null) File.Delete(background);
            throw;
        }

        async Task LoadCover() => cover = await Download(content.Cover);
        async Task LoadBackground() => background = await Download(content.Background);

        async Task<string?> Download(Uri? uri)
        {
            try { return await GameCatalog.DownloadAsync(uri, token); }
            catch (HttpRequestException) { return null; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        }
    }

    private sealed record ArtworkPreview(ArtworkMatch Match, GameArtwork Artwork, string? Cover, string? Background) : IDisposable
    {
        public void Dispose()
        {
            if (Cover is not null) File.Delete(Cover);
            if (Background is not null) File.Delete(Background);
        }
    }

    private void ScheduleArtwork(Game? game)
    {
        _artwork?.Cancel();
        if (game is null || _session is not null || _dialog) return;
        var art = _preferences.For(game);
        var complete = art.IsLaunchBox && art.HasCatalogMedia && File.Exists(art.Card) && File.Exists(art.Background);
        if (complete || !_fetched.Add(game.Id)) return;
        var lookup = new CancellationTokenSource();
        _artwork = lookup;
        _ = AutoArtwork(game, lookup);
    }

    private async Task AutoArtwork(Game game, CancellationTokenSource lookup)
    {
        try
        {
            await Task.Delay(650, lookup.Token);
            var art = _preferences.For(game);
            ArtworkMatch? match = null;
            var title = CatalogSearchTitle(game, art);
            if (art.IsLaunchBox && Uri.TryCreate(art.CatalogUrl, UriKind.Absolute, out var page) && GameCatalog.IsLaunchBoxPage(page))
                match = new ArtworkMatch(title, page);
            else
            {
                var found = await GameCatalog.SearchAsync(title, lookup.Token);
                var normalizedTitle = GameCatalog.Normalize(title);
                var matches = found.Where(result => GameCatalog.Normalize(result.Title) == normalizedTitle).ToList();
                var platform = art.CatalogDetails.FirstOrDefault();
                if (platform == "PC" || game.SteamId.HasValue) platform = "Windows";
                var preferred = matches.Where(result => string.Equals(result.Platform, platform, StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToList();
                if (preferred.Count == 1) match = preferred[0];
                else if (matches.Count == 1) match = matches[0];
            }
            if (match is not null) await LoadArtwork(game, match, replace: !art.HasCatalogMedia || art.CatalogId != match.Id, lookup.Token);
        }
        catch (OperationCanceledException) { _fetched.Remove(game.Id); }
        catch (Exception ex) { Debug.WriteLine(ex); }
        finally
        {
            if (ReferenceEquals(_artwork, lookup)) _artwork = null;
            lookup.Dispose();
        }
    }

    private static string CatalogSearchTitle(Game game, Presentation art)
    {
        const string legacyPrefix = "wikipedia:";
        if (!string.IsNullOrWhiteSpace(art.CatalogTitle)) return art.CatalogTitle;
        if (art.CatalogId.StartsWith(legacyPrefix, StringComparison.Ordinal))
            return Uri.UnescapeDataString(art.CatalogId[legacyPrefix.Length..]).Replace('_', ' ');
        return game.Title;
    }

    private async Task LoadArtwork(Game game, ArtworkMatch match, bool replace, CancellationToken token)
    {
        var content = await GameCatalog.GetAsync(match, token);
        var art = _preferences.For(game);
        string? cover = null;
        string? backdrop = null;
        try
        {
            if (replace || !File.Exists(art.Card)) cover = await GameCatalog.DownloadAsync(content.Cover, token);
            if (replace || !File.Exists(art.Background)) backdrop = await GameCatalog.DownloadAsync(content.Background, token);
            token.ThrowIfCancellationRequested();
            ApplyArtwork(game, match, content, cover, backdrop, replace);
        }
        finally
        {
            if (cover is not null) File.Delete(cover);
            if (backdrop is not null) File.Delete(backdrop);
        }
    }

    private void ApplyArtwork(Game game, ArtworkMatch match, GameArtwork content, string? cover, string? background, bool replace)
    {
        var art = _preferences.For(game);
        var cardPath = cover is not null ? Preferences.StoreImage(game.Id, "card", cover) : replace ? "" : art.Card;
        var backgroundPath = background is not null ? Preferences.StoreImage(game.Id, "background", background) : replace ? "" : art.Background;
        art.Card = cardPath;
        art.Background = backgroundPath;
        art.CoverAspect = cardPath.Length > 0 ? content.CoverAspect : 0;
        art.HasCatalogMedia = true;
        art.Description = content.Description;
        art.CatalogId = match.Id;
        art.CatalogTitle = match.Title;
        art.CatalogUrl = match.Page.AbsoluteUri;
        art.CatalogDetails = content.Details;
        Save();
        if (SelectedGame?.Id == game.Id) UpdateHero(false, false);
    }

    private static TextBox Field(Panel panel, string title, string value)
    {
        var field = new TextBox { Header = title, Text = value, Style = ConsoleStyle("ConsoleTextField") };
        panel.Children.Add(field);
        return field;
    }

    private ContentDialog CreateDialog(string title, object content, string confirm = "Applica") => new()
    {
        XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm,
        CloseButtonText = "Annulla", DefaultButton = ContentDialogButton.Primary,
        Background = (Brush)Application.Current.Resources["GlassFill"],
        BorderBrush = (Brush)Application.Current.Resources["GlassEdge"],
        CornerRadius = new CornerRadius(24),
        PrimaryButtonStyle = ConsoleStyle("ConsoleDialogButton"),
        CloseButtonStyle = ConsoleStyle("ConsoleDialogButton")
    };

    private Task<bool> ShowDialog(string title, FrameworkElement content) => ShowDialog(CreateDialog(title, content));

    private async Task<bool> ShowDialog(ContentDialog dialog)
    {
        if (_dialog) return false;
        _dialog = true;
        _activeDialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { _dialog = false; _activeDialog = null; }
    }

    private ContentDialog? _activeDialog;
}
