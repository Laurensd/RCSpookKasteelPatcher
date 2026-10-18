namespace RedCatPatcher.Patching;

/// <summary>
/// Fix: de engine zoekt bij het opstarten alleen joystick-ID 0 en 1 (<c>joyGetPosEx</c>). Windows nummert
/// controllers in winmm niet altijd vanaf 0: met virtuele gamepads (Steam, Oculus) of eerder aangesloten controllers
/// komt een Xbox-controller bijvoorbeeld op ID 2 en ziet het spel hem nooit. Deze fix probeert alle 16 ID's en
/// gebruikt de eerste die antwoordt; het spel leest daarna elk frame die ID (<c>[esi]</c>).
/// Achtergrond: docs/TECHNISCH.md, "Optie --controller".
/// </summary>
public static class JoystickSearch
{
    public const string File = "RcHcGame.dat";
    public const string Description = "Joystick: zoekt de controller op elk ID (0-15) in plaats van alleen 0 en 1";

    /// <summary>
    /// De opstartcode (1.0.0: <c>0x5A5A30</c>) tot en met het opslaan van de gevonden ID, 109 bytes. Op
    /// <see cref="IatOffset"/> staat het adres van <c>joyGetPosEx</c> in de importtabel; dat verschilt per build.
    /// </summary>
    private static readonly byte[] Original =
    [
        0x8b, 0x2d, 0, 0, 0, 0,                                   // mov ebp, [joyGetPosEx]
        0x8d, 0x7e, 0x04,                                         // lea edi, [esi+4]          (JOYINFOEX)
        0x57, 0x6a, 0x00,                                         // push edi; push 0
        0xc7, 0x84, 0x24, 0xc4, 0x01, 0, 0, 0, 0, 0, 0,           // mov [esp+1c4], 0          (try-niveau)
        0xc7, 0x07, 0x34, 0, 0, 0,                                // mov [edi], 34h            (dwSize)
        0xc7, 0x46, 0x08, 0x83, 0, 0, 0,                          // mov [esi+8], 83h          (dwFlags)
        0xff, 0xd5, 0x85, 0xc0, 0x75, 0x1e,                       // call ebp; test; jne ID 1
        0x8b, 0x46, 0x0c, 0x8b, 0x4e, 0x10, 0xc6, 0x46, 0x38, 0x01,
        0x89, 0x86, 0xac, 0, 0, 0, 0x89, 0x8e, 0xb0, 0, 0, 0,     // midden onthouden
        0xc7, 0x06, 0, 0, 0, 0, 0xeb, 0x25,                       // [esi] = 0
        0x57, 0x6a, 0x01, 0xff, 0xd5, 0x85, 0xc0, 0x75, 0x1c,     // ID 1
        0x8b, 0x56, 0x0c, 0x8b, 0x46, 0x10, 0xc6, 0x46, 0x38, 0x01,
        0x89, 0x96, 0xac, 0, 0, 0, 0x89, 0x86, 0xb0, 0, 0, 0,
        0xc7, 0x06, 0x01, 0, 0, 0,                                // [esi] = 1
    ];

    /// <summary>Hetzelfde stuk als lus over ID 0-15, met ebp als teller (ebp wordt verderop niet meer gebruikt).</summary>
    private static readonly byte[] Loop =
    [
        0x8d, 0x7e, 0x04,                                         // lea edi, [esi+4]
        0xc7, 0x84, 0x24, 0xbc, 0x01, 0, 0, 0, 0, 0, 0,           // mov [esp+1bc], 0          (zelfde plek, zonder de twee pushes)
        0x31, 0xed,                                               // xor ebp, ebp
        0xc7, 0x07, 0x34, 0, 0, 0,                                // volgende: mov [edi], 34h
        0xc7, 0x46, 0x08, 0x83, 0, 0, 0,                          // mov [esi+8], 83h
        0x57, 0x55,                                               // push edi; push ebp
        0xff, 0x15, 0, 0, 0, 0,                                   // call [joyGetPosEx]
        0x85, 0xc0, 0x74, 0x08,                                   // test eax, eax; je gevonden
        0x45, 0x83, 0xfd, 0x10, 0x72, 0xe1,                       // inc ebp; cmp ebp, 16; jb volgende
        0xeb, 0x3c,                                               // jmp einde
        0x8b, 0x46, 0x0c, 0x8b, 0x4e, 0x10, 0xc6, 0x46, 0x38, 0x01, // gevonden: midden onthouden
        0x89, 0x86, 0xac, 0, 0, 0, 0x89, 0x8e, 0xb0, 0, 0, 0,
        0x89, 0x2e,                                               // mov [esi], ebp
        .. Enumerable.Repeat((byte)0x90, 36),                     // einde
    ];

    private const int IatOffset = 2, LoopIatOffset = 33;

    /// <summary>Past de fix toe op de inhoud van RcHcGame.dat in het geheugen.</summary>
    public static PatchResult Apply(byte[] data)
    {
        if (Find(data, Loop, LoopIatOffset) >= 0) return PatchResult.AlreadyApplied;
        int at = Find(data, Original, IatOffset);
        if (at < 0) throw new PatchException($"Joystickzoektocht niet gevonden in {File}.");

        var iat = data.AsSpan(at + IatOffset, 4).ToArray();
        Loop.CopyTo(data, at);
        iat.CopyTo(data, at + LoopIatOffset);
        return PatchResult.Applied;
    }

    /// <summary>De plek van <paramref name="code"/>, met 4 willekeurige bytes op <paramref name="iatOffset"/>; -1 als hij er niet of meer dan eens staat.</summary>
    private static int Find(byte[] data, byte[] code, int iatOffset)
    {
        int found = -1, count = 0;
        ReadOnlySpan<byte> after = code.AsSpan(iatOffset + 4);
        for (int i = data.AsSpan().IndexOf(after); i >= 0;)
        {
            int start = i - iatOffset - 4;
            if (start >= 0 && data.AsSpan(start, iatOffset).SequenceEqual(code.AsSpan(0, iatOffset))) { found = start; count++; }
            int next = data.AsSpan(i + 1).IndexOf(after);
            i = next < 0 ? -1 : i + 1 + next;
        }
        if (count > 1) throw new PatchException($"Joystickzoektocht {count}x gevonden in {File} (verwacht 1x).");
        return found;
    }
}
