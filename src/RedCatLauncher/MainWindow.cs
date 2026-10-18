using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using RedCatPatcher.Patching;

namespace RedCatLauncher;

/// <summary>
/// Het startscherm: bovenaan een knop per categorie (Display, Graphics, Controls, Advanced), daaronder één regel per
/// instelling van die categorie, onderaan Play en Quit. Te bedienen met muis, toetsenbord of controller: omhoog/omlaag
/// kiest een regel (boven de eerste regel ligt de tabbalk), links/rechts (of klikken, A) wisselt de waarde of de
/// categorie, LB/RB of PgUp/PgDn wisselt overal de categorie, B/Esc sluit, Start speelt.
/// De keuze komt meteen in RedCatLauncher.ini [Keuze]; toegepast wordt pas bij Play.
/// </summary>
sealed class MainWindow : Window
{
    static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xE8, 0x8A, 0x1A));
    static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    static readonly IBrush Selected = new SolidColorBrush(Color.FromRgb(0x5C, 0x3A, 0x10));
    static readonly IBrush Error = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x6E));

    readonly GameFolder game;
    readonly List<Category> categories = [];
    readonly Button play, quit;
    readonly ScrollViewer page = new();
    readonly TextBlock help = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Dim, MinHeight = 36 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    readonly Pad pad = new();
    readonly Bindings bindings = new(Dim);
    PixelSize screen = new(1920, 1080);
    Choice choice;
    Category? current;
    Control? focused;
    bool busy;

    public MainWindow(GameFolder game, string? startupError)
    {
        this.game = game;
        choice = game.Wanted;
        Title = "RedCat Spookkasteel";
        Width = 760;
        Height = 760;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 18;
        Styles.Add(CurrentTabStyle());
        Styles.Add(FocusStyle());
        // dgVoodoo rendert op het hoofdscherm (CenterAppWindow); daar hangt de lijst met resoluties van af
        if (Screens.Primary is { } primary) screen = primary.Bounds.Size;

        if (game.CanChoose) AddCategories();

        play = MakeButton("Play", 220);
        quit = MakeButton("Quit", 160);
        play.Click += (_, _) => Play();
        quit.Click += (_, _) => Close();
        play.GotFocus += (_, _) => OnFocus(play, game.NeedsApply ? "Applies the changed settings and starts the game." : "Starts the game.");
        quit.GotFocus += (_, _) => OnFocus(quit, "Closes the launcher without playing. Your settings are kept.");

        var tabs = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 10), IsVisible = categories.Count > 0 };
        foreach (var category in categories) tabs.Children.Add(category.Tab);
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
                        new TextBlock { Text = $"Launcher {version}", Foreground = Dim, FontSize = 15 },
                    },
                }, Avalonia.Controls.Dock.Top),
                Dock(tabs, Avalonia.Controls.Dock.Top),
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
                            Text = "Arrows or d-pad: select and change · LB/RB or PgUp/PgDn: category · Enter/A: change or play · Start: play · Esc/B: quit",
                            FontSize = 13, Foreground = Dim, TextWrapping = TextWrapping.Wrap,
                        },
                    },
                }, Avalonia.Controls.Dock.Bottom),
                page,
            },
        };

        if (categories.Count > 0) ShowCategory(categories[0]);
        if (startupError != null) ShowError(startupError);
        else if (!game.HasGame) ShowError($"{GameFolder.GameExe} not found in {game.Dir}. Put the launcher in the game folder, next to {GameFolder.GameExe}.");
        else if (!game.CanChoose)
            ShowStatus("This installation was patched with an older patcher, so its settings can't be changed here. " +
                "Patch again from the disc (players and scores are kept). Play starts the game as it is now.");
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

    void AddCategories()
    {
        AddCategory("Display", "Aspect ratio, display mode and resolution.", null,
            OptionRow.Create("Aspect ratio",
                "Widescreen gives a wider field of view and a HUD that isn't stretched. 16:10 fits the Steam Deck.",
                [("4:3 (original)", (Widescreen?)null), ("16:9 widescreen", Widescreen.Wide16x9), ("16:10 widescreen", Widescreen.Wide16x10)],
                x => x.Widescreen,
                // Een vaste resolutie hoort bij één beeldverhouding; bij een andere wordt het Native
                (x, v) => x with
                {
                    Widescreen = v,
                    Display = x.Display.GlideResolution.Contains('x') && char.IsDigit(x.Display.GlideResolution[^1]) && x.Display.Mode != DisplayMode.Window
                        ? x.Display with { Resolution = Resolutions.Default(x.Display.Mode) } : x.Display,
                }),
            OptionRow.Create("Display mode",
                "Borderless works best with Alt+Tab and under Proton. Fullscreen switches the screen mode. Windowed shows the game in a window.",
                [("Borderless", DisplayMode.Borderless), ("Fullscreen", DisplayMode.Fullscreen), ("Windowed", DisplayMode.Window)],
                x => x.Display.Mode,
                // In een venster is de resolutie de venstergrootte, dus bij het wisselen terug naar de standaard
                (x, v) => x with
                {
                    Display = (v == DisplayMode.Window) == (x.Display.Mode == DisplayMode.Window)
                        ? x.Display with { Mode = v }
                        : x.Display with { Mode = v, Resolution = Resolutions.Default(v) },
                }),
            OptionRow.Create("Resolution",
                "The resolution the game is drawn at; it is scaled to fill the screen. Native is the sharpest. " +
                "Original is the game's own blurry look. Windowed: the size of the window.",
                x => Resolutions.For(x, screen),
                x => x.Display.GlideResolution, (x, v) => x with { Display = x.Display with { Resolution = v } }));
        AddCategory("Graphics", "Picture quality and frame rate.", null,
            OptionRow.Create("Anti-aliasing",
                "Smooths jagged edges (MSAA). Higher settings ask more of the graphics card.",
                DgVoodoo.AntialiasingValues.Select(v => (v == "off" ? "Off" : $"{v} MSAA", v)),
                x => x.Display.Antialiasing, (x, v) => x with { Display = x.Display with { Antialiasing = v } }),
            OptionRow.Create("Render scale",
                "Supersampling: draws the game at 2 to 4 times the resolution and scales it down to the screen. " +
                "Even smoother edges, but much heavier on the graphics card.",
                Resolutions.RenderScales,
                x => x.Display.RenderScale, (x, v) => x with { Display = x.Display with { RenderScale = v } }),
            OptionRow.Create("V-Sync",
                "Turning V-Sync off can cause screen tearing.",
                [("On", true), ("Off", false)],
                x => x.Display.VSync, (x, v) => x with { Display = x.Display with { VSync = v } }),
            OptionRow.Create("Frame rate limit",
                "Maximum frames per second. Unlimited: only V-Sync limits the frame rate. The game itself runs at about 60.",
                new[] { 0, 30, 40, 60, 90, 120, 144 }.Select(v => (v == 0 ? "Unlimited" : $"{v} fps", v)),
                x => x.Display.FpsLimit, (x, v) => x with { Display = x.Display with { FpsLimit = v } }),
            OptionRow.Create("CRT effect",
                "Scanlines and a slightly curved picture, like an old monitor. Only visible in fullscreen.",
                [("Off", false), ("On", true)],
                x => x.Display.Crt, (x, v) => x with { Display = x.Display with { Crt = v } }));
        AddCategory("Controls", "Keyboard and controller layout.", bindings.View,
            OptionRow.Create("Keyboard",
                "Modern: move with W, A, S and D. Classic: the arrow keys, as in the original.",
                [("Modern (WASD)", true), ("Classic (arrow keys)", false)],
                x => x.Wasd, (x, v) => x with { Wasd = v }),
            OptionRow.Create("Controller",
                "Modern: a layout like today's games, with weapon switching, scores and the menu on the controller. " +
                "Classic: as in the original, the stick and four buttons. Works in the game; the menus use mouse and keyboard.",
                [("Modern", true), ("Classic", false)],
                x => x.Controller, (x, v) => x with { Controller = v }),
            OptionRow.Create("Controller in menus",
                "The game's menus don't support a controller. On: in the menus the left stick moves the mouse pointer, " +
                "A clicks, B is Esc and Start is Enter. The launcher keeps running in the background while you play.",
                [("On", true), ("Off", false)],
                x => x.MenuController, (x, v) => x with { MenuController = v }));
        AddCategory("Advanced", "Troubleshooting.", null,
            OptionRow.Create("Debug logs",
                "Writes log files of the game and the menu to the game folder, to help track down problems.",
                [("Off", false), ("On", true)],
                x => x.DebugLogs, (x, v) => x with { DebugLogs = v }));
        ShowAll();
    }

    void AddCategory(string name, string description, Control? extra, params OptionRow[] rows)
    {
        var category = new Category(new Button
        {
            Content = name,
            Height = 46,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        }, [.. rows], new StackPanel { Spacing = 4 });
        category.Tab.Click += (_, _) => ShowCategory(category);
        category.Tab.GotFocus += (_, _) => OnFocus(category.Tab, description);
        foreach (var row in rows)
        {
            category.Panel.Children.Add(row.Button);
            row.Button.Click += (_, _) => Change(row, +1);
            row.Button.GotFocus += (_, _) => OnFocus(row.Button, row.Help);
        }
        if (extra != null) category.Panel.Children.Add(extra);
        categories.Add(category);
    }

    /// <summary>Alle regels opnieuw tonen: een keuze kan de waarden van andere regels veranderen (resolutie, render scale).</summary>
    void ShowAll()
    {
        foreach (var row in categories.SelectMany(c => c.Rows)) row.Show(choice);
        bindings.Show(choice);
    }

    void ShowCategory(Category category)
    {
        current = category;
        foreach (var c in categories) c.Tab.Classes.Set("current", c == category);
        page.Content = category.Panel;
        page.Offset = default;
    }

    IEnumerable<Button> AllButtons =>
        categories.Select(c => c.Tab).Concat(categories.SelectMany(c => c.Rows.Select(r => r.Button))).Append(play).Append(quit);

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

    /// <summary>De open categorie: oranje tekst met een streep eronder.</summary>
    static Style CurrentTabStyle() => new(x => x.OfType<Button>().Class("current").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
    {
        Setters =
        {
            new Setter(ContentPresenter.ForegroundProperty, Accent),
            new Setter(ContentPresenter.BorderBrushProperty, Accent),
            new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(0, 0, 0, 3)),
        },
    };

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

    void OnFocus(Control control, string text)
    {
        focused = control;
        help.Text = text;
        control.BringIntoView();
    }

    bool OnTab => categories.Any(c => c.Tab == focused);

    /// <summary>Index van de gekozen regel in de open categorie, of -1.</summary>
    int RowIndex => current?.Rows.FindIndex(r => r.Button == focused) ?? -1;

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (busy) { e.Handled = true; return; }
        e.Handled = e.Key switch
        {
            Key.Up => Move(-1),
            Key.Down => Move(+1),
            Key.Left => Sideways(-1),
            Key.Right => Sideways(+1),
            Key.PageUp => SwitchCategory(-1),
            Key.PageDown => SwitchCategory(+1),
            Key.Tab when e.KeyModifiers.HasFlag(KeyModifiers.Control) => SwitchCategory(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1),
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
            case Pad.Action.PreviousTab: SwitchCategory(-1); break;
            case Pad.Action.NextTab: SwitchCategory(+1); break;
            case Pad.Action.Accept:
                if (RowIndex >= 0) Change(current!.Rows[RowIndex], +1);
                else if (focused == play) Play();
                else if (focused == quit) Close();
                break;
            case Pad.Action.Start: Play(); break;
            case Pad.Action.Back: Close(); break;
        }
    }

    static bool Do(Action action) { action(); return true; }

    /// <summary>
    /// Omhoog/omlaag: de tabbalk, de regels van de open categorie, dan Play en Quit naast elkaar. Van de tabbalk omlaag
    /// naar de eerste regel, van de knoppen omhoog naar de laatste.
    /// </summary>
    bool Move(int delta)
    {
        var rows = current?.Rows ?? [];
        int row = RowIndex;
        Control target =
            OnTab ? (delta > 0 ? rows.FirstOrDefault()?.Button ?? play : focused!)
            : row >= 0 ? (row + delta < 0 ? current!.Tab : row + delta >= rows.Count ? play : rows[row + delta].Button)
            : focused == play || focused == quit ? (delta < 0 ? rows.LastOrDefault()?.Button ?? current?.Tab ?? focused : focused)
            : play;
        target.Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Links/rechts: de categorie wisselen op de tabbalk, de waarde van een regel, of tussen Play en Quit.</summary>
    bool Sideways(int delta)
    {
        if (OnTab) SwitchCategory(delta);
        else if (RowIndex >= 0) Change(current!.Rows[RowIndex], delta);
        else (focused == play ? quit : play).Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Naar de vorige of volgende categorie; de focus blijft op de tabbalk, bij de regels of op de knoppen.</summary>
    bool SwitchCategory(int delta)
    {
        if (current == null) return true;
        bool onTab = OnTab, onRow = RowIndex >= 0;
        var next = categories[(categories.IndexOf(current) + delta + categories.Count) % categories.Count];
        ShowCategory(next);
        if (onTab) next.Tab.Focus(NavigationMethod.Directional);
        else if (onRow) next.Rows[0].Button.Focus(NavigationMethod.Directional);
        return true;
    }

    void Change(OptionRow row, int delta)
    {
        choice = row.Step(choice, delta);
        ShowAll();
        try
        {
            game.Save(choice);
            UpdatePending();
        }
        catch (Exception e) when (GameFolder.IsExpected(e)) { ShowError($"Settings not saved: {e.Message}"); }
    }

    void UpdatePending() =>
        ShowStatus(game.NeedsFixUpdate ? "A game fix is installed when you press Play."
            : game.NeedsApply ? "Changed: applied when you press Play." : "");

    async void Play()
    {
        if (busy) return;
        busy = true;
        foreach (var b in AllButtons) b.IsEnabled = false;
        ShowStatus(game.NeedsApply ? "Applying settings..." : "Starting the game...");

        var error = await Task.Run(game.Play);
        if (error == null)
        {
            // Verborgen blijven draaien voor de controller in de menu's
            Hide();
            await Task.Run(game.RunMenuController);
            Close();
            return;
        }
        ShowError(error);
        foreach (var b in AllButtons) b.IsEnabled = true;
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

/// <summary>Een categorie: de knop op de tabbalk en de regels die eronder verschijnen.</summary>
sealed record Category(Button Tab, List<OptionRow> Rows, StackPanel Panel);

/// <summary>Eén instelling: een knop met het label links en de waarde rechts.</summary>
sealed class OptionRow
{
    public Button Button { get; }
    public string Help { get; }
    readonly TextBlock value = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    readonly Func<Choice, string> text;
    readonly Func<Choice, int, Choice> step;

    OptionRow(string label, string help, Func<Choice, string> text, Func<Choice, int, Choice> step)
    {
        Help = help;
        this.text = text;
        this.step = step;
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

    public static OptionRow Create<T>(string label, string help, IEnumerable<(string Text, T Value)> items,
        Func<Choice, T> get, Func<Choice, T, Choice> set)
    {
        var list = items.ToList();
        return Create(label, help, _ => list, get, set);
    }

    /// <summary>
    /// Een regel waarvan de waarden van de rest van de keuze afhangen (bijv. de resoluties van de weergave). Een waarde
    /// die de speler of de patcher zelf heeft ingesteld en die niet in de lijst staat (bijv. max_isf), komt vooraan,
    /// zodat hij niet zomaar verdwijnt.
    /// </summary>
    public static OptionRow Create<T>(string label, string help, Func<Choice, IEnumerable<(string Text, T Value)>> items,
        Func<Choice, T> get, Func<Choice, T, Choice> set)
    {
        (List<(string Text, T Value)> List, int Index) Current(Choice c)
        {
            var list = items(c).ToList();
            var now = get(c);
            int index = list.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, now));
            if (index < 0) list.Insert(index = 0, (now?.ToString() ?? "", now));
            return (list, index);
        }
        return new OptionRow(label, help,
            c => { var (list, index) = Current(c); return list[index].Text; },
            (c, delta) => { var (list, index) = Current(c); return set(c, list[(index + delta + list.Count) % list.Count].Value); });
    }

    public Choice Step(Choice c, int delta) => step(c, delta);

    public void Show(Choice c) => value.Text = $"‹  {text(c)}  ›";
}
