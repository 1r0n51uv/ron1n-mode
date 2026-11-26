using System.Runtime.InteropServices;

namespace Ron1n.Mode;

internal static class WindowsKeys
{
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    public static void FillScreen(nint window, int x, int y, int width, int height) =>
        SetWindowPos(window, 0, x, y, width, height, 0x0014);
}
