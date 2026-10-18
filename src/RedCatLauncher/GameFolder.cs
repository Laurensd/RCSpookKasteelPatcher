using System.ComponentModel;
using System.Diagnostics;
using RedCatPatcher;
using RedCatPatcher.Patching;

namespace RedCatLauncher;

/// <summary>
/// De spelmap naast de launcher: de keuze uit RedCatLauncher.ini lezen en bewaren, toepassen vanuit de basis als die
/// afwijkt van wat er nu in de bestanden staat, en RedCat.exe starten (of met een startlevel direct het spel).
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

    public TestOptions Testing => ini?.Testing ?? new TestOptions();

    /// <summary>Wat er toegepast wordt: de keuze, met de logs aan als er een [Debug]-schakelaar aan staat.</summary>
    Choice Effective => Testing.Effective(Wanted);

    /// <summary>De gekozen instellingen of een nieuwere fix (<see cref="Basis.Revision"/>) staan nog niet in de spelbestanden.</summary>
    public bool NeedsApply => CanChoose && (ini!.Applied != Effective || ini.Revision < Basis.Revision);

    /// <summary>Alleen een nieuwere fix staat nog niet in de spelbestanden; de keuze zelf is al toegepast.</summary>
    public bool NeedsFixUpdate => NeedsApply && ini!.Applied == Effective;

    /// <summary>Bewaart de keuze in [Keuze] en de testhulp in [Testen]; toegepast wordt pas bij het spelen.</summary>
    public void Save(Choice wanted, TestOptions testing)
    {
        if (!CanChoose) return;
        ini = ini! with { Wanted = wanted, Testing = testing };
        ini.Write(dir);
    }

    /// <summary>
    /// Bouwt de spelbestanden opnieuw op uit de basis als de keuze verschilt van [Toegepast], en zet de
    /// [Debug]-schakelaars in AdamWin.ini (dat schrijft alleen wat anders is).
    /// </summary>
    public void ApplyIfNeeded()
    {
        if (!CanChoose) return;
        if (NeedsApply)
        {
            var effective = Effective;
            Basis.Apply(dir, effective, ini!.BasisSha256);
            ini = ini with { Applied = effective, Revision = Basis.Revision };
            ini.Write(dir);
            Log("Settings applied from the base copy");
        }
        Testing.Apply(dir);
    }

    /// <summary>RedCat.exe zoals het laatst gestart is.</summary>
    public Process? Started { get; private set; }

    /// <summary>Direct in een level gestart (tabblad Testing), zonder menu.</summary>
    bool startedLevel;

    /// <summary>
    /// Start RedCat.exe, of met een startlevel direct het spel, net als <c>RedCatPatcher --start-level</c>
    /// (<see cref="StartLevel"/>).
    /// </summary>
    public void Start()
    {
        if (!HasGame) throw new PatchException($"{GameExe} not found in {dir}. Put the launcher in the game folder, next to {GameExe}.");
        if (CanChoose && Testing.StartLevel is { } level)
        {
            StartLevel.Prepare(dir, level);
            Started = StartLevel.Launch(dir);
            startedLevel = true;
            Log($"Started level {level} ({StartLevel.Levels[level - 1].Name}) directly");
            return;
        }
        Started = Process.Start(new ProcessStartInfo(Path.Combine(dir, GameExe)) { WorkingDirectory = dir, UseShellExecute = false });
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

    /// <summary>Na het starten: de controller in de menu's tot RedCat.exe stopt, als de speler dat wil.</summary>
    public void RunMenuController()
    {
        // Zonder menu (startlevel) is er niets te doen
        if (Started != null && !startedLevel && Wanted.MenuController) MenuPad.Run(Started);
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
