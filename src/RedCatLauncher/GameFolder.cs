using System.ComponentModel;
using System.Diagnostics;
using RedCatPatcher;
using RedCatPatcher.Patching;

namespace RedCatLauncher;

/// <summary>
/// De spelmap naast de launcher: de keuze uit RedCatLauncher.ini lezen en bewaren, toepassen vanuit de basis als die
/// afwijkt van wat er nu in de bestanden staat, en RedCat.exe starten.
/// </summary>
sealed class GameFolder(string dir)
{
    public const string GameExe = "RedCat.exe";

    public string Dir => dir;
    LauncherIni? ini = LauncherIni.Read(dir);

    /// <summary>Gepatcht met een patcher die de basis en het keuzebestand schrijft.</summary>
    public bool CanChoose => ini?.Wanted != null && Directory.Exists(Basis.DirOf(dir));

    public bool HasGame => File.Exists(Path.Combine(dir, GameExe));

    public Choice Wanted => ini?.Wanted ?? new Choice();

    /// <summary>De gekozen instellingen of een nieuwere fix (<see cref="Basis.Revision"/>) staan nog niet in de spelbestanden.</summary>
    public bool NeedsApply => CanChoose && (ini!.Applied != ini.Wanted || ini.Revision < Basis.Revision);

    /// <summary>Alleen een nieuwere fix staat nog niet in de spelbestanden; de keuze zelf is al toegepast.</summary>
    public bool NeedsFixUpdate => NeedsApply && ini!.Applied == ini.Wanted;

    /// <summary>Bewaart de keuze in [Keuze]; toegepast wordt pas bij het spelen.</summary>
    public void Save(Choice wanted)
    {
        if (!CanChoose) return;
        ini = ini! with { Wanted = wanted };
        ini.Write(dir);
    }

    /// <summary>Bouwt de spelbestanden opnieuw op uit de basis als de keuze verschilt van [Toegepast].</summary>
    public void ApplyIfNeeded()
    {
        if (!NeedsApply) return;
        Basis.Apply(dir, ini!.Wanted!, ini.BasisSha256);
        ini = ini with { Applied = ini.Wanted, Revision = Basis.Revision };
        ini.Write(dir);
        Log("Settings applied from the base copy");
    }

    public void Start()
    {
        if (!HasGame) throw new PatchException($"{GameExe} not found in {dir}. Put the launcher in the game folder, next to {GameExe}.");
        Process.Start(new ProcessStartInfo(Path.Combine(dir, GameExe)) { WorkingDirectory = dir, UseShellExecute = false });
    }

    /// <summary>Toepassen en starten. Geeft een foutmelding terug, of null als het spel gestart is.</summary>
    public string? Play()
    {
        try
        {
            ApplyIfNeeded();
            Start();
            return null;
        }
        catch (Exception e) when (IsExpected(e))
        {
            Log($"Error: {e.Message}");
            return e.Message;
        }
    }

    public static bool IsExpected(Exception e) => e is PatchException or IOException or UnauthorizedAccessException or Win32Exception;

    /// <summary>Alleen toepassingen en fouten, voor wie problemen zoekt (bijv. onder Proton).</summary>
    public void Log(string line)
    {
        try { File.AppendAllText(Path.Combine(dir, "RedCatLauncher.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
