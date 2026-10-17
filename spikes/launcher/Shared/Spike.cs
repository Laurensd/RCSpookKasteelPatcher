using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LauncherSpike;

// Gedeeld tussen de WinForms- en de Avalonia-proef: RedCat.exe starten, opstarttijd loggen, controller lezen.
static class Spike
{
    public static readonly string Dir = AppContext.BaseDirectory;
    static readonly string LogPath = Path.Combine(Dir, "launcher-spike.log");

    public static bool Measure => Environment.GetCommandLineArgs().Contains("--measure");

    public static string Elapsed()
    {
        var ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        return $"{ms:0} ms";
    }

    public static void Log(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Start RedCat.exe uit de eigen map. Geeft een foutmelding terug, of null als het lukte.</summary>
    public static string? StartGame()
    {
        var exe = Path.Combine(Dir, "RedCat.exe");
        if (!File.Exists(exe)) return $"RedCat.exe niet gevonden in {Dir}";
        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Dir, UseShellExecute = false });
            Log("RedCat.exe gestart");
            return null;
        }
        catch (Exception e)
        {
            Log($"Starten mislukt: {e.Message}");
            return $"Starten mislukt: {e.Message}";
        }
    }

    public static string Describe() => $"wine={WineVersion() ?? "nee"}, xinput={Pad.Available}";

    static string? WineVersion()
    {
        var ntdll = NativeLibrary.Load("ntdll.dll");
        if (!NativeLibrary.TryGetExport(ntdll, "wine_get_version", out var fn)) return null;
        unsafe { return Marshal.PtrToStringAnsi(((delegate* unmanaged<nint>)fn)()); }
    }
}

/// <summary>
/// Leest XInput-controllers (poll vanuit een UI-timer). Onder Steam Input komt een Steam Deck-controller standaard als
/// XInput-pad binnen. Pijltjes/linkerstick = vorige/volgende knop, A = activeren, B = sluiten.
/// </summary>
sealed class Pad
{
    public enum Action { None, Previous, Next, Activate, Back }

    [StructLayout(LayoutKind.Sequential)]
    struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetState(uint userIndex, out XInputState state);

    const ushort Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, A = 0x1000, B = 0x2000;
    const short Dead = 16000;

    public static bool Available { get; } = Probe();
    ushort previous;

    static bool Probe()
    {
        try { XInputGetState(0, out _); return true; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public Action Poll()
    {
        if (!Available) return Action.None;
        ushort now = 0;
        for (uint i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out var s) != 0) continue;
            now = s.Buttons;
            if (s.ThumbLY > Dead) now |= Up;
            if (s.ThumbLY < -Dead) now |= Down;
            if (s.ThumbLX < -Dead) now |= Left;
            if (s.ThumbLX > Dead) now |= Right;
            break;
        }
        var pressed = (ushort)(now & ~previous);
        previous = now;
        if ((pressed & (Up | Left)) != 0) return Action.Previous;
        if ((pressed & (Down | Right)) != 0) return Action.Next;
        if ((pressed & A) != 0) return Action.Activate;
        if ((pressed & B) != 0) return Action.Back;
        return Action.None;
    }
}
