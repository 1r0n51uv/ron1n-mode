using Microsoft.UI.Xaml;

namespace Ron1n.Mode;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _show;
    private RegisteredWaitHandle? _wait;
    private HomeWindow? _window;

    public App()
    {
        StartupLog.Write("Initializing WinUI.");
        UnhandledException += (_, args) => StartupLog.Write(args.Exception.ToString());
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new Mutex(false, @"Local\Ron1nMode", out var first);
        const string showEvent = @"Local\Ron1nMode.Show";
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(showEvent, out var existing))
                using (existing) existing.Set();
            Exit();
            return;
        }

        _show = new EventWaitHandle(false, EventResetMode.AutoReset, showEvent);
        StartupLog.Write("Creating the home window.");
        var window = _window = new HomeWindow();
        window.Closed += (_, _) =>
        {
            _wait?.Unregister(null);
            _show?.Dispose();
            _instance?.Dispose();
        };
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) =>
            window.DispatcherQueue.TryEnqueue(window.ShowHome), null, Timeout.Infinite, false);
        window.ShowHome();
    }
}
