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
        Height = 680;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 18;
        Styles.Add(CurrentTabStyle());
        Styles.Add(FocusStyle());

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
        var c = choice;
        AddCategory("Display", "Aspect ratio, display mode and sharpness.",
            OptionRow.Create("Aspect ratio",
                "Widescreen gives a wider field of view and a HUD that isn't stretched. 16:10 fits the Steam Deck.",
                [("4:3 (original)", (Widescreen?)null), ("16:9 widescreen", Widescreen.Wide16x9), ("16:10 widescreen", Widescreen.Wide16x10)],
                x => x.Widescreen, (x, v) => x with { Widescreen = v }, c),
            OptionRow.Create("Display mode",
                "Borderless works best with Alt+Tab and under Proton. Windowed is twice the game's resolution.",
                [("Borderless", DisplayMode.Borderless), ("Fullscreen", DisplayMode.Fullscreen), ("Windowed", DisplayMode.Window)],
                x => x.Display.Mode, (x, v) => x with { Display = x.Display with { Mode = v } }, c),
            OptionRow.Create("Render resolution",
                "How sharply dgVoodoo2 draws the game. Auto: as large as the screen, 2x when windowed.",
                [("Auto", (string?)null), ("Screen maximum", "max"), ("2x", "2x"), ("3x", "3x"), ("4x", "4x"),
                 ("Desktop", "desktop"), ("Original (no scaling)", "unforced")],
                x => x.Display.Resolution, (x, v) => x with { Display = x.Display with { Resolution = v } }, c));
        AddCategory("Graphics", "Picture quality and frame rate.",
            OptionRow.Create("Anti-aliasing",
                "Smooths jagged edges (MSAA). Higher settings ask more of the graphics card.",
                DgVoodoo.AntialiasingValues.Select(v => (v == "off" ? "Off" : v, v)),
                x => x.Display.Antialiasing, (x, v) => x with { Display = x.Display with { Antialiasing = v } }, c),
            OptionRow.Create("V-Sync",
                "Turning V-Sync off can cause screen tearing.",
                [("On", true), ("Off", false)],
                x => x.Display.VSync, (x, v) => x with { Display = x.Display with { VSync = v } }, c),
            OptionRow.Create("Frame rate limit",
                "Maximum frames per second. Unlimited: only V-Sync limits the frame rate.",
                new[] { 0, 30, 40, 60, 90, 120, 144 }.Select(v => (v == 0 ? "Unlimited" : $"{v} fps", v)),
                x => x.Display.FpsLimit, (x, v) => x with { Display = x.Display with { FpsLimit = v } }, c),
            OptionRow.Create("CRT effect",
                "Scanlines and a slightly curved picture, like an old monitor. Only visible in fullscreen.",
                [("Off", false), ("On", true)],
                x => x.Display.Crt, (x, v) => x with { Display = x.Display with { Crt = v } }, c));
        AddCategory("Controls", "Keyboard and controller.",
            OptionRow.Create("Movement keys",
                "Default keys for new players and for the Default button in the game's options. Existing players keep their own keys.",
                [("Arrow keys", false), ("WASD", true)],
                x => x.Wasd, (x, v) => x with { Wasd = v }, c),
            OptionRow.Create("Controller layout",
                "Modern: A jump, B dodge, X shoot, LB/RB switch weapon, Back scores, Start menu. Classic: as in the original, " +
                "the stick and four buttons. In the game only; the menus still use mouse and keyboard.",
                [("Classic", false), ("Modern", true)],
                x => x.Controller, (x, v) => x with { Controller = v }, c));
        AddCategory("Advanced", "Troubleshooting.",
            OptionRow.Create("Debug logs",
                "Writes log files of the game and the menu to the game folder, to help track down problems.",
                [("Off", false), ("On", true)],
                x => x.DebugLogs, (x, v) => x with { DebugLogs = v }, c));
    }

    void AddCategory(string name, string description, params OptionRow[] rows)
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
            row.Show(choice);
            row.Button.Click += (_, _) => Change(row, +1);
            row.Button.GotFocus += (_, _) => OnFocus(row.Button, row.Help);
        }
        categories.Add(category);
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
        row.Show(choice);
        try
        {
            game.Save(choice);
            UpdatePending();
        }
        catch (Exception e) when (GameFolder.IsExpected(e)) { ShowError($"Settings not saved: {e.Message}"); }
    }

    void UpdatePending() =>
        ShowStatus(game.NeedsApply ? "Changed: applied when you press Play." : "");

    async void Play()
    {
        if (busy) return;
        busy = true;
        foreach (var b in AllButtons) b.IsEnabled = false;
        ShowStatus(game.NeedsApply ? "Applying settings..." : "Starting the game...");

        var error = await Task.Run(game.Play);
        if (error == null)
        {
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
