using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using RedCatPatcher.Patching;

namespace RedCatLauncher;

/// <summary>
/// Tabblad Spelen: één regel per instelling, te bedienen met muis, toetsenbord of controller.
/// Omhoog/omlaag kiest een regel, links/rechts (of klikken, A) wisselt de waarde, B/Esc sluit, Start speelt.
/// De keuze komt meteen in RedCatLauncher.ini [Keuze]; toegepast wordt pas bij Spelen.
/// </summary>
sealed class MainWindow : Window
{
    static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xE8, 0x8A, 0x1A));
    static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    static readonly IBrush Selected = new SolidColorBrush(Color.FromRgb(0x5C, 0x3A, 0x10));
    static readonly IBrush Error = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x6E));

    readonly GameFolder game;
    readonly List<OptionRow> rows = [];
    readonly Button play, quit;
    readonly List<Button> focusables;
    readonly TextBlock help = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Dim, MinHeight = 36 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    readonly Pad pad = new();
    Choice choice;
    int focus;
    bool busy;

    public MainWindow(GameFolder game, string? startupError)
    {
        this.game = game;
        choice = game.Wanted;
        Title = "RedCat Spookkasteel";
        Width = 760;
        Height = 760;
        MinWidth = 520;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 18;
        Styles.Add(FocusStyle());

        if (game.CanChoose) AddRows();

        play = MakeButton("Spelen", 220);
        quit = MakeButton("Afsluiten", 160);
        play.Click += (_, _) => Play();
        quit.Click += (_, _) => Close();
        focusables = [.. rows.Select(r => r.Button), play, quit];
        for (int i = 0; i < focusables.Count; i++)
        {
            int index = i;
            focusables[i].GotFocus += (_, _) => OnFocus(index);
        }

        var list = new StackPanel { Spacing = 4 };
        foreach (var row in rows) list.Children.Add(row.Button);
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);

        Content = new DockPanel
        {
            Margin = new Thickness(24, 12),
            Children =
            {
                Dock(new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 10),
                    Children =
                    {
                        new TextBlock { Text = "RedCat Spookkasteel", FontSize = 30, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = $"Spelen · launcher {version}", Foreground = Dim, FontSize = 15 },
                    },
                }, Avalonia.Controls.Dock.Top),
                Dock(new StackPanel
                {
                    Margin = new Thickness(0, 10, 0, 0),
                    Spacing = 8,
                    Children =
                    {
                        help,
                        status,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { play, quit } },
                        new TextBlock
                        {
                            Text = "Pijltjes of d-pad: kiezen en wisselen · Enter/A: wisselen of spelen · Start: spelen · Esc/B: sluiten",
                            FontSize = 13, Foreground = Dim, TextWrapping = TextWrapping.Wrap,
                        },
                    },
                }, Avalonia.Controls.Dock.Bottom),
                new ScrollViewer { Content = list },
            },
        };

        if (startupError != null) ShowError(startupError);
        else if (!game.HasGame) ShowError($"{GameFolder.GameExe} niet gevonden in {game.Dir}. Zet de launcher in de spelmap, naast {GameFolder.GameExe}.");
        else if (!game.CanChoose)
            ShowStatus("Deze installatie is gepatcht met een oudere patcher, dus de instellingen zijn hier niet te kiezen. " +
                "Patch opnieuw met de disc (spelers en scores blijven bewaard). Spelen start het spel zoals het nu is.");
        else UpdatePending();

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => OnPad(pad.Poll());
        timer.Start();
        Closed += (_, _) => timer.Stop();
        Opened += (_, _) =>
        {
            GameIcon.Apply(this, Path.Combine(game.Dir, GameFolder.GameExe));
            play.Focus(NavigationMethod.Directional);
        };
    }

    void AddRows()
    {
        var c = choice;
        rows.Add(OptionRow.Create("Beeldverhouding",
            "Breedbeeld: een breder blikveld en een HUD dat niet wordt uitgerekt. 16:10 past op de Steam Deck.",
            [("4:3 (origineel)", (Widescreen?)null), ("16:9 breedbeeld", Widescreen.Wide16x9), ("16:10 breedbeeld", Widescreen.Wide16x10)],
            x => x.Widescreen, (x, v) => x with { Widescreen = v }, c));
        rows.Add(OptionRow.Create("Toetsenbord",
            "Standaardtoetsen voor nieuwe spelers en de knop Standaard bij Instellingen. Bestaande spelers houden hun eigen toetsen.",
            [("Pijltjestoetsen", false), ("WASD", true)],
            x => x.Wasd, (x, v) => x with { Wasd = v }, c));
        rows.Add(OptionRow.Create("Controller",
            "Gamepad: A springen, B ontwijken, X schieten, LB/RB wapen, Back scores, Start menu. Alleen in het spel; het menu blijft muis en toetsenbord.",
            [("Alleen de stick", false), ("Gamepad-indeling", true)],
            x => x.Controller, (x, v) => x with { Controller = v }, c));
        rows.Add(OptionRow.Create("Weergave",
            "Randloos werkt het best met alt-tab en onder Proton. Een venster is twee keer de spelresolutie.",
            [("Randloos schermvullend", DisplayMode.Borderless), ("Volledig scherm", DisplayMode.Fullscreen), ("Venster", DisplayMode.Window)],
            x => x.Display.Mode, (x, v) => x with { Display = x.Display with { Mode = v } }, c));
        rows.Add(OptionRow.Create("Renderresolutie",
            "Hoe scherp dgVoodoo2 het spel tekent. Automatisch: zo groot als het scherm, in een venster 2x.",
            [("Automatisch", (string?)null), ("Zo groot als het scherm", "max"), ("2x", "2x"), ("3x", "3x"), ("4x", "4x"),
             ("Bureaublad", "desktop"), ("Origineel (geen schaling)", "unforced")],
            x => x.Display.Resolution, (x, v) => x with { Display = x.Display with { Resolution = v } }, c));
        rows.Add(OptionRow.Create("Anti-aliasing",
            "Gladde randen (MSAA). Hoger kost meer van de videokaart.",
            DgVoodoo.AntialiasingValues.Select(v => (v == "off" ? "Uit" : v, v)),
            x => x.Display.Antialiasing, (x, v) => x with { Display = x.Display with { Antialiasing = v } }, c));
        rows.Add(OptionRow.Create("Verticale synchronisatie",
            "Uit kan beeldscheuren geven.",
            [("Aan", true), ("Uit", false)],
            x => x.Display.VSync, (x, v) => x with { Display = x.Display with { VSync = v } }, c));
        rows.Add(OptionRow.Create("Fps-limiet",
            "Maximaal aantal beelden per seconde. Geen: alleen de verticale synchronisatie.",
            new[] { 0, 30, 40, 60, 90, 120, 144 }.Select(v => (v == 0 ? "Geen" : v.ToString(), v)),
            x => x.Display.FpsLimit, (x, v) => x with { Display = x.Display with { FpsLimit = v } }, c));
        rows.Add(OptionRow.Create("CRT-effect",
            "Scanlines en een licht gebold beeld, zoals een oude monitor. Alleen schermvullend te zien.",
            [("Uit", false), ("Aan", true)],
            x => x.Display.Crt, (x, v) => x with { Display = x.Display with { Crt = v } }, c));
        rows.Add(OptionRow.Create("Logbestanden",
            "Logbestanden van spel en menu in de spelmap, voor het zoeken naar problemen.",
            [("Uit", false), ("Aan", true)],
            x => x.DebugLogs, (x, v) => x with { DebugLogs = v }, c));

        foreach (var row in rows)
        {
            row.Show(choice);
            row.Button.Click += (_, _) => Change(row, +1);
        }
    }

    static Button MakeButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        Height = 54,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>De gekozen regel of knop goed zichtbaar, ook op afstand (Steam Deck, tv).</summary>
    static Style FocusStyle() => new(x => x.OfType<Button>().Class(":focus").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
    {
        Setters =
        {
            new Setter(ContentPresenter.BackgroundProperty, Selected),
            new Setter(ContentPresenter.BorderBrushProperty, Accent),
            new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(2)),
        },
    };

    void OnFocus(int index)
    {
        focus = index;
        help.Text = index < rows.Count ? rows[index].Help
            : focusables[index] == play ? (game.NeedsApply ? "Past de gewijzigde instellingen toe en start het spel." : "Start het spel.")
            : "Sluit de launcher zonder te spelen. Je keuze blijft bewaard.";
        focusables[index].BringIntoView();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (busy) { e.Handled = true; return; }
        e.Handled = e.Key switch
        {
            Key.Up => Move(-1),
            Key.Down => Move(+1),
            Key.Left => Sideways(-1),
            Key.Right => Sideways(+1),
            Key.Escape => Do(Close),
            _ => false,
        };
    }

    void OnPad(Pad.Action action)
    {
        if (busy || action == Pad.Action.None) return;
        switch (action)
        {
            case Pad.Action.Up: Move(-1); break;
            case Pad.Action.Down: Move(+1); break;
            case Pad.Action.Left: Sideways(-1); break;
            case Pad.Action.Right: Sideways(+1); break;
            case Pad.Action.Accept:
                if (focus < rows.Count) Change(rows[focus], +1);
                else if (focusables[focus] == play) Play();
                else Close();
                break;
            case Pad.Action.Start: Play(); break;
            case Pad.Action.Back: Close(); break;
        }
    }

    static bool Do(Action action) { action(); return true; }

    /// <summary>Omhoog/omlaag door de regels; onder de laatste regel liggen Spelen en Afsluiten naast elkaar.</summary>
    bool Move(int delta)
    {
        int target = focus < rows.Count
            ? Math.Clamp(focus + delta, 0, rows.Count)                // van de laatste regel omlaag naar Spelen
            : delta < 0 && rows.Count > 0 ? rows.Count - 1 : focus;   // van de knoppen omhoog naar de laatste regel
        focusables[target].Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Links/rechts: de waarde van een regel wisselen, of tussen Spelen en Afsluiten.</summary>
    bool Sideways(int delta)
    {
        if (focus < rows.Count) Change(rows[focus], delta);
        else (focusables[focus] == play ? quit : play).Focus(NavigationMethod.Directional);
        return true;
    }

    void Change(OptionRow row, int delta)
    {
        choice = row.Step(choice, delta);
        row.Show(choice);
        try
        {
            game.Save(choice);
            UpdatePending();
        }
        catch (Exception e) when (GameFolder.IsExpected(e)) { ShowError($"Keuze niet bewaard: {e.Message}"); }
    }

    void UpdatePending() =>
        ShowStatus(game.NeedsApply ? "Gewijzigd: wordt toegepast als je op Spelen drukt." : "");

    async void Play()
    {
        if (busy) return;
        busy = true;
        foreach (var b in focusables) b.IsEnabled = false;
        ShowStatus(game.NeedsApply ? "Instellingen toepassen..." : "Spel starten...");

        var error = await Task.Run(game.Play);
        if (error == null)
        {
            Close();
            return;
        }
        ShowError(error);
        foreach (var b in focusables) b.IsEnabled = true;
        busy = false;
        play.Focus(NavigationMethod.Directional);
    }

    void ShowStatus(string text)
    {
        status.Foreground = Accent;
        status.Text = text;
    }

    void ShowError(string text)
    {
        status.Foreground = Error;
        status.Text = text;
    }
}

