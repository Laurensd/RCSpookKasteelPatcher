using System.Text;

namespace RedCatPatcher.Patching;

/// <summary>
/// De hulp-agent in het menu (knop Help) toont per scherm een ballon met vragen. Een klik op een vraag opende
/// het bijbehorende onderwerp uit Agent\REDCAT.HLP als WinHelp-popup; WinHelp bestaat niet meer op Windows 10/11
/// (er opent alleen een webpagina van Microsoft). De patcher zet de onderwerpen daarom in Agent\RedCat.ini, en de
/// menupatch "menu-agent-help" laat ze zien in een gewoon berichtvenster. Achtergrond: zie docs/TECHNISCH.md, Fix 8.
/// </summary>
public static class AgentHelp
{
    public const string IniName = "RedCat.ini";

    /// <summary>Schrijft Agent\RedCat.ini; geeft het aantal onderwerpen terug.</summary>
    public static int Apply(string gameDir)
    {
        var agent = FindEntry(gameDir, "Agent") ?? throw new PatchException("Map 'Agent' niet gevonden in de spelmap.");
        var hlp = FindEntry(agent, "REDCAT.HLP") ?? throw new PatchException("Agent\\REDCAT.HLP niet gevonden.");
        var ghc = FindEntry(agent, "REDCAT.ghc") ?? throw new PatchException("Agent\\REDCAT.ghc niet gevonden.");

        var names = File.ReadLines(ghc, Encoding.Latin1)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 5 && p[0] == "Global" && p[1] == "Const" && p[3] == "=")
            .Select(p => p[2]);
        var topics = WinHelpFile.ReadTopics(File.ReadAllBytes(hlp), names);

