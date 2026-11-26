using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ron1n.Mode;

public sealed partial class HomeWindow
{
    private static readonly string[] SettingsCategories = ["Libreria", "Schermo e audio", "Comandi", "Avvio", "Aggiornamenti"];
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ConsoleMode";

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (_dialog) return;
        Home.Visibility = Visibility.Collapsed;
        Settings.Visibility = Visibility.Visible;
        if (Categories.Items.Count == 0)
            foreach (var title in SettingsCategories)
                Categories.Items.Add(new ListViewItem { Content = title, MinHeight = 50 });
        if (Categories.SelectedIndex < 0) Categories.SelectedIndex = 0;
        else RenderSettings();
        if (Categories.SelectedItem is Control item) item.Focus(FocusState.Programmatic);
    }

    private void CloseSettings(object sender, RoutedEventArgs e)
    {
        Settings.Visibility = Visibility.Collapsed;
        Home.Visibility = Visibility.Visible;
        FocusSelection();
    }

    private void SettingsCategoryChanged(object sender, SelectionChangedEventArgs e) => RenderSettings();

    private void RenderSettings()
    {
        if (Categories.SelectedIndex < 0) return;
        SettingsRows.Children.Clear();
        SettingsRows.Children.Add(new TextBlock { Text = SettingsCategories[Categories.SelectedIndex], FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        switch (Categories.SelectedIndex)
        {
            case 0:
                var folder = Field(SettingsRows, "Cartella giochi", _preferences.GamesFolder);
                AddSettingAction("Sfoglia", async () =>
                {
                    var picker = new FolderPicker();
                    picker.FileTypeFilter.Add("*");
                    InitializeWithWindow.Initialize(picker, _handle);
                    var selected = await picker.PickSingleFolderAsync();
                    if (selected is null) return;
                    folder.Text = selected.Path;
                    await SaveFolder(selected.Path);
                });
                AddSettingAction("Salva cartella e aggiorna libreria", () => SaveFolder(folder.Text.Trim().Trim('"')));
                break;
            case 1:
                var displays = DisplayArea.FindAll().Select((area, index) => new DisplayChoice(area.DisplayId.Value.ToString(),
                    $"Schermo {index + 1} · {area.OuterBounds.Width} × {area.OuterBounds.Height}" + (area.IsPrimary ? " · Principale" : ""))).ToArray();
                var current = displays.FirstOrDefault(display => display.Id == _preferences.Screen)
                    ?? displays.FirstOrDefault(display => display.Id == DisplayArea.Primary.DisplayId.Value.ToString());
                var screen = new ComboBox { Header = "Schermo della home", ItemsSource = displays,
                    SelectedItem = current, MinWidth = 300 };
                screen.SelectionChanged += (_, _) =>
                {
                    if (screen.SelectedItem is not DisplayChoice display) return;
                    _preferences.Screen = display.Id;
                    Save();
                    PlaceHome();
                };
                SettingsRows.Children.Add(screen);
                AddSettingAction("Uscita audio di Windows", () =>
                {
                    Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
                    return Task.CompletedTask;
                });
                break;
            case 2:
                var keys = new[] { "Disattivata", "F8", "F9", "F10", "F11", "F12" };
                var keyboard = new ComboBox { Header = "Torna alla home · Ctrl + Alt", ItemsSource = keys,
                    SelectedItem = _preferences.KeyboardShortcut, MinWidth = 300 };
                keyboard.SelectionChanged += (_, _) =>
                {
                    _preferences.KeyboardShortcut = keyboard.SelectedItem as string ?? "Disattivata";
                    SetKeyboardShortcut();
                    Save();
                };
                SettingsRows.Children.Add(keyboard);
                var options = new[] { new ShortcutChoice("Disattivata", 0),
                    new ShortcutChoice("Visualizza / Crea / − + tasto in alto", Controller.View | Controller.Y),
                    new ShortcutChoice("Menu / Opzioni / + e Visualizza / Crea / −", Controller.Menu | Controller.View) };
                var controller = new ComboBox { Header = "Torna alla home · Controller", ItemsSource = options,
                     MinWidth = 300,
                    SelectedItem = options.FirstOrDefault(option => option.Mask == _preferences.ControllerShortcut) };
                controller.SelectionChanged += (_, _) =>
                {
                    if (controller.SelectedItem is not ShortcutChoice option) return;
                    _preferences.ControllerShortcut = option.Mask;
                    Save();
                };
                SettingsRows.Children.Add(controller);
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
        }
    }

    private sealed record DisplayChoice(string Id, string Label) { public override string ToString() => Label; }
    private sealed record ShortcutChoice(string Label, int Mask) { public override string ToString() => Label; }

    private async Task SaveFolder(string folder)
    {
        _preferences.GamesFolder = folder;
        Save();
        await ReloadGames();
    }

    private void AddToggle(string label, bool enabled, Action<bool> change)
    {
        var control = new ToggleSwitch { Header = label, IsOn = enabled };
        control.Toggled += (_, _) =>
        {
            try { change(control.IsOn); }
            catch (Exception ex) { Report(ex.Message); }
        };
        SettingsRows.Children.Add(control);
    }

    private void AddSettingAction(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(16, 9, 16, 9) };
        button.Click += async (_, _) => await RunAction(action);
        SettingsRows.Children.Add(button);
    }

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
        Add("Scegli immagine card", () => PickImage(game, false));
        Add("Scegli immagine sfondo", () => PickImage(game, true));
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

    private async Task PickImage(Game game, bool background)
    {
        _artwork?.Cancel();
        var path = await PickFile(".jpg", ".jpeg", ".png", ".webp");
        if (path is null) return;
        var art = _preferences.For(game);
        var image = Preferences.StoreImage(game.Id, background ? "background" : "card", path);
        if (background) art.Background = image;
        else art.Card = image;
        Save();
        if (SelectedGame?.Id == game.Id) UpdateHero(false, false);
    }

    private async Task SearchArtwork(Game game)
    {
        _artwork?.Cancel();
        var panel = new StackPanel { Spacing = 10, MinWidth = 345 };
        var query = Field(panel, "Titolo da cercare", game.Title);
        var search = new Button { Content = "Cerca" };
        var results = new ListView { MinHeight = 100, MaxHeight = 230 };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(search);
        panel.Children.Add(message);
        panel.Children.Add(results);
        using var cancellation = new CancellationTokenSource();
        var dialog = CreateDialog("Cerca un altro titolo", panel);
        dialog.IsPrimaryButtonEnabled = false;
        results.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = results.SelectedItem is ArtworkMatch;
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
                message.Text = found.Count == 0 ? "Nessun titolo trovato." : "Seleziona il risultato da abbinare.";
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
        if (apply && results.SelectedItem is ArtworkMatch match)
            await LoadArtwork(game, match, true, CancellationToken.None);
    }

    private void ScheduleArtwork(Game? game)
    {
        _artwork?.Cancel();
        if (game is null || _session is not null || _dialog) return;
        var art = _preferences.For(game);
        if (art.ArtworkId is not null || art.CatalogId.Length > 0 || !_fetched.Add(game.Id)) return;
        var lookup = new CancellationTokenSource();
        _artwork = lookup;
        _ = AutoArtwork(game, lookup);
    }

    private async Task AutoArtwork(Game game, CancellationTokenSource lookup)
    {
        try
        {
            await Task.Delay(650, lookup.Token);
            var match = game.SteamId is { } id ? new ArtworkMatch(id, game.Title) : null;
            if (match is null)
            {
                var found = await GameCatalog.SearchAsync(game.Title, lookup.Token);
                var matches = found.Where(result => GameCatalog.Normalize(result.Title) == GameCatalog.Normalize(game.Title)).ToList();
                var steamMatches = matches.Where(result => result.SteamId.HasValue).Take(2).ToList();
                if (steamMatches.Count == 1) match = steamMatches[0];
                else if (matches.Count == 1) match = matches[0];
            }
            if (match is not null) await LoadArtwork(game, match, false, lookup.Token);
        }
        catch (OperationCanceledException) { _fetched.Remove(game.Id); }
        catch (Exception ex) { Debug.WriteLine(ex); }
        finally
        {
            if (ReferenceEquals(_artwork, lookup)) _artwork = null;
            lookup.Dispose();
        }
    }

    private async Task LoadArtwork(Game game, ArtworkMatch match, bool replace, CancellationToken token)
    {
        var content = await GameCatalog.GetAsync(match, token);
        if (content is null)
        {
            if (replace) Report("Contenuti non disponibili per il titolo selezionato.");
            return;
        }
        var art = _preferences.For(game);
        string? cover = null;
        string? backdrop = null;
        try
        {
            if (replace || !File.Exists(art.Card)) cover = await GameCatalog.DownloadAsync(content.Cover, token);
            if (replace || !File.Exists(art.Background)) backdrop = await GameCatalog.DownloadAsync(content.Background, token);
            token.ThrowIfCancellationRequested();
            if (cover is not null && (replace || !File.Exists(art.Card))) art.Card = Preferences.StoreImage(game.Id, "card", cover);
            if (backdrop is not null && (replace || !File.Exists(art.Background))) art.Background = Preferences.StoreImage(game.Id, "background", backdrop);
            art.Description = content.Description;
            art.ArtworkId = match.SteamId;
            art.CatalogId = match.Id;
            Save();
            if (SelectedGame?.Id == game.Id) UpdateHero(false, false);
        }
        finally
        {
            if (cover is not null) File.Delete(cover);
            if (backdrop is not null) File.Delete(backdrop);
        }
    }

    private static TextBox Field(Panel panel, string title, string value)
    {
        var field = new TextBox { Header = title, Text = value, MinWidth = 310 };
        panel.Children.Add(field);
        return field;
    }

    private ContentDialog CreateDialog(string title, object content, string confirm = "Applica") => new()
    {
        XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm,
        CloseButtonText = "Annulla", DefaultButton = ContentDialogButton.Primary
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
