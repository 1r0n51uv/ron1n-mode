using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace Ron1n.Mode;

public partial class HomeWindow : Window
{
    private readonly Preferences _preferences;
    private readonly Controller _controller = new();
    private readonly DispatcherTimer _input = new() { Interval = TimeSpan.FromMilliseconds(65) };
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly List<Button> _tiles = [];
    private List<Game> _games = [];
    private int _selected = -1;
    private bool _scanActive;
    private bool _showingA;
    private bool _dialog;
    private GameSession? _session;
    private CancellationTokenSource? _artwork;
    private readonly HashSet<string> _fetched = [];
    private HwndSource? _source;
    private string? _loadProblem;
    private (string? Background, string? Card, string? Id) _paletteKey;
    private Color _accent = Color.FromRgb(110, 230, 211);

    public HomeWindow()
    {
        InitializeComponent();
        try { _preferences = Preferences.Load(); }
        catch (Exception ex) { _preferences = new(); _loadProblem = "Preferenze non leggibili: " + ex.Message; }
        SourceInitialized += (_, _) =>
        {
            _source = (HwndSource)PresentationSource.FromVisual(this);
            _source.AddHook(HandleWindowMessage);
            SetKeyboardShortcut();
            PlaceHome();
        };
        Loaded += async (_, _) =>
        {
            _input.Start();
            await ReloadGames();
            if (_loadProblem is not null) Status.Text = _loadProblem;
            if (_preferences.CheckUpdates) await CheckUpdate(false);
        };
        Closed += (_, _) =>
        {
            _artwork?.Cancel();
            _input.Stop();
            _sessionTimer.Stop();
            _session?.Dispose();
            if (_source is not null) WindowsKeys.UnregisterHotKey(_source.Handle, 1);
        };
        _input.Tick += (_, _) => ReadController();
        _sessionTimer.Tick += (_, _) => PollSession();
        PreviewKeyDown += OnKey;
        LibraryScroll.ScrollChanged += (_, _) => RefreshCardArtwork();
        SizeChanged += (_, _) =>
        {
            TitleText.FontSize = Math.Clamp(ActualHeight / 10, 38, 69);
            TitleText.LineHeight = TitleText.FontSize * 1.09;
        };
    }

    public void ShowHome()
    {
        if (_dialog) return;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        PlaceHome();
        if (_source is not null) Native.BringForward(_source.Handle);
        Activate();
        FocusSelection();
    }

    private void PlaceHome()
    {
        if (_source is null) return;
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == _preferences.Screen)
                     ?? Screen.PrimaryScreen;
        if (screen is not null) WindowsKeys.FillScreen(_source.Handle, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
    }

