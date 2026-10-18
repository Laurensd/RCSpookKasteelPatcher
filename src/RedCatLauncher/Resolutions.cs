using Avalonia;
using RedCatPatcher.Patching;

namespace RedCatLauncher;

/// <summary>
/// De keuzes voor Resolution, zoals in een modern spel: een lijst met vaste resoluties die bij de beeldverhouding passen
/// en op het scherm passen. Schermvullend is het de resolutie waarop dgVoodoo rendert (daarna geschaald naar het
/// scherm), in een venster de venstergrootte (een veelvoud van de spelresolutie).
/// </summary>
static class Resolutions
{
    static readonly (int W, int H)[] Standard4x3 = [(2880, 2160), (1920, 1440), (1600, 1200), (1440, 1080), (1280, 960), (1024, 768), (960, 720)];
    static readonly (int W, int H)[] Standard16x9 = [(3840, 2160), (3200, 1800), (2560, 1440), (1920, 1080), (1600, 900), (1280, 720)];
    static readonly (int W, int H)[] Standard16x10 = [(2560, 1600), (1920, 1200), (1680, 1050), (1440, 900), (1280, 800)];

    public static IEnumerable<(string Text, string Value)> For(Choice c, PixelSize screen)
    {
        var (gameWidth, gameHeight) = c.Widescreen is { } w ? (w.Width, w.Height) : (640, 480);
        if (c.Display.Mode == DisplayMode.Window)
        {
            yield return ($"{gameWidth}×{gameHeight} (original)", "unforced");
            for (int n = 2; n <= 4; n++)
                if (n == 2 || (gameWidth * n <= screen.Width && gameHeight * n <= screen.Height))
                    yield return ($"{gameWidth * n}×{gameHeight * n}", $"{n}x");
            yield break;
        }

        var (aspectWidth, aspectHeight, standard) = c.Widescreen?.Name switch
        {
            "16:9" => (16, 9, Standard16x9),
            "16:10" => (16, 10, Standard16x10),
            _ => (4, 3, Standard4x3),
        };
        // Zoals dgVoodoo's "max": de grootste resolutie met deze beeldverhouding die op het scherm past
        int nativeHeight = Math.Min(screen.Height, screen.Width * aspectHeight / aspectWidth);
        int nativeWidth = nativeHeight * aspectWidth / aspectHeight;
        yield return ($"Native ({nativeWidth}×{nativeHeight})", DgVoodoo.DefaultResolution);
        foreach (var (width, height) in standard)
            if (width <= screen.Width && height <= screen.Height && height != nativeHeight)
                yield return ($"{width}×{height}", $"{width}x{height}");
        yield return ($"Original ({gameWidth}×{gameHeight})", "unforced");
    }

    /// <summary>De resolutie na het wisselen tussen venster en schermvullend, waar dezelfde waarde iets anders betekent.</summary>
    public static string Default(DisplayMode mode) =>
        mode == DisplayMode.Window ? DisplayOptions.DefaultWindowResolution : DgVoodoo.DefaultResolution;

    public static IEnumerable<(string Text, int Value)> RenderScales(Choice c) =>
        c.Display.Mode == DisplayMode.Window ? [("Not available when windowed", c.Display.RenderScale)]
        : c.Display.GlideResolution == "unforced" ? [("Not available at Original", c.Display.RenderScale)]
        : [("Off", 1), ("2× supersampling", 2), ("3× supersampling", 3), ("4× supersampling", 4)];
}
