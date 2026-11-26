using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;
using WinRT.Interop;

namespace Ron1n.Mode;

public sealed partial class HomeWindow : Window
{
    private readonly Preferences _preferences;
    private readonly Controller _controller = new();
    private readonly DispatcherQueueTimer _input;
    private readonly DispatcherQueueTimer _sessionTimer;
    private readonly List<Button> _tiles = [];
    private List<Game> _games = [];
    private int _selected = -1;
    private bool _scanActive, _dialog, _initialized, _closed, _shown;
    private GameSession? _session;
    private CancellationTokenSource? _artwork;
    private readonly HashSet<string> _fetched = [];
    private readonly nint _handle;
    private string? _loadProblem;

    public HomeWindow()
    {
        InitializeComponent();
        Title = "RON1N Mode";
        try { _preferences = Preferences.Load(); }
        catch (Exception ex) { _preferences = new(); _loadProblem = "Preferenze non leggibili: " + ex.Message; }

        _handle = WindowNative.GetWindowHandle(this);
        WindowsKeys.HookHotKey(_handle, ShowHome);
        SetKeyboardShortcut();
        _input = DispatcherQueue.CreateTimer();
        _input.Interval = TimeSpan.FromMilliseconds(65);
        _input.Tick += (_, _) => ReadController();
        _sessionTimer = DispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromMilliseconds(750);
        _sessionTimer.Tick += (_, _) => PollSession();
        Root.KeyDown += OnKey;
        Root.Loaded += async (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            _input.Start();
            await ReloadGames();
            if (_loadProblem is not null) Report(_loadProblem);
            if (_preferences.CheckUpdates) await CheckUpdate(false);
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _artwork?.Cancel();
            _input.Stop();
            _sessionTimer.Stop();
            _session?.Dispose();
            WindowsKeys.UnhookHotKey(_handle);
        };
    }

