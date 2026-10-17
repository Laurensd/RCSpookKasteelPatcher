using System.Text;

namespace RedCatPatcher.Steam;

/// <summary>
/// Steams tekst-KeyValues (config.vdf, loginusers.vdf). Strings worden ruw bewaard, inclusief
/// escapes als <c>\\</c>, zodat waarden die we niet aanraken precies zo teruggeschreven worden.
/// Gebruik <see cref="Escape"/> voor nieuwe waarden en <see cref="Unescape"/> om een waarde te lezen.
/// </summary>
public static class TextVdf
{
    public static VdfMap Parse(string text)
    {
        int pos = 0;
        var root = ParseMap(text, ref pos, topLevel: true);
        return root;
    }

    public static string Write(VdfMap root)
    {
        var sb = new StringBuilder();
        WriteMap(sb, root, 0);
        return sb.ToString();
    }

    public static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string Unescape(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                char c = raw[++i];
                sb.Append(c switch { 'n' => '\n', 't' => '\t', _ => c });
            }
            else sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    private static VdfMap ParseMap(string text, ref int pos, bool topLevel)
    {
        var map = new VdfMap();
        while (true)
        {
            var key = NextToken(text, ref pos);
            if (key == null)
            {
                if (topLevel) return map;
                throw new PatchException("VDF-bestand is afgebroken (ontbrekende '}').");
            }
            if (key is { Quoted: false, Value: "}" })
            {
                if (topLevel) throw new PatchException("VDF-bestand bevat een '}' te veel.");
                return map;
            }
            var value = NextToken(text, ref pos) ?? throw new PatchException($"VDF-bestand is afgebroken na '{key.Value.Value}'.");
            if (value is { Quoted: false, Value: "{" })
                map.Add(key.Value.Value, ParseMap(text, ref pos, topLevel: false));
            else
                map.Add(key.Value.Value, value.Value);
        }
    }

    private static (string Value, bool Quoted)? NextToken(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            if (c is '{' or '}') { pos++; return (c.ToString(), false); }
            if (c == '"')
            {
                int start = ++pos;
                while (pos < text.Length && text[pos] != '"')
                    pos += text[pos] == '\\' ? 2 : 1;
                if (pos >= text.Length) throw new PatchException("VDF-bestand bevat een niet-afgesloten string.");
                return (text[start..pos++], true);
            }
            int begin = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"')) pos++;
            return (text[begin..pos], false);
        }
        return null;
    }

    private static void WriteMap(StringBuilder sb, VdfMap map, int depth)
    {
        var indent = new string('\t', depth);
        foreach (var (key, value) in map)
        {
            if (value is VdfMap child)
            {
                sb.Append(indent).Append('"').Append(key).Append("\"\n");
                sb.Append(indent).Append("{\n");
                WriteMap(sb, child, depth + 1);
                sb.Append(indent).Append("}\n");
            }
            else
                sb.Append(indent).Append('"').Append(key).Append("\"\t\t\"").Append(value).Append("\"\n");
        }
    }
}
