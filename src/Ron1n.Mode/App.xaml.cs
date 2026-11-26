using System.Windows;
using System.Windows.Threading;

namespace Ron1n.Mode;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _show;
    private RegisteredWaitHandle? _wait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\Ron1nMode", out var first);
        const string showEvent = @"Local\Ron1nMode.Show";
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(showEvent, out var existing))
                using (existing) existing.Set();
            Shutdown();
            return;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, showEvent);
        var window = new HomeWindow();
        MainWindow = window;
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) =>
            Dispatcher.InvokeAsync(window.ShowHome, DispatcherPriority.Normal), null, Timeout.Infinite, false);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wait?.Unregister(null);
        _show?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
