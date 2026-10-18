using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RedCatLauncher;

/// <summary>
/// Controller in de menu's: het menu (RcHcMenu.dat, Delphi) leest geen joystick en is met de muis te bedienen. Zolang
/// een venster van het menu op de voorgrond staat, beweegt de linkerstick (of d-pad) de muis, klikt A, is B Escape en
/// Start Enter. In het spel zelf doet dit niets: dat leest de controller zelf (Patching/Controller.cs).
/// Draait tot RedCat.exe stopt; RedCat.exe start het menu en het spel en wacht op beide.
/// </summary>
static class MenuPad
{
    const string MenuProcess = "RcHcMenu";
    const short Dead = 7849;                       // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
    const double FullSpeed = 0.9, DpadSpeed = 0.35; // schermhoogtes per seconde
    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(8);

    public static void Run(Process game)
    {
        if (!Pad.Available || !OperatingSystem.IsWindows()) return;
        int screenHeight = Math.Max(480, GetSystemMetrics(SmCyScreen));
        ushort previous = 0;
        bool leftDown = false;
        double restX = 0, restY = 0;
        int lastPid = 0;
        bool lastIsMenu = false;
        var clock = Stopwatch.StartNew();

        while (!game.HasExited)
        {
            Thread.Sleep(Tick);
            double seconds = clock.Elapsed.TotalSeconds;
            clock.Restart();

            GetWindowThreadProcessId(GetForegroundWindow(), out int pid);
            if (pid != lastPid)
            {
                lastPid = pid;
                lastIsMenu = IsMenu(pid);
            }
            bool connected = Pad.TryRead(out var state);
            if (!lastIsMenu || !connected)
            {
                if (leftDown) { Mouse(MouseLeftUp); leftDown = false; }
                // Een knop die in het spel al ingedrukt was, telt bij terugkomst in het menu niet als nieuwe druk
                previous = connected ? state.Buttons : (ushort)0;
                continue;
            }

            // Stick: kwadratische curve voor fijn mikken, d-pad met een vaste lage snelheid
            double x = Axis(state.ThumbLX), y = -Axis(state.ThumbLY);
            var buttons = state.Buttons;
            if ((buttons & Pad.DLeft) != 0) x -= DpadSpeed / FullSpeed;
            if ((buttons & Pad.DRight) != 0) x += DpadSpeed / FullSpeed;
            if ((buttons & Pad.DUp) != 0) y -= DpadSpeed / FullSpeed;
            if ((buttons & Pad.DDown) != 0) y += DpadSpeed / FullSpeed;
            if (x != 0 || y != 0)
            {
                double step = FullSpeed * screenHeight * seconds;
                restX += x * step;
                restY += y * step;
                int dx = (int)restX, dy = (int)restY;
                restX -= dx;
                restY -= dy;
                if ((dx != 0 || dy != 0) && GetCursorPos(out var cursor)) SetCursorPos(cursor.X + dx, cursor.Y + dy);
            }

            var pressed = (ushort)(buttons & ~previous);
            var released = (ushort)(previous & ~buttons);
            previous = buttons;
            if ((pressed & Pad.A) != 0) { Mouse(MouseLeftDown); leftDown = true; }
            if ((released & Pad.A) != 0 && leftDown) { Mouse(MouseLeftUp); leftDown = false; }
            if ((pressed & Pad.B) != 0) Key(VkEscape);
            if ((pressed & Pad.StartButton) != 0) Key(VkReturn);
        }
        if (leftDown) Mouse(MouseLeftUp);
    }

    static double Axis(short value)
    {
        double magnitude = Math.Abs((double)value);
        if (magnitude <= Dead) return 0;
        double t = Math.Min(1, (magnitude - Dead) / (32767 - Dead));
        return Math.Sign(value) * t * t;
    }

    static bool IsMenu(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            // .NET haalt alleen ".exe" van de naam af, dus hier staat "RcHcMenu.dat"
            return process.ProcessName.StartsWith(MenuProcess, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }      // proces al weg
        catch (InvalidOperationException) { return false; }
    }

    const int SmCyScreen = 1;
    const uint InputMouse = 0, InputKeyboard = 1, KeyUp = 0x0002, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
    const ushort VkEscape = 0x1B, VkReturn = 0x0D;

    static void Mouse(uint flags) =>
        Send(new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = flags } } });

    static void Key(ushort vk)
    {
        var scan = (ushort)MapVirtualKey(vk, 0);
        Send(new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Scan = scan } } });
        Send(new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Scan = scan, Flags = KeyUp } } });
    }

    static void Send(Input input) => SendInput(1, [input], Marshal.SizeOf<Input>());

    [StructLayout(LayoutKind.Sequential)]
    struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }

    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
}
