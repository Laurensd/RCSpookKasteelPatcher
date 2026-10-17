namespace RedCatPatcher.Disc;

/// <summary>
/// Leest 2048-byte datasectoren uit een disc-image: .cue/.bin (raw 2352/2336) of .iso (2048).
/// </summary>
public sealed class DiscImage : IDisposable
{
    public const int SectorDataSize = 2048;

    private readonly FileStream _stream;
    private readonly int _sectorSize;
    private readonly int _defaultDataOffset;
    private readonly byte[] _raw;

    public string DataFile { get; }
    public string Description { get; }

    private DiscImage(string dataFile, int sectorSize, int dataOffset, string description)
    {
        DataFile = dataFile;
        Description = description;
        _sectorSize = sectorSize;
        _defaultDataOffset = dataOffset;
        _raw = new byte[sectorSize];
        _stream = new FileStream(dataFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
    }

    public static DiscImage Open(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cue" => OpenCue(path),
            ".iso" => new DiscImage(path, 2048, 0, "ISO (2048 bytes/sector)"),
            ".bin" or ".img" => OpenRawBin(path),
            _ => throw new PatchException($"Onbekend bestandstype '{ext}'. Gebruik een .cue, .bin of .iso bestand."),
        };
    }

    private static DiscImage OpenCue(string cuePath)
    {
        var cue = CueSheet.Parse(cuePath);
        var track = cue.Tracks.FirstOrDefault(t => t.IsData)
            ?? throw new PatchException("De .cue bevat geen datatrack.");
        var (size, offset) = track.Mode.ToUpperInvariant() switch
        {
            "MODE1/2048" => (2048, 0),
            "MODE1/2352" => (2352, 16),
            "MODE2/2352" => (2352, 24),
            "MODE2/2336" => (2336, 8),
            _ => throw new PatchException($"Tracktype '{track.Mode}' wordt niet ondersteund."),
        };
        if (!File.Exists(track.File))
            throw new PatchException($"Het bestand '{Path.GetFileName(track.File)}' uit de .cue is niet gevonden naast de .cue.");
        return new DiscImage(track.File, size, offset, $"{Path.GetFileName(track.File)} ({track.Mode})");
    }

    private static DiscImage OpenRawBin(string binPath)
    {
        // Zonder .cue: herken raw 2352-sectoren aan het sync-patroon, anders behandelen als 2048.
        Span<byte> head = stackalloc byte[16];
        using (var fs = File.OpenRead(binPath)) fs.ReadExactly(head);
        bool raw = head[0] == 0 && head[11] == 0 && head[1..11].ToArray().All(b => b == 0xFF);
        if (!raw) return new DiscImage(binPath, 2048, 0, $"{Path.GetFileName(binPath)} (2048)");
        int offset = head[15] == 2 ? 24 : 16;
        return new DiscImage(binPath, 2352, offset, $"{Path.GetFileName(binPath)} (raw MODE{head[15]}/2352)");
    }

    public void ReadSector(long lba, Span<byte> destination)
    {
        _stream.Position = lba * _sectorSize;
        _stream.ReadExactly(_raw);
        int offset = _defaultDataOffset;
        if (_sectorSize == 2352)
        {
            // Mode-byte in de sectorheader bepaalt waar de data staat (Mode 1: 16, Mode 2 Form 1: 24).
            offset = _raw[15] == 1 ? 16 : 24;
        }
        _raw.AsSpan(offset, SectorDataSize).CopyTo(destination);
    }

    public byte[] ReadSectors(long lba, int count)
    {
        var buffer = new byte[count * SectorDataSize];
        for (int i = 0; i < count; i++)
            ReadSector(lba + i, buffer.AsSpan(i * SectorDataSize, SectorDataSize));
        return buffer;
    }

    public void Dispose() => _stream.Dispose();
}
