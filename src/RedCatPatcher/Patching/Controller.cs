namespace RedCatPatcher.Patching;

/// <summary>
/// Gamepad/joystick. De engine leest zelf joystick 0 of 1 via winmm (joyGetPosEx): de linkerstick (X/Y) en tien knoppen.
/// Elke richting en knop staat in een vaste tabel gekoppeld aan een virtual-key-code; een actie geldt als ingedrukt als
/// de toets óf de joystickknop met die code is ingedrukt. Deze klasse herschrijft die tabel.
/// Achtergrond: zie docs/TECHNISCH.md, "Optie --controller".
/// </summary>
public static class Controller
{
    public const string File = "RcHcGame.dat";
    public const string Description = "Controller: stick beweegt, A springen, B ontwijken, X schieten, LB/RB wapen, Start menu";

    /// <summary>Tabelindex: 0 links, 1 rechts, 2 omhoog, 3 omlaag, 4–13 knop 1–10.</summary>
    public const int Entries = 14;

    // CDaviJoystick vult de tabel met 14 aanroepen "push vk; push index; mov ecx,esi; call SetMapping" (11 bytes per
    // aanroep), in deze volgorde. Identiek in alle drie de builds; alleen de call-offset verschilt.
    private static readonly int[] CallOrder = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 0, 2, 1, 3];
    private const int CallSize = 11;

    /// <summary>Wat de engine zelf doet: stick = pijltjestoetsen, knop 1–4 = Ctrl, Alt, Spatie, Enter.</summary>
    public static readonly byte[] Original = [0x25, 0x27, 0x26, 0x28, 0x11, 0x12, 0x20, 0x0D, 0, 0, 0, 0, 0, 0];

    // Extra toetsen voor acties die niet in het menu staan; ze komen naast de bestaande toetsen in PlayerDef.ini.
    private const byte F5 = 0x74, F6 = 0x75;
    private static readonly (string Action, string Name)[] ExtraBinds = [("WeaponPrev", "F5"), ("WeaponNext", "F6")];

    /// <summary>
    /// De tabel voor --controller. Knoppen volgens de DirectInput-nummering van een Xbox-controller
    /// (1 A, 2 B, 3 X, 4 Y, 5 LB, 6 RB, 7 Back, 8 Start, 9 linkerstick indrukken, 10 rechterstick indrukken).
    /// Met <paramref name="wasd"/> stuurt de stick W/A/S/D, zodat hij dezelfde toetsen indrukt als het toetsenbord.
    /// Zonder <paramref name="buttons"/> blijven de knoppen zoals de engine ze had.
    /// </summary>
    public static byte[] Mapping(bool wasd, bool buttons)
    {
        var map = (byte[])Original.Clone();
        if (wasd) (map[0], map[1], map[2], map[3]) = ((byte)'A', (byte)'D', (byte)'W', (byte)'S');
        if (buttons)
        {
            map[4] = 0x20;   // A      springen (Spatie)
            map[5] = 0x12;   // B      ontwijken (Alt; met de stick zijwaarts)
            map[6] = 0x11;   // X      schieten (Ctrl)
            map[7] = 0x0D;   // Y      Enter
            map[8] = F5;     // LB     vorig wapen
            map[9] = F6;     // RB     volgend wapen
            map[10] = 0x09;  // Back   scores (Tab)
            map[11] = 0x1B;  // Start  menu (Escape)
            map[12] = 0x2E;  // LS     camera centreren (Delete)
            map[13] = 0;     // RS     niets
        }
        return map;
    }

    /// <summary>Patcht de tabel in RcHcGame.dat en zet de extra toetsen in PlayerDef.ini.</summary>
    public static PatchResult Apply(string gameDir, bool wasd, bool buttons = true)
    {
        var result = PatchTable(Path.Combine(gameDir, File), Mapping(wasd, buttons));
        if (buttons) SetExtraBinds(gameDir, true);
        return result;
    }

    /// <summary>
    /// Zet de extra toetsen (F5/F6) erbij in PlayerDef.ini, of haalt ze weer weg. De andere toetsen in die regels
    /// blijven staan.
    /// </summary>
    public static void SetExtraBinds(string gameDir, bool on)
    {
        var playerDef = Path.Combine(gameDir, "settings", "PlayerDef.ini");
        foreach (var (action, name) in ExtraBinds)
            IniFile.Update(playerDef, s => s.Equals("KeyBinds", StringComparison.OrdinalIgnoreCase), action, keys =>
            {
                var list = keys.Split(',').Where(k => k.Trim().Length > 0 && !k.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
                return string.Join(",", on ? list.Append(name) : list);
            }, firstSectionOnly: true);
    }

    public static PatchResult PatchTable(string path, byte[] mapping)
    {
        if (mapping.Length != Entries || mapping.Any(vk => vk >= 0x80))
            throw new ArgumentException("14 virtual-key-codes onder 0x80 verwacht (push imm8 is signed).", nameof(mapping));
        if (!System.IO.File.Exists(path)) throw new PatchException($"{File} niet gevonden in de spelmap.");

        var data = System.IO.File.ReadAllBytes(path);
        var result = PatchTable(data, mapping);
        if (result == PatchResult.Applied) System.IO.File.WriteAllBytes(path, data);
        return result;
    }

    /// <summary>
    /// Schrijft de tabel in de inhoud van RcHcGame.dat in het geheugen. Elke toewijzing kan, dus ook terug naar
    /// <see cref="Original"/>.
    /// </summary>
    public static PatchResult PatchTable(byte[] data, byte[] mapping)
    {
        if (mapping.Length != Entries || mapping.Any(vk => vk >= 0x80))
            throw new ArgumentException("14 virtual-key-codes onder 0x80 verwacht (push imm8 is signed).", nameof(mapping));

        int at = FindTable(data);
        if (ReadTable(data, at).SequenceEqual(mapping)) return PatchResult.AlreadyApplied;

        for (int i = 0; i < CallOrder.Length; i++)
            data[at + i * CallSize + 1] = mapping[CallOrder[i]];
        return PatchResult.Applied;
    }

    /// <summary>De huidige tabel (indexvolgorde), of een fout als de code niet precies één keer gevonden wordt.</summary>
    public static byte[] ReadTable(byte[] data) => ReadTable(data, FindTable(data));

    private static byte[] ReadTable(byte[] data, int at)
    {
        var map = new byte[Entries];
        for (int i = 0; i < CallOrder.Length; i++) map[CallOrder[i]] = data[at + i * CallSize + 1];
        return map;
    }

    private static int FindTable(byte[] data)
    {
        int found = -1, count = 0;
        // Ankers: "6a ?? 6a 04 8b ce e8" voor de eerste aanroep; daarna de rest van de structuur controleren.
        ReadOnlySpan<byte> anchor = [0x6a, 0x04, 0x8b, 0xce, 0xe8];
        for (int i = data.AsSpan().IndexOf(anchor); i >= 0;)
        {
            int start = i - 2;
            if (start >= 0 && Matches(data, start)) { found = start; count++; }
            int next = data.AsSpan(i + 1).IndexOf(anchor);
            i = next < 0 ? -1 : i + 1 + next;
        }
        if (count != 1)
            throw new PatchException($"Joysticktabel {count}x gevonden in {File} (verwacht 1x).");
        return found;
    }

    private static bool Matches(byte[] data, int start)
    {
        if (start + CallOrder.Length * CallSize > data.Length) return false;
        for (int i = 0; i < CallOrder.Length; i++)
        {
            int o = start + i * CallSize;
            if (data[o] != 0x6a || data[o + 1] >= 0x80 || data[o + 2] != 0x6a || data[o + 3] != CallOrder[i]
                || data[o + 4] != 0x8b || data[o + 5] != 0xce || data[o + 6] != 0xe8)
                return false;
        }
        return true;
    }
}
