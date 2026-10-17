using System.Text.RegularExpressions;

namespace RedCatPatcher.Disc;

public sealed record CueTrack(int Number, string Mode, string File)
{
    public bool IsData => !Mode.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Minimale .cue-parser: FILE- en TRACK-regels zijn genoeg om de datatrack te vinden.</summary>
public sealed partial class CueSheet
{
    public List<CueTrack> Tracks { get; } = new();

    public static CueSheet Parse(string cuePath)
    {
        var sheet = new CueSheet();
        var dir = Path.GetDirectoryName(Path.GetFullPath(cuePath))!;
        string? currentFile = null;
        foreach (var line in File.ReadAllLines(cuePath))
        {
            var file = FileLine().Match(line);
            if (file.Success)
            {
                var name = file.Groups[1].Success ? file.Groups[1].Value : file.Groups[2].Value;
                currentFile = ResolveFile(dir, name);
                continue;
            }
            var track = TrackLine().Match(line);
            if (track.Success && currentFile != null)
                sheet.Tracks.Add(new CueTrack(int.Parse(track.Groups[1].Value), track.Groups[2].Value, currentFile));
        }
        return sheet;
    }

    // Bestandsnamen in .cue's kloppen niet altijd qua hoofdletters (belangrijk op Linux).
    private static string ResolveFile(string dir, string name)
    {
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct)) return direct;
        var match = Directory.EnumerateFiles(dir)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(name), StringComparison.OrdinalIgnoreCase));
        return match ?? direct;
    }

    [GeneratedRegex("^\\s*FILE\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex FileLine();

    [GeneratedRegex("^\\s*TRACK\\s+(\\d+)\\s+(\\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex TrackLine();
}