    private async Task ReloadGames()
    {
        if (_scanActive) return;
        _scanActive = true;
        try
        {
            var selection = Current?.Id ?? _preferences.LastPlayed;
            Status.Text = "Aggiornamento della libreria…";
            var folder = _preferences.GamesFolder;
            var detected = await Task.Run(() => GameDiscovery.Scan(folder));
            var manualPaths = _preferences.Manual.Select(game => game.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _games = _preferences.Manual.Concat(detected.Where(game => !manualPaths.Contains(game.Path)))
                .DistinctBy(game => game.Id).ToList();
            _selected = _games.FindIndex(game => game.Id == selection);
            if (_selected < 0 && _games.Count > 0) _selected = 0;
            PopulateCards();
            UpdateHero();
            FocusSelection();
            Status.Text = "";
        }
        catch (Exception ex) { Status.Text = "Libreria non disponibile: " + ex.Message; }
        finally { _scanActive = false; }
    }

    private Game? Current => _selected >= 0 && _selected < _games.Count ? _games[_selected] : null;

    private void PopulateCards()
    {
        Cards.Children.Clear();
        _tiles.Clear();
        for (var index = 0; index < _games.Count; index++)
        {
            var item = _games[index];
            var position = index;
            var tile = new Button
            {
                Width = 114, Height = 96, Margin = new Thickness(3, 3, 9, 3),
                Content = new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                ToolTip = item.Title
            };
            System.Windows.Automation.AutomationProperties.SetName(tile, item.Title);
            tile.Click += (_, _) =>
            {
                if (_selected == position) StartOrResume();
                else Select(position, true);
            };
            tile.GotKeyboardFocus += (_, _) => Select(position, false);
            _tiles.Add(tile);
            Cards.Children.Add(tile);
        }
        var add = new Button { Content = "+", Width = 57, Height = 68, ToolTip = "Aggiungi un gioco" };
        System.Windows.Automation.AutomationProperties.SetName(add, "Aggiungi un gioco");
        add.Click += async (_, _) => await AddManual();
        Cards.Children.Add(add);
    }

    private void Select(int index, bool scroll)
    {
        if (index < 0 || index >= _games.Count || index == _selected) return;
        _selected = index;
        UpdateHero();
        if (scroll) _tiles[index].BringIntoView();
    }

    private void FocusSelection()
    {
        if (_selected >= 0 && _selected < _tiles.Count)
        {
            _tiles[_selected].Focus();
            _tiles[_selected].BringIntoView();
        }
        else Play.Focus();
    }

    private void UpdateHero(bool animate = true, bool fetch = true)
    {
        var item = Current;
        var art = item is null ? null : _preferences.For(item);
        TitleText.Text = item?.Title ?? "La tua libreria";
        Description.Text = art?.Description ?? "Aggiungi un gioco o scegli la cartella da cui importarlo.";
        Description.Visibility = Description.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        More.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        var running = item is not null && _session?.Game.Id == item.Id;
        Play.Content = item is null ? "Aggiungi gioco" : running ? "Riprendi" : "Gioca";
        SessionStatus.Text = running ? "In esecuzione" : _session is null ? "" : _session.Game.Title + " · In esecuzione";
        SessionStatus.Visibility = SessionStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var paletteKey = (art?.Background, art?.Card, item?.Id);
        if (_paletteKey != paletteKey)
        {
            _paletteKey = paletteKey;
            _accent = Palette(paletteKey.Item1, paletteKey.Item2, paletteKey.Item3);
        }
        var accent = _accent;
        AccentBar.Background = new SolidColorBrush(accent);
        Play.Background = new SolidColorBrush(accent);
        Ambient.BeginAnimation(GradientStop.ColorProperty,
            new ColorAnimation(Color.FromArgb(141, accent.R, accent.G, accent.B),
                TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 280 : 0)));

        var oldLayer = _showingA ? BackdropA : BackdropB;
        var newLayer = _showingA ? BackdropB : BackdropA;
        var desired = File.Exists(art?.Background) ? new Uri(art!.Background) : null;
        if ((oldLayer.Source as BitmapImage)?.UriSource != desired)
        {
            var background = LoadImage(art?.Background, 1920);
            _showingA = !_showingA;
            newLayer.Source = background;
            Fade(oldLayer, 0, 400);
            Fade(newLayer, background is null ? 0 : .9, 400);
        }
        if (animate && SystemParameters.ClientAreaAnimation)
            Hero.BeginAnimation(OpacityProperty, new DoubleAnimation(.63, 1, TimeSpan.FromMilliseconds(240)));
        for (var index = 0; index < _tiles.Count; index++)
        {
            var card = _tiles[index];
            var selected = index == _selected;
            card.BorderBrush = new SolidColorBrush(selected ? accent : Color.FromRgb(65, 77, 101));
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.Opacity = selected ? 1 : .78;
        }
        RefreshCardArtwork();
        if (fetch) ScheduleArtwork(item);
    }

