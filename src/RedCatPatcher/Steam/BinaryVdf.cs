using System.Text;

namespace RedCatPatcher.Steam;

/// <summary>
/// Steams binaire KeyValues-formaat (shortcuts.vdf). Elk veld begint met een typebyte,
/// gevolgd door een nul-getermineerde naam en de waarde; 0x08 sluit een map af.
/// Het lezen en schrijven behoudt volgorde en typen, zodat een ongewijzigd bestand byte voor byte gelijk blijft.
/// </summary>
public static class BinaryVdf
{
    private const byte TypeMap = 0x00, TypeString = 0x01, TypeInt32 = 0x02, TypeFloat = 0x03, TypeUInt64 = 0x07, TypeEnd = 0x08;

    public static VdfMap Read(byte[] data)
    {
        // Het bestand is zelf een map zonder naam: "\0shortcuts\0 ... \x08\x08".
        int pos = 0;
        var root = ReadMap(data, ref pos);
        if (pos != data.Length) throw new PatchException($"Onverwachte data aan het eind van het VDF-bestand (positie {pos}).");
        return root;
    }

    public static byte[] Write(VdfMap root)
    {
        using var ms = new MemoryStream();
        WriteMap(ms, root);
        return ms.ToArray();
    }

    private static VdfMap ReadMap(byte[] data, ref int pos)
    {
        var map = new VdfMap();
        while (true)
        {
            if (pos >= data.Length) throw new PatchException("VDF-bestand is afgebroken.");
            byte type = data[pos++];
            if (type == TypeEnd) return map;
            string key = ReadString(data, ref pos);
            object value = type switch
            {
                TypeMap => ReadMap(data, ref pos),
                TypeString => ReadString(data, ref pos),
                TypeInt32 => ReadFixed(data, ref pos, 4, s => BitConverter.ToInt32(s)),
                TypeFloat => ReadFixed(data, ref pos, 4, s => BitConverter.ToSingle(s)),
                TypeUInt64 => ReadFixed(data, ref pos, 8, s => BitConverter.ToUInt64(s)),
                _ => throw new PatchException($"Onbekend VDF-veldtype 0x{type:X2} bij '{key}'."),
            };
            map.Add(key, value);
        }
    }

    private static object ReadFixed(byte[] data, ref int pos, int size, Func<ReadOnlySpan<byte>, object> convert)
    {
        if (pos + size > data.Length) throw new PatchException("VDF-bestand is afgebroken.");
        var value = convert(data.AsSpan(pos, size));
        pos += size;
        return value;
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        int end = Array.IndexOf(data, (byte)0, pos);
        if (end < 0) throw new PatchException("VDF-bestand is afgebroken.");
        var s = Encoding.UTF8.GetString(data, pos, end - pos);
        pos = end + 1;
        return s;
    }

    private static void WriteMap(Stream s, VdfMap map)
    {
        foreach (var (key, value) in map)
        {
            switch (value)
            {
                case VdfMap child: s.WriteByte(TypeMap); WriteString(s, key); WriteMap(s, child); break;
                case string str: s.WriteByte(TypeString); WriteString(s, key); WriteString(s, str); break;
                case int i: s.WriteByte(TypeInt32); WriteString(s, key); s.Write(BitConverter.GetBytes(i)); break;
                case float f: s.WriteByte(TypeFloat); WriteString(s, key); s.Write(BitConverter.GetBytes(f)); break;
                case ulong u: s.WriteByte(TypeUInt64); WriteString(s, key); s.Write(BitConverter.GetBytes(u)); break;
                default: throw new ArgumentException($"VDF-waarde van type {value.GetType().Name} wordt niet ondersteund ({key}).");
            }
        }
        s.WriteByte(TypeEnd);
    }

    private static void WriteString(Stream s, string value)
    {
        s.Write(Encoding.UTF8.GetBytes(value));
        s.WriteByte(0);
    }
}

/// <summary>Een VDF-map: sleutels in volgorde, waarde is een string, int, float, ulong of geneste map.</summary>
public sealed class VdfMap : List<KeyValuePair<string, object>>
{
    public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));

    /// <summary>Sleutels zijn in Steam hoofdletterongevoelig (bijv. "AppName" en "appname" komen allebei voor).</summary>
    public int IndexOf(string key) => FindIndex(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public object? this[string key]
    {
        get { int i = IndexOf(key); return i < 0 ? null : this[i].Value; }
        set
        {
            int i = IndexOf(key);
            if (value == null) { if (i >= 0) RemoveAt(i); }
            else if (i < 0) Add(key, value);
            else this[i] = new KeyValuePair<string, object>(this[i].Key, value);
        }
    }

    public VdfMap? Map(string key) => this[key] as VdfMap;
    public string? String(string key) => this[key] as string;

    /// <summary>Geeft de submap met deze naam, en maakt hem aan als hij nog niet bestaat.</summary>
    public VdfMap GetOrAddMap(string key)
    {
        if (Map(key) is { } existing) return existing;
        var map = new VdfMap();
        this[key] = map;
        return map;
    }
}