        var ini = new StringBuilder();
        ini.Append("; Helpteksten van de hulp-agent, door de patcher uit REDCAT.HLP gehaald (WinHelp bestaat niet meer).\r\n");
        ini.Append("; Het menu toont [onderwerp] Title en Text; | is een nieuwe regel.\r\n");
        foreach (var (name, topic) in topics)
        {
            ini.Append($"\r\n[{name}]\r\n");
            ini.Append($"Title=\"{topic.Title}\"\r\n");
            ini.Append($"Text=\"{string.Join('|', topic.Lines)}\"\r\n");
        }
        File.WriteAllText(Path.Combine(agent, IniName), ini.ToString(), Encoding.Latin1);
        return topics.Count;
    }

    private static string? FindEntry(string dir, string name) =>
        Directory.EnumerateFileSystemEntries(dir).FirstOrDefault(e => Path.GetFileName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Een helponderwerp: titel en tekstregels (een lege regel scheidt alinea's).</summary>
public sealed record HelpTopic(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// Leest de tekst van onderwerpen uit een WinHelp 3.1/4-bestand (.hlp) zonder compressie, zoals REDCAT.HLP.
/// Opmaak, plaatjes en hyperlinks vallen weg. Indeling volgens "The Windows Help File Format" (M. Winterhoff).
/// </summary>
public static class WinHelpFile
{
    private const int TopicBlockSize = 4096, TopicBlockHeader = 12;

    /// <summary>Onderwerpen per contextnaam (zoals h_Adventure); namen die niet in het bestand staan, ontbreken.</summary>
    public static IReadOnlyList<(string Name, HelpTopic Topic)> ReadTopics(byte[] hlp, IEnumerable<string> contextNames)
    {
        if (hlp.Length < 16 || BitConverter.ToInt32(hlp, 0) != 0x00035F3F)
            throw new PatchException("Geen WinHelp-bestand.");
        var files = ReadBTree(hlp, BitConverter.ToInt32(hlp, 4), (p, page) =>
        {
            int end = Array.IndexOf(hlp, (byte)0, p);
            return (Encoding.Latin1.GetString(hlp, p, end - p), BitConverter.ToInt32(hlp, end + 1), end + 5);
        }).ToDictionary(e => e.Key, e => e.Value);

        var system = File(hlp, files, "|SYSTEM");
        int minor = BitConverter.ToUInt16(system.Array!, system.Offset + 2);
        int flags = BitConverter.ToUInt16(system.Array!, system.Offset + 10);
        if (minor <= 16 || flags != 0 || files.ContainsKey("|Phrases") || files.ContainsKey("|PhrIndex"))
            throw new PatchException("Gecomprimeerd WinHelp-bestand wordt niet ondersteund.");

        if (!files.TryGetValue("|CONTEXT", out int context)) throw new PatchException("WinHelp-bestand mist |CONTEXT.");
        var offsets = ReadBTree(hlp, context, (p, _) => (BitConverter.ToInt32(hlp, p), BitConverter.ToInt32(hlp, p + 4), p + 8))
            .ToDictionary(e => e.Key, e => e.Value);
        var topics = ReadTopicFile(File(hlp, files, "|TOPIC"));

        var result = new List<(string, HelpTopic)>();
        foreach (var name in contextNames)
            if (Hash(name) is { } h && offsets.TryGetValue(h, out var offset) && topics.TryGetValue(offset, out var topic))
                result.Add((name, topic));
        return result;
    }

    /// <summary>
    /// Hash van een contextnaam zoals |CONTEXT die gebruikt (hoofdletterongevoelig). Alleen letters en _, de tekens
    /// die REDCAT.HLP gebruikt; voor andere tekens geeft dit null.
    /// </summary>
    public static int? Hash(string name)
    {
        int h = 0;
        foreach (char ch in name)
        {
            int v = char.ToUpperInvariant(ch) switch
            {
                >= 'A' and <= 'Z' and var c => c - 'A' + 0x11,
                '_' => 0x0D,
                _ => -1,
            };
            if (v < 0) return null;
            h = unchecked(h * 43 + v);
        }
        return h;
    }

    private static ArraySegment<byte> File(byte[] hlp, Dictionary<string, int> files, string name)
    {
        if (!files.TryGetValue(name, out int p)) throw new PatchException($"WinHelp-bestand mist {name}.");
        int used = BitConverter.ToInt32(hlp, p + 4);
        return new ArraySegment<byte>(hlp, p + 9, used);
    }

    /// <summary>Alle sleutels uit de bladpagina's van een B+-boom (bestand op positie <paramref name="file"/>).</summary>
    private static IEnumerable<KeyValuePair<TKey, TValue>> ReadBTree<TKey, TValue>(byte[] hlp, int file,
        Func<int, int, (TKey Key, TValue Value, int Next)> readEntry)
    {
        int h = file + 9;
        if (BitConverter.ToUInt16(hlp, h) != 0x293B) throw new PatchException("Ongeldige B-boom in WinHelp-bestand.");
        int pageSize = BitConverter.ToUInt16(hlp, h + 4);
        int page = BitConverter.ToInt16(hlp, h + 26), levels = BitConverter.ToInt16(hlp, h + 32);
        int pages = h + 38;
        for (int level = 1; level < levels; level++)
            page = BitConverter.ToInt16(hlp, pages + page * pageSize + 4);   // eerste kind = PreviousPage
        while (page != -1)
        {
            int b = pages + page * pageSize, n = BitConverter.ToInt16(hlp, b + 2), p = b + 8;
            for (int i = 0; i < n; i++)
            {
                var (key, value, next) = readEntry(p, page);
                yield return new(key, value);
                p = next;
            }
            page = BitConverter.ToInt16(hlp, b + 6);
        }
    }

    /// <summary>Onderwerpen in |TOPIC per TOPICOFFSET (blok * 0x8000 + tekens sinds het eerste record in dat blok).</summary>
    private static Dictionary<int, HelpTopic> ReadTopicFile(ArraySegment<byte> topic)
    {
        var data = topic.ToArray();
        var result = new Dictionary<int, HelpTopic>();
        static int Raw(int pos) => (pos >> 14) * TopicBlockSize + (pos & 0x3FFF);

        int link = Raw(BitConverter.ToInt32(data, 4)), block = -1, chars = 0, blockStart = 0;
        string? title = null;
        int titleOffset = 0;
        var lines = new List<string>();
        void Flush()
        {
            if (title != null) result[titleOffset] = new HelpTopic(title, Tidy(lines));
            lines.Clear();
        }

        for (int guard = 0; link >= 0 && link + 21 <= data.Length && guard < 100_000; guard++)
        {
            if (link / TopicBlockSize != block) { block = link / TopicBlockSize; blockStart = chars; }
            var record = Gather(data, link, BitConverter.ToInt32(data, link));
            int dataLen2 = BitConverter.ToInt32(record, 4), next = BitConverter.ToInt32(record, 12);
            int dataLen1 = BitConverter.ToInt32(record, 16);
            byte type = record[20];
            var data1 = record.AsSpan(21, dataLen1 - 21).ToArray();
            var data2 = record.AsSpan(dataLen1).ToArray();
            if (type == 0x02)
            {
                Flush();
                title = Encoding.Latin1.GetString(data2).TrimEnd('\0');
                titleOffset = block * 0x8000 + (chars - blockStart);
            }
            else if (type is 0x20 or 0x23)
            {
                if (type == 0x20 && title != null) lines.AddRange(Paragraphs(data1, data2));
                chars += dataLen2;
            }
            if (next == -1) break;
            link = Raw(next);
        }
        Flush();
        return result;
    }

    /// <summary>Een record kan over een blokgrens lopen; sla dan de blokkop van 12 bytes over.</summary>
    private static byte[] Gather(byte[] data, int start, int size)
    {
        var buf = new byte[size];
        for (int copied = 0, p = start; copied < size;)
        {
            int take = Math.Min(TopicBlockSize - p % TopicBlockSize, size - copied);
            Array.Copy(data, p, buf, copied, take);
            copied += take;
            p += take;
            if (copied < size) p += TopicBlockHeader;
        }
        return buf;
    }

    /// <summary>Tekst van een record (type 0x20): strings in LinkData2, opmaakcommando's in LinkData1.</summary>
    private static List<string> Paragraphs(byte[] data1, byte[] data2)
    {
        var r = new Reader(data1);
        r.Long(); r.Word();                         // TopicSize, TopicLength
        r.Byte(); r.Short(); r.UShort();            // onbekend, onbekend, id
        int bits = r.UShort();
        if ((bits & 0x0001) != 0) r.Long();
        foreach (int f in new[] { 0x02, 0x04, 0x08, 0x10, 0x20, 0x40 })
            if ((bits & f) != 0) r.Short();
        if ((bits & 0x0100) != 0) { r.Byte(); r.UShort(); }
        if ((bits & 0x0200) != 0)
            for (int i = r.Short(); i > 0; i--)
                if ((r.Word() & 0x4000) != 0) r.Word();

        var result = new List<string>();
        var line = new StringBuilder();
        var strings = Encoding.Latin1.GetString(data2).Split('\0');
        foreach (var s in strings)
        {
            line.Append(s);
            if (r.AtEnd) break;
            byte cmd = r.Byte();
            switch (cmd)
            {
                case 0xFF: break;
                case 0x80: r.UShort(); break;                                 // lettertype
                case 0x81: result.Add(line.ToString()); line.Clear(); break;  // regeleinde
                case 0x82: result.Add(line.ToString()); result.Add(""); line.Clear(); break;   // alinea
                case 0x83: line.Append(' '); break;                           // tab
                case 0x86 or 0x87 or 0x88:                                    // plaatje
                    byte kind = r.Byte();
                    int size = r.Long();
                    if (kind == 0x22) r.Word();
                    r.Skip(size);
                    break;
                case 0x89 or 0x8B or 0x8C: break;                             // einde hotspot, harde spatie/streepje
                case >= 0xE0 and <= 0xE7: r.Skip(4); break;                   // popup/sprong naar onderwerp
                case 0xC8 or 0xCC or (>= 0xEA and <= 0xEF): r.Skip(r.UShort()); break;   // macro, extern onderwerp
                default: throw new PatchException($"Onbekend opmaakcommando 0x{cmd:X2} in WinHelp-bestand.");
            }
            if (cmd == 0xFF) break;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result;
    }

    /// <summary>Eerste regel (de titel nog eens) eraf, spaties en lege regels opruimen, | mag niet in de tekst.</summary>
    private static List<string> Tidy(List<string> lines)
    {
        var result = new List<string>();
        foreach (var raw in lines.Skip(1))
        {
            var l = string.Join(' ', raw.Replace('|', '/').Replace('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (l.Length == 0 && (result.Count == 0 || result[^1].Length == 0)) continue;
            result.Add(l);
        }
        while (result.Count > 0 && result[^1].Length == 0) result.RemoveAt(result.Count - 1);
        return result;
    }

    private sealed class Reader(byte[] data)
    {
        private int _p;
        public bool AtEnd => _p >= data.Length;
        public byte Byte() => data[_p++];
        public int UShort() { int v = BitConverter.ToUInt16(data, _p); _p += 2; return v; }
        public void Skip(int n) => _p += n;
        // Gecomprimeerde getallen: bit 0 van de eerste byte kiest tussen de korte en de lange vorm.
        public int Word() => (data[_p] & 1) != 0 ? UShort() >> 1 : Byte() >> 1;
        public int Short() => (data[_p] & 1) != 0 ? (UShort() >> 1) - 0x4000 : (Byte() >> 1) - 0x40;
        public int Long()
        {
            if ((data[_p] & 1) == 0) return (UShort() >> 1) - 0x4000;
            int v = (int)(BitConverter.ToUInt32(data, _p) >> 1) - 0x40000000;
            _p += 4;
            return v;
        }
    }
}
