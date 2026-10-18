using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RedCatPatcher.Install;

/// <summary>
/// Schrijft een Windows-snelkoppeling (.lnk, formaat MS-SHLLINK) zonder COM: een header, de IDList van het doel,
/// een LinkInfo-blok met het lokale pad, en de strings voor omschrijving, werkmap en pictogram.
/// Geen IShellLink, omdat COM-interop in een getrimde single-file build niet beschikbaar is.
/// De IDList komt van SHParseDisplayName (gewone P/Invoke); zonder IDList negeert de shell het doel.
/// </summary>
public static class ShellLink
{
    private static readonly Guid LinkClsid = new("00021401-0000-0000-C000-000000000046");

    [Flags]
    private enum LinkFlags : uint
    {
        HasLinkTargetIdList = 0x01, HasLinkInfo = 0x02, HasName = 0x04, HasWorkingDir = 0x10, HasIconLocation = 0x40, IsUnicode = 0x80,
    }

    public static byte[] Create(string target, string workingDir, string iconPath, string description, byte[]? idList = null)
    {
        if (!IsAbsoluteWindowsPath(target)) throw new ArgumentException($"Doel van een snelkoppeling moet een absoluut pad zijn: {target}");

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode);

        // ShellLinkHeader (76 bytes)
        w.Write(0x4C);
        w.Write(LinkClsid.ToByteArray());
        var flags = LinkFlags.HasLinkInfo | LinkFlags.HasName | LinkFlags.HasWorkingDir | LinkFlags.HasIconLocation | LinkFlags.IsUnicode;
        if (idList != null) flags |= LinkFlags.HasLinkTargetIdList;
        w.Write((uint)flags);
        w.Write(0x20u);           // FileAttributes: FILE_ATTRIBUTE_ARCHIVE
        w.Write(new byte[24]);    // aanmaak-, toegangs- en wijzigingstijd: onbekend
        w.Write(0u);              // FileSize
        w.Write(0);               // IconIndex
        w.Write(1u);              // ShowCommand: SW_SHOWNORMAL
        w.Write((ushort)0);       // HotKey
        w.Write(new byte[10]);    // gereserveerd

        if (idList != null)
        {
            w.Write((ushort)idList.Length);
            w.Write(idList);
        }
        WriteLinkInfo(w, target);

        // StringData, in de volgorde die de specificatie voorschrijft
        WriteCountedString(w, description);
        WriteCountedString(w, workingDir);
        WriteCountedString(w, iconPath);

        w.Write(0u); // TerminalBlock: geen ExtraData
        return ms.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static void Write(string lnkPath, string target, string workingDir, string iconPath, string description) =>
        File.WriteAllBytes(lnkPath, Create(target, workingDir, iconPath, description, GetIdList(target)));

    /// <summary>De IDList van een bestaand bestand, zoals de shell hem zelf in een .lnk zet (inclusief de afsluitende 0x0000).</summary>
    [SupportedOSPlatform("windows")]
    public static byte[] GetIdList(string path)
    {
        int hr = SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        if (hr != 0) throw new IOException($"Kan geen snelkoppeling maken naar {path} (SHParseDisplayName 0x{hr:X8}).");
        try
        {
            var bytes = new byte[ILGetSize(pidl)]; // inclusief de afsluitende 0x0000, die ook in de .lnk hoort
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(IntPtr pidl);

    /// <summary>LinkInfo met een VolumeID (vaste schijf) en het pad in zowel ANSI als Unicode.</summary>
    private static void WriteLinkInfo(BinaryWriter w, string target)
    {
        const int headerSize = 0x24;           // inclusief de Unicode-offsets
        byte[] volumeId = [.. BitConverter.GetBytes(17), .. BitConverter.GetBytes(3), .. BitConverter.GetBytes(0), .. BitConverter.GetBytes(16), 0];
        byte[] basePathAnsi = [.. target.Select(c => c < 0x80 ? (byte)c : (byte)'?'), 0];
        byte[] suffixAnsi = [0];
        byte[] basePathUnicode = [.. Encoding.Unicode.GetBytes(target), 0, 0];
        byte[] suffixUnicode = [0, 0];

        int volumeIdOffset = headerSize;
        int basePathOffset = volumeIdOffset + volumeId.Length;
        int suffixOffset = basePathOffset + basePathAnsi.Length;
        int basePathUnicodeOffset = suffixOffset + suffixAnsi.Length;
        int suffixUnicodeOffset = basePathUnicodeOffset + basePathUnicode.Length;
        int size = suffixUnicodeOffset + suffixUnicode.Length;

        w.Write(size);
        w.Write(headerSize);
        w.Write(1);                   // LinkInfoFlags: VolumeIDAndLocalBasePath
        w.Write(volumeIdOffset);
        w.Write(basePathOffset);
        w.Write(0);                   // CommonNetworkRelativeLinkOffset
        w.Write(suffixOffset);
        w.Write(basePathUnicodeOffset);
        w.Write(suffixUnicodeOffset);
        w.Write(volumeId);
        w.Write(basePathAnsi);
        w.Write(suffixAnsi);
        w.Write(basePathUnicode);
        w.Write(suffixUnicode);
    }

    /// <summary>
    /// Het doel van een bestaande .lnk uit het LinkInfo-blok (lokaal pad plus achtervoegsel, Unicode als dat er is),
    /// of null als de snelkoppeling geen lokaal pad heeft of geen geldige .lnk is.
    /// </summary>
    public static string? ReadTarget(byte[] lnk)
    {
        try
        {
            if (lnk.Length < 0x4C || BitConverter.ToInt32(lnk, 0) != 0x4C || new Guid(lnk.AsSpan(4, 16)) != LinkClsid) return null;
            var flags = (LinkFlags)BitConverter.ToUInt32(lnk, 0x14);
            int pos = 0x4C;
            if (flags.HasFlag(LinkFlags.HasLinkTargetIdList)) pos += 2 + BitConverter.ToUInt16(lnk, pos);
            if (!flags.HasFlag(LinkFlags.HasLinkInfo)) return null;

            int headerSize = BitConverter.ToInt32(lnk, pos + 4);
            if ((BitConverter.ToInt32(lnk, pos + 8) & 1) == 0) return null; // geen VolumeIDAndLocalBasePath
            if (headerSize >= 0x24 && BitConverter.ToInt32(lnk, pos + 0x1C) is > 0 and var basePathUnicode)
                return UnicodeAt(lnk, pos + basePathUnicode) + UnicodeAt(lnk, pos + BitConverter.ToInt32(lnk, pos + 0x20));
            return AnsiAt(lnk, pos + BitConverter.ToInt32(lnk, pos + 0x10)) + AnsiAt(lnk, pos + BitConverter.ToInt32(lnk, pos + 0x18));
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private static string UnicodeAt(byte[] data, int offset)
    {
        int end = offset;
        while (data[end] != 0 || data[end + 1] != 0) end += 2;
        return Encoding.Unicode.GetString(data, offset, end - offset);
    }

    private static string AnsiAt(byte[] data, int offset) =>
        Encoding.Latin1.GetString(data, offset, Array.IndexOf(data, (byte)0, offset) - offset);

    private static void WriteCountedString(BinaryWriter w, string value)
    {
        w.Write((ushort)value.Length);
        w.Write(Encoding.Unicode.GetBytes(value));
    }

    private static bool IsAbsoluteWindowsPath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
}
