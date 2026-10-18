using System.Runtime.InteropServices;

namespace RedCatLauncher;

/// <summary>
/// Leest XInput-controllers (poll vanuit een UI-timer); Avalonia leest zelf geen gamepad. Onder Steam Input komt de
/// Steam Deck-controller standaard als XInput-pad binnen. Een ingedrukt gehouden richting herhaalt.
/// </summary>
sealed class Pad
{
    public enum Action { None, Up, Down, Left, Right, Accept, Back, Start }

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

    const ushort DUp = 0x0001, DDown = 0x0002, DLeft = 0x0004, DRight = 0x0008, StartButton = 0x0010, A = 0x1000, B = 0x2000;
    const ushort Directions = DUp | DDown | DLeft | DRight;
    const short Dead = 16000;
    static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400), RepeatEvery = TimeSpan.FromMilliseconds(120);

    public static bool Available { get; } = OperatingSystem.IsWindows() && Probe();

    ushort previous;
    DateTime nextRepeat;

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
            if (s.ThumbLY > Dead) now |= DUp;
            if (s.ThumbLY < -Dead) now |= DDown;
            if (s.ThumbLX < -Dead) now |= DLeft;
            if (s.ThumbLX > Dead) now |= DRight;
            break;
        }
        var pressed = (ushort)(now & ~previous);
        previous = now;

        if ((pressed & A) != 0) return Action.Accept;
        if ((pressed & B) != 0) return Action.Back;
        if ((pressed & StartButton) != 0) return Action.Start;

        var direction = (ushort)(now & Directions);
        if ((pressed & Directions) != 0) nextRepeat = DateTime.UtcNow + RepeatDelay;
        else if (direction != 0 && DateTime.UtcNow >= nextRepeat) nextRepeat = DateTime.UtcNow + RepeatEvery;
        else return Action.None;

        return (direction & DUp) != 0 ? Action.Up
            : (direction & DDown) != 0 ? Action.Down
            : (direction & DLeft) != 0 ? Action.Left
            : Action.Right;
    }
}
