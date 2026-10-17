using System.Text;

namespace RedCatPatcher.Disc;

public sealed record IsoEntry(string Name, bool IsDirectory, uint Lba, uint Size, IReadOnlyList<IsoEntry> Children);

/// <summary>
/// ISO 9660-lezer met Joliet-ondersteuning (lange namen met hoofd- en kleine letters).
/// </summary>
public sealed class Iso9660
{
    private readonly DiscImage _disc;
    private readonly bool _joliet;

    public IsoEntry Root { get; }
    public string VolumeLabel { get; }

    /// <summary>Grootte van het volume in sectoren volgens de volume descriptor (0 als die het niet vermeldt).</summary>
    public uint VolumeSectors { get; }

    public Iso9660(DiscImage disc)
    {
        _disc = disc;
        byte[]? primary = null, joliet = null;
        var sector = new byte[DiscImage.SectorDataSize];
        for (long lba = 16; lba < 64; lba++)
        {
            disc.ReadSector(lba, sector);
            if (Encoding.ASCII.GetString(sector, 1, 5) != "CD001")
                throw new PatchException("Dit is geen geldige ISO 9660-disc (CD001 niet gevonden).");
            byte type = sector[0];
            if (type == 255) break;
            if (type == 1 && primary == null) primary = (byte[])sector.Clone();
            // Joliet: supplementary descriptor met escape-sequence %/@, %/C of %/E
            if (type == 2 && sector[88] == 0x25 && sector[89] == 0x2F && sector[90] is 0x40 or 0x43 or 0x45)
                joliet = (byte[])sector.Clone();
        }
        var descriptor = joliet ?? primary ?? throw new PatchException("Geen primary volume descriptor gevonden.");
        _joliet = joliet != null;
        VolumeLabel = (primary != null ? Encoding.ASCII.GetString(primary, 40, 32) : "").Trim();
        VolumeSectors = BitConverter.ToUInt32(primary ?? descriptor, 80);
        Root = ReadDirectory("", descriptor.AsSpan(156, 34).ToArray(), depth: 0);
    }

    private IsoEntry ReadDirectory(string name, byte[] record, int depth)
    {
        uint lba = BitConverter.ToUInt32(record, 2);
        uint size = BitConverter.ToUInt32(record, 10);
        var children = new List<IsoEntry>();
        if (depth > 16) return new IsoEntry(name, true, lba, size, children);

        int sectors = (int)((size + DiscImage.SectorDataSize - 1) / DiscImage.SectorDataSize);
        var data = _disc.ReadSectors(lba, sectors);
        int pos = 0;
        while (pos < data.Length)
        {
            int len = data[pos];
            if (len == 0)
            {
                // Records lopen niet over sectorgrenzen: door naar de volgende sector.
                pos = (pos / DiscImage.SectorDataSize + 1) * DiscImage.SectorDataSize;
                continue;
            }
            var rec = data.AsSpan(pos, len).ToArray();
            pos += len;
            int nameLen = rec[32];
            if (nameLen == 1 && rec[33] <= 1) continue; // "." en ".."
            bool isDir = (rec[25] & 0x02) != 0;
            string entryName = DecodeName(rec.AsSpan(33, nameLen));
            if (isDir)
                children.Add(ReadDirectory(entryName, rec, depth + 1));
            else
                children.Add(new IsoEntry(entryName, false, BitConverter.ToUInt32(rec, 2), BitConverter.ToUInt32(rec, 10), Array.Empty<IsoEntry>()));
        }
        return new IsoEntry(name, true, lba, size, children);
    }

    private string DecodeName(ReadOnlySpan<byte> raw)
    {
        string s = _joliet ? Encoding.BigEndianUnicode.GetString(raw) : Encoding.ASCII.GetString(raw);
        int semi = s.IndexOf(';');
        if (semi >= 0) s = s[..semi];
        if (!_joliet && s.EndsWith('.')) s = s[..^1];
        return s;
    }

    public IsoEntry? Find(string path)
    {
        var current = Root;
        foreach (var part in path.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.Children.FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    /// <summary>Kopieert een map (recursief) van de disc naar <paramref name="targetDir"/>.</summary>
    public void ExtractDirectory(IsoEntry dir, string targetDir, Action<string>? onFile = null)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var child in dir.Children)
        {
            var target = Path.Combine(targetDir, child.Name);
            if (child.IsDirectory) { ExtractDirectory(child, target, onFile); continue; }
            ExtractFile(child, target);
            onFile?.Invoke(target);
        }
    }

    public void ExtractFile(IsoEntry file, string target)
    {
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write);
        CopyFile(file, output);
    }

    public void CopyFile(IsoEntry file, Stream output)
    {
        var sector = new byte[DiscImage.SectorDataSize];
        long remaining = file.Size;
        for (long lba = file.Lba; remaining > 0; lba++)
        {
            _disc.ReadSector(lba, sector);
            int n = (int)Math.Min(remaining, sector.Length);
            output.Write(sector, 0, n);
            remaining -= n;
        }
    }
}
