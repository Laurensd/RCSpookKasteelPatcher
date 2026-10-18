using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace RedCatLauncher;

/// <summary>
/// Het icoon van RedCat.exe voor het launchervenster (taakbalk en titelbalk). Uit het bestand in de spelmap gelezen,
/// zodat het icoon van Davilex niet in deze repository hoeft. Zonder RedCat.exe blijft het standaardicoon.
/// </summary>
static class GameIcon
{
    const int RedCatIconId = 101;   // groepsicoon in RedCat.exe
    const uint LoadAsDatafile = 0x2 | 0x20, ImageIcon = 1, WmSetIcon = 0x80;
    const int SmCxIcon = 11, SmCxSmIcon = 49, IconSmall = 0, IconBig = 1;

    public static void Apply(Window window, string exe)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(exe)) return;
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == 0) return;

        var module = LoadLibraryExW(exe, 0, LoadAsDatafile);
        if (module == 0) return;
        try
        {
            foreach (var (which, metric) in new[] { (IconBig, SmCxIcon), (IconSmall, SmCxSmIcon) })
            {
                int size = GetSystemMetrics(metric);
                var icon = LoadImageW(module, RedCatIconId, ImageIcon, size, size, 0);
                if (icon != 0) SendMessageW(hwnd, WmSetIcon, which, icon);
            }
        }
        finally { FreeLibrary(module); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern nint LoadLibraryExW(string path, nint file, uint flags);

    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(nint module);

    [DllImport("user32.dll")]
    static extern nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
