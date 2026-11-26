using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Ron1n.Mode;

internal static class WindowsKeys
{
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    private delegate nint Subclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private static readonly Dictionary<nint, Subclass> Hooks = [];

    public static void HookHotKey(nint window, Action action)
    {
        Subclass callback = (hwnd, message, wParam, lParam, _, _) =>
        {
            if (message == 0x0312 && wParam == 1)
            {
                try { action(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                return 0;
            }
            return DefSubclassProc(hwnd, message, wParam, lParam);
        };
        if (SetWindowSubclass(window, callback, 1, 0)) Hooks[window] = callback;
    }

    public static void UnhookHotKey(nint window)
    {
        UnregisterHotKey(window, 1);
        if (Hooks.Remove(window, out var callback)) RemoveWindowSubclass(window, callback, 1);
    }

    public static void ActivateFocused(object? element)
    {
        if (element is ComboBox combo) { combo.IsDropDownOpen = !combo.IsDropDownOpen; return; }
        if (element is not UIElement target) return;
        var peer = FrameworkElementAutomationPeer.FromElement(target) ?? FrameworkElementAutomationPeer.CreatePeerForElement(target);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
    }
}