    public void ShowHome()
    {
        if (_closed || _dialog) return;
        var firstShow = !_shown;
        if (!firstShow) AppWindow.Show();
        Activate();
        _shown = true;
        if (firstShow) StartupLog.Write("Home window activated.");
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed) return;
            try { PlaceHome(); }
            catch (Exception ex)
            {
                StartupLog.Write($"Fullscreen: {ex}");
                Report("Impossibile attivare lo schermo intero.");
            }
            Native.BringForward(_handle);
            if (Root.XamlRoot is not null && Settings.Visibility == Visibility.Collapsed) FocusSelection();
        });
    }

    private void PlaceHome()
    {
        var area = DisplayArea.FindAll().FirstOrDefault(display => display.DisplayId.Value.ToString() == _preferences.Screen)
                   ?? DisplayArea.Primary;
        if (area is null) return;
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen &&
            DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)?.DisplayId.Value == area.DisplayId.Value)
            return;
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        var bounds = area.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, Math.Max(1, bounds.Width / 2), Math.Max(1, bounds.Height / 2)));
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    private async Task ReloadGames(string? select = null)
    {
        if (_scanActive) return;
        _scanActive = true;
        try
        {
            var selection = select ?? SelectedGame?.Id ?? _preferences.LastPlayed;
            Report("Aggiornamento della libreria…");
            var folder = _preferences.GamesFolder;
            var detected = await Task.Run(() => GameDiscovery.Scan(folder));
            var manualPaths = _preferences.Manual.Select(game => game.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _games = _preferences.Manual.Concat(detected.Where(game => !manualPaths.Contains(game.Path)))
                .DistinctBy(game => game.Id).ToList();
            _selected = _games.FindIndex(game => game.Id == selection);
            if (_selected < 0 && _games.Count > 0) _selected = 0;
            PopulateCards();
            UpdateHero();
            if (Settings.Visibility == Visibility.Collapsed) FocusSelection();
            Report("");
        }
        catch (Exception ex) { Report("Libreria non disponibile: " + ex.Message); }
        finally { _scanActive = false; }
    }

    private Game? SelectedGame => _selected >= 0 && _selected < _games.Count ? _games[_selected] : null;

    private void PopulateCards()
    {
        Cards.Children.Clear();
        _tiles.Clear();
        for (var index = 0; index < _games.Count; index++)
        {
            var game = _games[index];
            var position = index;
            var tile = new Button { Width = 130, Height = 103, CornerRadius = new CornerRadius(12) };
            AutomationProperties.SetName(tile, game.Title);
            ToolTipService.SetToolTip(tile, game.Title);
            tile.Click += (_, _) => { if (_selected == position) StartOrResume(); else Select(position); };
            tile.GotFocus += (_, _) => Select(position);
            _tiles.Add(tile);
            Cards.Children.Add(tile);
        }
        var add = new Button { Content = "+", Width = 54, Height = 54 };
        AutomationProperties.SetName(add, "Aggiungi un gioco");
        add.Click += async (_, _) => await AddManual();
        Cards.Children.Add(add);
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _games.Count || index == _selected) return;
        _selected = index;
        UpdateHero();
        _tiles[index].StartBringIntoView();
    }

    private void FocusSelection()
    {
        if (_selected >= 0 && _selected < _tiles.Count)
        {
            _tiles[_selected].Focus(FocusState.Programmatic);
            _tiles[_selected].StartBringIntoView();
        }
        else Play.Focus(FocusState.Programmatic);
    }

    private string? _backgroundPath;
    private Color _accent = Color.FromArgb(255, 110, 230, 211);
    private int _paletteRequest;

    private void UpdateHero(bool animate = true, bool fetch = true)
    {
        var item = SelectedGame;
        var art = item is null ? null : _preferences.For(item);
        TitleText.Text = item?.Title ?? "La tua libreria";
        Description.Text = art?.Description ?? "Aggiungi un gioco o scegli la cartella da cui importarlo.";
        Description.Visibility = Description.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var catalogId = art?.CatalogId ?? "";
        var wikiArticle = catalogId.StartsWith("wikipedia:", StringComparison.Ordinal) ? catalogId["wikipedia:".Length..] : "";
        CatalogSource.Visibility = wikiArticle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (wikiArticle.Length > 0)
        {
            CatalogSource.Content = "Fonte: Wikipedia";
            CatalogSource.NavigateUri = new Uri("https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(wikiArticle));
        }
        More.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        var running = item is not null && _session?.Game.Id == item.Id;
        Play.Content = item is null ? "Aggiungi gioco" : running ? "Riprendi" : "Gioca";
        SessionStatus.Text = running ? "In esecuzione" : _session is null ? "" : _session.Game.Title + " · In esecuzione";
        SessionStatus.Visibility = SessionStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var background = File.Exists(art?.Background) ? art!.Background : null;
        if (_backgroundPath != background)
        {
            _backgroundPath = background;
            Backdrop.Source = LoadImage(background, 1920);
            Fade(Backdrop, .2, .76);
        }
        if (animate) Fade(Hero, .64, 1);
        _ = UpdatePaletteAsync(background ?? art?.Card, item?.Id);
        for (var index = 0; index < _tiles.Count; index++)
        {
            var card = _tiles[index];
            card.BorderThickness = new Thickness(index == _selected ? 2 : 1);
            card.Opacity = index == _selected ? 1 : .76;
            var path = Math.Abs(index - _selected) <= 12 ? _preferences.For(_games[index]).Card : "";
            if (card.Content is not null && card.Tag as string == path) continue;
            card.Tag = path;
            var image = LoadImage(path, 260);
            if (image is not null)
            {
                card.Background = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
                card.Content = "";
            }
            else
            {
                card.ClearValue(Control.BackgroundProperty);
                card.Content = new TextBlock { Text = _games[index].Title, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
            }
        }
        PaintAccent();
        if (fetch) ScheduleArtwork(item);
    }

    private string? _paletteKey;

    private async Task UpdatePaletteAsync(string? path, string? id)
    {
        var key = path + "|" + id;
        if (key == _paletteKey) return;
        _paletteKey = key;
        var request = ++_paletteRequest;
        var color = await ArtworkPalette.ReadAsync(path, id);
        if (request != _paletteRequest || _closed) return;
        _accent = color;
        PaintAccent();
    }

    private void PaintAccent()
    {
        var brush = new SolidColorBrush(_accent);
        AccentBar.Background = brush;
        Play.Background = brush;
        Play.Foreground = new SolidColorBrush(Colors.Black);
        Ambient.Color = Color.FromArgb(99, _accent.R, _accent.G, _accent.B);
        for (var index = 0; index < _tiles.Count; index++)
            if (index == _selected) _tiles[index].BorderBrush = brush;
            else _tiles[index].ClearValue(Control.BorderBrushProperty);
    }

    private static void Fade(UIElement element, double from, double to)
    {
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) { element.Opacity = to; return; }
        var animation = new DoubleAnimation { From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private static BitmapImage? LoadImage(string? path, int width)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return new BitmapImage(new Uri(path)) { DecodePixelWidth = width };
    }

    private async void PlayGame(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is null) await AddManual();
        else StartOrResume();
    }

    private void StartOrResume()
    {
        var game = SelectedGame;
        if (game is null || _dialog) return;
        if (_session is not null)
        {
            if (_session.Game.Id != game.Id)
            {
                Select(_games.FindIndex(entry => entry.Id == _session.Game.Id));
                FocusSelection();
                return;
            }
            if (!_session.Resume()) Report("La finestra del gioco non è ancora disponibile.");
            return;
        }
        try
        {
            _preferences.Watched.TryGetValue(game.Id, out var watch);
            _session = new GameSession(game, watch ?? game.WatchProcess);
            _preferences.LastPlayed = game.Id;
            _artwork?.Cancel();
            Save();
            _sessionTimer.Start();
            UpdateHero(false, false);
            Report("");
        }
        catch (Exception ex) { Report("Avvio non riuscito: " + ex.Message); }
    }

    private void PollSession()
    {
        if (_session is null) return;
        try { if (_session.Poll()) return; }
        catch (Exception ex) { Report("Monitoraggio del gioco: " + ex.Message); }
        var game = _session.Game;
        var timedOut = _session.TimedOut;
        _sessionTimer.Stop();
        _session.Dispose();
        _session = null;
        Select(_games.FindIndex(entry => entry.Id == game.Id));
        UpdateHero(false, false);
        if (timedOut) Report("Finestra del gioco non rilevata. Controlla il processo nelle opzioni della scheda.");
        ShowHome();
    }

    private void OnKey(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape || _dialog) return;
        GoBack();
        args.Handled = true;
    }

    private void GoBack()
    {
        if (Settings.Visibility == Visibility.Visible) CloseSettings(this, new RoutedEventArgs());
        else AppWindow.Hide();
    }

    private void ReadController()
    {
        try
        {
            var (buttons, shortcut) = _controller.Poll(_preferences.ControllerShortcut);
            var status = _controller.Connected ? "Controller" : "Mouse e tastiera";
            if (InputStatus.Text != status) InputStatus.Text = status;
            if (shortcut && !_dialog) { ShowHome(); return; }
            if (Native.GetForegroundWindow() != _handle || Root.XamlRoot is null) return;
            if ((buttons & Controller.Down) != 0) FocusManager.TryMoveFocus(FocusNavigationDirection.Down);
            if ((buttons & Controller.Up) != 0) FocusManager.TryMoveFocus(FocusNavigationDirection.Up);
            if ((buttons & Controller.Left) != 0) FocusManager.TryMoveFocus(FocusNavigationDirection.Left);
            if ((buttons & Controller.Right) != 0) FocusManager.TryMoveFocus(FocusNavigationDirection.Right);
            if ((buttons & Controller.Confirm) != 0)
                WindowsKeys.ActivateFocused(FocusManager.GetFocusedElement(Root.XamlRoot));
            if ((buttons & Controller.Back) != 0)
            {
                if (_activeDialog is not null) _activeDialog.Hide();
                else if (!_dialog) GoBack();
            }
            if ((buttons & Controller.Menu) != 0 && !_dialog && Settings.Visibility == Visibility.Collapsed)
                OpenGameMenu(More, new RoutedEventArgs());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Debug.WriteLine(ex); }
    }

    private void ShowDesktop(object sender, RoutedEventArgs e) => AppWindow.Hide();

    private void Report(string message) => Status.Text = SettingsStatus.Text = message;

    private void Save()
    {
        if (_loadProblem is not null) return;
        try { _preferences.Save(); }
        catch (Exception ex) { Report("Salvataggio non riuscito: " + ex.Message); }
    }

    private void SetKeyboardShortcut()
    {
        WindowsKeys.UnregisterHotKey(_handle, 1);
        if (Enum.TryParse<VirtualKey>(_preferences.KeyboardShortcut, out var shortcut) && shortcut is >= VirtualKey.F1 and <= VirtualKey.F12
            && !WindowsKeys.RegisterHotKey(_handle, 1, 0x4003, (uint)shortcut))
            Report("La scorciatoia da tastiera è già in uso.");
    }
}
