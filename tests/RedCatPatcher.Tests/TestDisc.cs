using System.Text;

namespace RedCatPatcher.Tests;

/// <summary>
/// Bouwt een minimale ISO 9660-image (optioneel met Joliet) met de map DATA en een paar bestanden,
/// plus een raw MODE2/2352-variant zoals een .bin van een echte cd.
/// </summary>
public static class TestDisc
{
    private const int S = 2048;

    public static readonly (string Name, byte[] Content)[] Files =
    [
        ("RcHcGame.dat", Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray()), // > 2 sectoren
        ("AdamWin.ini", Encoding.ASCII.GetBytes("[Engine]\r\nMode=640x480\r\n")),
    ];

    public static byte[] BuildIso(bool joliet)
    {
        // Layout: 16 PVD, 17 Joliet SVD of terminator, 18 terminator,
        //         20/21 root+DATA (primair), 22/23 root+DATA (Joliet), 24.. bestandsinhoud
        int fileStart = 24;
        var fileLba = new List<int>();
        int next = fileStart;
        foreach (var (_, content) in Files) { fileLba.Add(next); next += (content.Length + S - 1) / S; }
        var iso = new byte[next * S];

        WriteDescriptor(iso, 16, type: 1, rootLba: 20, jolietEscape: false);
        if (joliet) WriteDescriptor(iso, 17, type: 2, rootLba: 22, jolietEscape: true);
        WriteDescriptor(iso, joliet ? 18 : 17, type: 255, rootLba: 0, jolietEscape: false);

        foreach (var (rootLba, dataLba, j) in new[] { (20, 21, false), (22, 23, true) })
        {
            if (j && !joliet) continue;
            WriteDir(iso, rootLba, rootLba, rootLba, [Record(dataLba, S, true, Name("DATA", "Data", j))]);
            var entries = Files.Select((f, i) => Record(fileLba[i], f.Content.Length, false, Name(f.Name.ToUpperInvariant() + ";1", f.Name, j))).ToList();
            WriteDir(iso, dataLba, dataLba, rootLba, entries);
        }
        for (int i = 0; i < Files.Length; i++) Files[i].Content.CopyTo(iso, fileLba[i] * S);
        return iso;
    }

    /// <summary>Raw sectoren van 2352 bytes: sync, header (mode 2), subheader, data, lege EDC/ECC.</summary>
    public static byte[] ToRawMode2(byte[] iso)
    {
        int sectors = iso.Length / S;
        var raw = new byte[sectors * 2352];
        for (int i = 0; i < sectors; i++)
        {
            int o = i * 2352;
            for (int k = 1; k <= 10; k++) raw[o + k] = 0xFF;
            raw[o + 15] = 2;
            Array.Copy(iso, i * S, raw, o + 24, S);
        }
        return raw;
    }

    private static byte[] Name(string primary, string jolietName, bool joliet) =>
        joliet ? Encoding.BigEndianUnicode.GetBytes(jolietName) : Encoding.ASCII.GetBytes(primary);

    private static void WriteDescriptor(byte[] iso, int lba, byte type, int rootLba, bool jolietEscape)
    {
        int o = lba * S;
        iso[o] = type;
        Encoding.ASCII.GetBytes("CD001").CopyTo(iso, o + 1);
        iso[o + 6] = 1;
        if (type == 255) return;
        Encoding.ASCII.GetBytes("REDCAT".PadRight(32)).CopyTo(iso, o + 40);
        if (jolietEscape) { iso[o + 88] = 0x25; iso[o + 89] = 0x2F; iso[o + 90] = 0x45; }
        Record(rootLba, S, true, [0]).CopyTo(iso, o + 156);
    }

    private static void WriteDir(byte[] iso, int lba, int self, int parent, List<byte[]> entries)
    {
        int o = lba * S;
        foreach (var rec in new[] { Record(self, S, true, [0]), Record(parent, S, true, [1]) }.Concat(entries))
        {
            rec.CopyTo(iso, o);
            o += rec.Length;
        }
    }

    private static byte[] Record(int lba, int size, bool dir, byte[] name)
    {
        int len = 33 + name.Length;
        if (len % 2 == 1) len++;
        var r = new byte[len];
        r[0] = (byte)len;
        BitConverter.GetBytes(lba).CopyTo(r, 2);
        BitConverter.GetBytes(size).CopyTo(r, 10);
        r[25] = (byte)(dir ? 0x02 : 0);
        r[28] = 1;
        r[32] = (byte)name.Length;
        name.CopyTo(r, 33);
        return r;
    }
}
