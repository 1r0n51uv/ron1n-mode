using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace Ron1n.Mode;

public partial class HomeWindow
{
    private async Task AddManual()
    {
        var file = new OpenFileDialog { Filter = "Applicazioni (*.exe)|*.exe", CheckFileExists = true };
        if (file.ShowDialog(this) != true) return;
        var game = new Game { Id = Guid.NewGuid().ToString("N"), Path = file.FileName };
        var fields = new StackPanel();
        var name = Field(fields, "Nome", game.Title);
        var arguments = Field(fields, "Argomenti (facoltativi)", "");
        if (!ShowDialog("Aggiungi gioco", fields)) return;
        game.Name = name.Text.Trim();
        game.Arguments = arguments.Text;
        _preferences.Manual.Add(game);
        Save();
        await ReloadGames();
        Select(_games.FindIndex(entry => entry.Id == game.Id), true);
        FocusSelection();
    }

    private void OpenGameMenu(object sender, RoutedEventArgs e)
    {
        if (Current is not { } game || _dialog) return;
        var menu = new ContextMenu { PlacementTarget = More };
        Add("Modifica gioco", async () => await EditGame(game));
        Add("Cerca un altro titolo", async () => await SearchArtwork(game));
        Add("Scegli immagine card", () => { PickImage(game, false); return Task.CompletedTask; });
        Add("Scegli immagine sfondo", () => { PickImage(game, true); return Task.CompletedTask; });
        if (game.Source == "Manuale")
            Add("Rimuovi dalla libreria", async () =>
            {
                if (_session?.Game.Id == game.Id) return;
                _preferences.Manual.Remove(game);
                Save();
                await ReloadGames();
            });
        menu.IsOpen = true;

        void Add(string label, Func<Task> action)
        {
            var item = new MenuItem { Header = label };
            item.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { Status.Text = ex.Message; }
            };
            menu.Items.Add(item);
        }
    }

    private async Task EditGame(Game game)
    {
        var fields = new StackPanel();
        var name = Field(fields, "Nome", game.Title);
        name.IsEnabled = game.Source == "Manuale";
        var arguments = Field(fields, "Argomenti", game.Arguments);
        arguments.IsEnabled = game.LaunchUri.Length == 0;
        var path = Field(fields, "Eseguibile", game.Path);
        path.IsEnabled = game.Source == "Manuale";
        var watch = Field(fields, "Processo da seguire (facoltativo)", _preferences.Watched.GetValueOrDefault(game.Id, game.WatchProcess));
        if (!ShowDialog("Modifica gioco", fields)) return;
        if (game.Source == "Manuale")
        {
            if (!File.Exists(path.Text) || !path.Text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Status.Text = "Scegli un eseguibile esistente.";
                return;
            }
            game.Name = name.Text.Trim();
            game.Path = path.Text.Trim();
            game.Arguments = arguments.Text;
        }
        _preferences.Watched[game.Id] = watch.Text.Trim();
        Save();
        await ReloadGames();
    }

    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel();
        var folder = Field(panel, "Cartella giochi", _preferences.GamesFolder);
        var browse = new Button { Content = "Scegli cartella" };
        panel.Children.Add(browse);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Scegli la cartella giochi" };
            if (dialog.ShowDialog(Window.GetWindow(panel)) == true) folder.Text = dialog.FolderName;
        };
        var screens = Screen.AllScreens.Select(screen => screen.DeviceName).ToArray();
        var display = new ComboBox { ItemsSource = screens, Margin = new Thickness(0, 4, 0, 10) };
        display.SelectedItem = screens.Contains(_preferences.Screen) ? _preferences.Screen : Screen.PrimaryScreen?.DeviceName;
        panel.Children.Add(new TextBlock { Text = "Schermo della home" });
        panel.Children.Add(display);
        var audio = new Button { Content = "Uscita audio di Windows" };
        audio.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        panel.Children.Add(audio);
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var startup = new CheckBox { Content = "Apri a schermo intero all’accesso", IsChecked = run?.GetValue("ConsoleMode") is string };
        panel.Children.Add(startup);
        var keys = new[] { "Disattivata", "F8", "F9", "F10", "F11", "F12" };
        panel.Children.Add(new TextBlock { Text = "Torna alla home · Ctrl + Alt" });
        var keyboard = new ComboBox { ItemsSource = keys, SelectedItem = _preferences.KeyboardShortcut };
        panel.Children.Add(keyboard);
        var shortcuts = new[] { (Name: "Disattivata", Mask: 0), (Name: "Visualizza / Crea / − + tasto in alto", Mask: Controller.View | Controller.Y),
            (Name: "Menu / Opzioni / + e Visualizza / Crea / −", Mask: Controller.Menu | Controller.View) };
        panel.Children.Add(new TextBlock { Text = "Torna alla home · controller" });
        var controller = new ComboBox { ItemsSource = shortcuts.Select(item => item.Name), SelectedIndex = Array.FindIndex(shortcuts, item => item.Mask == _preferences.ControllerShortcut) };
        panel.Children.Add(controller);
        var updates = new CheckBox { Content = "Controlla gli aggiornamenti all’avvio", IsChecked = _preferences.CheckUpdates };
        var beta = new CheckBox { Content = "Ricevi anche le beta", IsChecked = _preferences.BetaUpdates };
        panel.Children.Add(updates);
        panel.Children.Add(beta);
        var check = new Button { Content = "Controlla aggiornamenti adesso" };
        var checkOnClose = false;
        check.Click += (_, _) => { checkOnClose = true; Window.GetWindow(panel)?.Close(); };
        panel.Children.Add(check);
        if (!ShowDialog("Impostazioni", panel))
        {
            if (checkOnClose) await CheckUpdate(true);
            return;
        }
        _preferences.GamesFolder = folder.Text.Trim().Trim('"');
        _preferences.Screen = display.SelectedItem as string ?? "";
        _preferences.KeyboardShortcut = keyboard.SelectedItem as string ?? "Disattivata";
        if (controller.SelectedIndex >= 0) _preferences.ControllerShortcut = shortcuts[controller.SelectedIndex].Mask;
        _preferences.CheckUpdates = updates.IsChecked == true;
        _preferences.BetaUpdates = beta.IsChecked == true;
        try
        {
            if (startup.IsChecked == true && Environment.ProcessPath is { } exe) run?.SetValue("ConsoleMode", "\"" + exe + "\" --home");
            else run?.DeleteValue("ConsoleMode", false);
        }
        catch (Exception ex) { Status.Text = "Avvio automatico: " + ex.Message; }
        SetKeyboardShortcut();
        PlaceHome();
        Save();
        await ReloadGames();
    }

    private void PickImage(Game game, bool background)
    {
        var picker = new OpenFileDialog { Filter = "Immagini (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp" };
        if (picker.ShowDialog(this) != true) return;
        if (LoadImage(picker.FileName, 320) is null) { Status.Text = "Immagine non leggibile."; return; }
        var art = _preferences.For(game);
        var image = Preferences.StoreImage(game.Id, background ? "background" : "card", picker.FileName);
        if (background) art.Background = image;
        else art.Card = image;
        _tiles.ForEach(tile => tile.Tag = null);
        Save();
        UpdateHero(false, false);
    }

    private async Task SearchArtwork(Game game)
    {
        _artwork?.Cancel();
        var panel = new StackPanel();
        var query = Field(panel, "Titolo da cercare su Steam", game.Title);
        var search = new Button { Content = "Cerca" };
        var results = new ListBox { MinHeight = 100, MaxHeight = 230 };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(search);
        panel.Children.Add(message);
        panel.Children.Add(results);
        using var cancellation = new CancellationTokenSource();
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false;
            try
            {
                var found = await SteamStoreArtwork.SearchAsync(query.Text.Trim(), cancellation.Token);
                results.ItemsSource = found;
                results.SelectedIndex = found.Count == 1 ? 0 : -1;
                message.Text = found.Count == 0 ? "Nessun titolo trovato." : "Seleziona il gioco da abbinare.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { message.Text = "Ricerca non disponibile: " + ex.Message; }
            finally { search.IsEnabled = true; }
        };
        var apply = ShowDialog("Cerca un altro titolo", panel);
        cancellation.Cancel();
        if (apply && results.SelectedItem is ArtworkMatch match)
            await LoadArtwork(game, match, true, CancellationToken.None);
    }

    private void ScheduleArtwork(Game? game)
    {
        _artwork?.Cancel();
        if (game is null || _session is not null || _dialog) return;
        var art = _preferences.For(game);
        if (art.ArtworkId is not null || !_fetched.Add(game.Id)) return;
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
                var found = await SteamStoreArtwork.SearchAsync(game.Title, lookup.Token);
                var exact = found.Where(result => string.Equals(result.Title, game.Title, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (exact.Count == 1) match = exact[0];
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
        var content = await SteamStoreArtwork.GetAsync(match, token);
        if (content is null)
        {
            if (replace) Status.Text = "Contenuti non disponibili per il titolo selezionato.";
            return;
        }
        var art = _preferences.For(game);
        var cover = replace || !File.Exists(art.Card) ? await SteamStoreArtwork.DownloadAsync(content.Cover, token) : null;
        string? backdrop = null;
        try
        {
            backdrop = replace || !File.Exists(art.Background) ? await SteamStoreArtwork.DownloadAsync(content.Background, token) : null;
            token.ThrowIfCancellationRequested();
            if (cover is not null && (replace || !File.Exists(art.Card))) art.Card = Preferences.StoreImage(game.Id, "card", cover);
            if (backdrop is not null && (replace || !File.Exists(art.Background))) art.Background = Preferences.StoreImage(game.Id, "background", backdrop);
            art.Description = content.Description;
            art.ArtworkId = match.SteamId;
            Save();
            _tiles.ForEach(tile => tile.Tag = null);
            if (Current?.Id == game.Id) UpdateHero(false, false);
        }
        finally
        {
            if (cover is not null) File.Delete(cover);
            if (backdrop is not null) File.Delete(backdrop);
        }
    }

    private static TextBox Field(Panel panel, string title, string value)
    {
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 6, 0, 3) });
        var field = new TextBox { Text = value, MinWidth = 310 };
        panel.Children.Add(field);
        return field;
    }

    private bool ShowDialog(string title, Panel panel)
    {
        _dialog = true;
        try
        {
            panel.Margin = new Thickness(22);
            var window = new Window
            {
                Owner = this, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                Content = panel, MaxHeight = Math.Max(300, ActualHeight - 40)
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var save = new Button { Content = "Applica" };
            save.Click += (_, _) => window.DialogResult = true;
            buttons.Children.Add(save);
            buttons.Children.Add(new Button { Content = "Annulla", IsCancel = true });
            panel.Children.Add(buttons);
            window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return window.ShowDialog() == true;
        }
        finally { _dialog = false; }
    }
}
