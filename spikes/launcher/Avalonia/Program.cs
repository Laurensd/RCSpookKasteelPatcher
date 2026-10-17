using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace LauncherSpike;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // --software: zonder ANGLE/Direct3D tekenen, voor als het venster onder Proton zwart of leeg blijft
        Win32RenderingMode[] rendering = args.Contains("--software")
            ? [Win32RenderingMode.Software]
            : [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software];
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = rendering })
            .StartWithClassicDesktopLifetime(args);
    }
}

sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

sealed class MainWindow : Window
{
    readonly Button[] buttons;
    readonly TextBlock status = new() { HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    readonly Pad pad = new();
    int focus;

    public MainWindow()
    {
        Title = "RedCat launcher-proef (Avalonia)";
        Width = 640;
        Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 22;

        var play = new Button { Content = "Spelen", IsDefault = true };
        var quit = new Button { Content = "Afsluiten", IsCancel = true };
        buttons = [play, quit];
        for (var i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i];
            var index = i;
            b.Width = 260;
            b.Height = 70;
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            b.GotFocus += (_, _) => focus = index;
        }
        play.Click += (_, _) => Play();
        quit.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Spacing = 20,
            Margin = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { play, quit, status },
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => OnPad(pad.Poll());
        timer.Start();

        Opened += (_, _) =>
        {
            play.Focus(NavigationMethod.Tab);
            var line = $"Avalonia opgestart in {Spike.Elapsed()} ({Spike.Describe()})";
            status.Text = line;
            Spike.Log(line);
            if (Spike.Measure) Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        };
    }

    void OnPad(Pad.Action action)
    {
        switch (action)
        {
            case Pad.Action.Previous: MoveFocus(-1); break;
            case Pad.Action.Next: MoveFocus(1); break;
            case Pad.Action.Activate: if (focus == 0) Play(); else Close(); break;
            case Pad.Action.Back: Close(); break;
        }
    }

    void MoveFocus(int delta) => buttons[(focus + delta + buttons.Length) % buttons.Length].Focus(NavigationMethod.Tab);

    void Play()
    {
        var error = Spike.StartGame();
        if (error is null) Close();
        else status.Text = error;
    }
}
