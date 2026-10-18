using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RedCatLauncher;

/// <summary>
/// Een map openen in de bestandsbeheerder. Onder Proton/Wine opent explorer.exe die van Wine; winebrowser geeft een
/// file://-adres door aan xdg-open, zodat de map in de bestandsbeheerder van Linux opent. Native op Linux: xdg-open.
/// </summary>
static class FileManager
{
    public static void Open(string dir)
    {
        if (!OperatingSystem.IsWindows()) Run("xdg-open", dir);
        else if (IsWine())
        {
            try { Run("winebrowser", new Uri(Path.GetFullPath(dir)).AbsoluteUri); }
            catch (System.ComponentModel.Win32Exception) { Run("explorer.exe", dir); }
        }
        else Run("explorer.exe", dir);
    }

    static void Run(string program, string argument) =>
        Process.Start(new ProcessStartInfo(program) { ArgumentList = { argument }, UseShellExecute = false })?.Dispose();

    /// <summary>Wine (en dus Proton) heeft een eigen export in ntdll.dll.</summary>
    static bool IsWine() => GetModuleHandle("ntdll.dll") is var ntdll && ntdll != IntPtr.Zero
        && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
}
