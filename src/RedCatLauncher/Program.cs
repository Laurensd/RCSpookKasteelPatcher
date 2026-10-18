using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace RedCatLauncher;

/// <summary>
/// Launcher naast RedCat.exe: kies breedbeeld, besturing, beeld en logs en start het spel. Zie docs/LAUNCHER.md.
///   --spelen     Zonder venster: keuze toepassen en het spel starten (Steam Deck Game Mode). Bij een fout verschijnt
///                alsnog het venster met de melding.
///   --software   Zonder GPU tekenen, als het venster leeg of zwart blijft.
///   --map &lt;map&gt;  Een andere spelmap dan die van de launcher.
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var dir = AppContext.BaseDirectory;
        int at = Array.IndexOf(args, "--map");
        if (at >= 0 && at + 1 < args.Length) dir = Path.GetFullPath(args[at + 1]);
        var game = new GameFolder(dir);

        string? error = null;
        if (args.Contains("--spelen") || args.Contains("--play"))
        {
            error = game.Play();
            if (error == null)
            {
                game.RunMenuController();
                return 0;
            }
        }

        Win32RenderingMode[] rendering = args.Contains("--software")
            ? [Win32RenderingMode.Software]
            : [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software];
        return AppBuilder.Configure(() => new App(game, error))
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = rendering })
            .StartWithClassicDesktopLifetime(args);
    }
}

sealed class App(GameFolder game, string? error) : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(game, error);
        base.OnFrameworkInitializationCompleted();
    }
}