    private void RefreshCardArtwork()
    {
        const int cardPitch = 126;
        var first = Math.Max(0, (int)(LibraryScroll.HorizontalOffset / cardPitch) - 2);
        var last = Math.Min(_tiles.Count, first + 5 + (int)(LibraryScroll.ViewportWidth / cardPitch));
        for (var index = 0; index < _tiles.Count; index++)
        {
            var card = _tiles[index];
            var path = index >= first && index < last ? _preferences.For(_games[index]).Card : "";
            if (card.Tag as string == path) continue;
            card.Tag = path;
            var cover = LoadImage(path, 250);
            if (cover is not null) card.Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
            else card.ClearValue(Control.BackgroundProperty);
            if (card.Content is TextBlock label)
                label.Visibility = cover is not null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void Fade(UIElement layer, double opacity, int milliseconds)
    {
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity,
            TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0)));
    }

    private static BitmapImage? LoadImage(string? path, int width)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = width;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException) { return null; }
    }

    private static Color Palette(string? background, string? card, string? id)
    {
        var image = LoadImage(background, 28) ?? LoadImage(card, 28);
        if (image is null)
        {
            return (id?.Aggregate(0, (value, character) => (value + character) % 3) ?? 0) switch
            {
                1 => Color.FromRgb(255, 171, 119),
                2 => Color.FromRgb(199, 171, 255),
                _ => Color.FromRgb(110, 230, 211)
            };
        }
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[pixels.PixelWidth * pixels.PixelHeight * 4];
        pixels.CopyPixels(buffer, pixels.PixelWidth * 4, 0);
        double red = 0, green = 0, blue = 0, weight = 0;
        for (var index = 0; index < buffer.Length; index += 4)
        {
            var saturation = Math.Max(buffer[index], Math.Max(buffer[index + 1], buffer[index + 2]))
                             - Math.Min(buffer[index], Math.Min(buffer[index + 1], buffer[index + 2]));
            if (buffer[index + 3] < 128 || saturation < 24) continue;
            weight += saturation;
            blue += saturation * buffer[index];
            green += saturation * buffer[index + 1];
            red += saturation * buffer[index + 2];
        }
        if (weight == 0) return Color.FromRgb(110, 230, 211);
        return Color.FromRgb(Lift(red / weight), Lift(green / weight), Lift(blue / weight));
        static byte Lift(double value) => (byte)(100 + value * .60);
    }

    private async void PlayGame(object sender, RoutedEventArgs e)
    {
        if (Current is null) await AddManual();
        else StartOrResume();
    }

    private void StartOrResume()
    {
        var game = Current;
        if (game is null || _dialog) return;
        if (_session is not null)
        {
            if (_session.Game.Id != game.Id)
            {
                var active = _games.FindIndex(entry => entry.Id == _session.Game.Id);
                Select(active, true);
                FocusSelection();
                return;
            }
            if (!_session.Resume()) Status.Text = "La finestra del gioco non è ancora disponibile.";
            return;
        }
        try
        {
            _preferences.Watched.TryGetValue(game.Id, out var watch);
            _session = new GameSession(game, watch ?? game.WatchProcess);
            _preferences.LastPlayed = game.Id;
            Save();
            _sessionTimer.Start();
            UpdateHero(false, false);
            Status.Text = "";
        }
        catch (Exception ex) { Status.Text = "Avvio non riuscito: " + ex.Message; }
    }

    private void PollSession()
    {
        if (_session is null) return;
        try { if (_session.Poll()) return; }
        catch (Exception ex) { Status.Text = "Monitoraggio del gioco: " + ex.Message; }
        var game = _session.Game;
        var timedOut = _session.TimedOut;
        _sessionTimer.Stop();
        _session.Dispose();
        _session = null;
        Select(_games.FindIndex(entry => entry.Id == game.Id), true);
        UpdateHero(false, false);
        if (timedOut) Status.Text = "Finestra del gioco non rilevata. Controlla il processo nelle opzioni della scheda.";
        ShowHome();
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (_dialog || Keyboard.FocusedElement is TextBox) return;
        if (args.Key is Key.Left or Key.Right)
        {
            Navigate(args.Key == Key.Right ? 1 : -1);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            Hide();
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && Keyboard.FocusedElement is Button button && _tiles.Contains(button))
        {
            StartOrResume();
            args.Handled = true;
        }
    }

    private void Navigate(int direction)
    {
        if (_games.Count == 0) return;
        Select(Math.Clamp(_selected + direction, 0, _games.Count - 1), true);
        FocusSelection();
    }

    private static void AdvanceFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement target)
            target.MoveFocus(new TraversalRequest(direction));
    }

    private static void PressFocusedButton()
    {
        if (Keyboard.FocusedElement is Button button)
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        else if (Keyboard.FocusedElement is MenuItem item)
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private void ReadController()
    {
        try
        {
            var (buttons, shortcut) = _controller.Poll(_preferences.ControllerShortcut);
            InputStatus.Text = _controller.Connected ? "Controller" : "Mouse e tastiera";
            if (_dialog)
            {
                if ((buttons & Controller.Down) != 0) AdvanceFocus(FocusNavigationDirection.Next);
                if ((buttons & Controller.Up) != 0) AdvanceFocus(FocusNavigationDirection.Previous);
                if ((buttons & Controller.Confirm) != 0) PressFocusedButton();
                if ((buttons & Controller.Back) != 0 && Keyboard.FocusedElement is DependencyObject focus)
                    Window.GetWindow(focus)?.Close();
                return;
            }
            if (shortcut) { ShowHome(); return; }
            if (!IsActive) return;
            if ((buttons & Controller.Down) != 0) AdvanceFocus(FocusNavigationDirection.Next);
            if ((buttons & Controller.Up) != 0) AdvanceFocus(FocusNavigationDirection.Previous);
            if ((buttons & Controller.Left) != 0) Navigate(-1);
            if ((buttons & Controller.Right) != 0) Navigate(1);
            if ((buttons & Controller.Confirm) != 0)
            {
                if (Keyboard.FocusedElement is Button button && _tiles.Contains(button)) StartOrResume();
                else PressFocusedButton();
            }
            if ((buttons & Controller.Back) != 0) Hide();
            if ((buttons & Controller.Menu) != 0) OpenGameMenu(More, new RoutedEventArgs());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine(ex);
        }
    }

    private void ShowDesktop(object sender, RoutedEventArgs e) => Hide();

    private void Save()
    {
        if (_loadProblem is not null) return;
        try { _preferences.Save(); }
        catch (Exception ex) { Status.Text = "Salvataggio non riuscito: " + ex.Message; }
    }

    private nint HandleWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == 1)
        {
            ShowHome();
            handled = true;
        }
        return 0;
    }

    private void SetKeyboardShortcut()
    {
        if (_source is null) return;
        WindowsKeys.UnregisterHotKey(_source.Handle, 1);
        if (Enum.TryParse<Key>(_preferences.KeyboardShortcut, out var shortcut) && shortcut is >= Key.F1 and <= Key.F12
            && !WindowsKeys.RegisterHotKey(_source.Handle, 1, 0x4003, (uint)KeyInterop.VirtualKeyFromKey(shortcut)))
            Status.Text = "La scorciatoia da tastiera è già in uso.";
    }
}