/// <summary>Eén instelling: een knop met het label links en de waarde rechts.</summary>
sealed class OptionRow
{
    public Button Button { get; }
    public string Help { get; }
    readonly TextBlock value = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    readonly string[] texts;
    readonly Func<Choice, int> get;
    readonly Func<Choice, int, Choice> set;

    OptionRow(string label, string help, string[] texts, Func<Choice, int> get, Func<Choice, int, Choice> set)
    {
        Help = help;
        this.texts = texts;
        this.get = get;
        this.set = set;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        Button = new Button
        {
            Content = grid,
            Height = 42,
            Padding = new Thickness(16, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// Een regel met vaste waarden. Een waarde die de speler of de patcher zelf heeft ingesteld en die niet in de lijst
    /// staat (bijv. resolutie 1920x1080), komt vooraan, zodat hij niet zomaar verdwijnt.
    /// </summary>
    public static OptionRow Create<T>(string label, string help, IEnumerable<(string Text, T Value)> items,
        Func<Choice, T> get, Func<Choice, T, Choice> set, Choice current)
    {
        var list = items.ToList();
        var now = get(current);
        if (!list.Any(i => EqualityComparer<T>.Default.Equals(i.Value, now))) list.Insert(0, (now?.ToString() ?? "", now));
        return new OptionRow(label, help, [.. list.Select(i => i.Text)],
            c => Math.Max(0, list.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, get(c)))),
            (c, i) => set(c, list[i].Value));
    }

    public Choice Step(Choice c, int delta) => set(c, (get(c) + delta + texts.Length) % texts.Length);

    public void Show(Choice c) => value.Text = $"‹  {texts[get(c)]}  ›";
}
