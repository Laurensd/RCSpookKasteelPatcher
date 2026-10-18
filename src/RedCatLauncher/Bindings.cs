using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RedCatPatcher.Patching;

namespace RedCatLauncher;

/// <summary>
/// Overzicht op de tab Controls: wat elke actie doet op toetsenbord en controller, volgens <c>PlayerDef.ini [KeyBinds]</c>
/// en <see cref="Controller.Mapping"/>. Alleen ter informatie; spelers met eigen toetsen in het menu wijken af.
/// </summary>
sealed class Bindings
{
    public Control View { get; }
    readonly Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("1.4*,*,*"), Margin = new Thickness(16, 0) };
    readonly IBrush dim;

    public Bindings(IBrush dim)
    {
        this.dim = dim;
        View = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            Spacing = 6,
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = "New players get these keys. Players you already made keep the keys from Options in the game's menu.",
                    FontSize = 13, Foreground = dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0),
                },
            },
        };
    }

    static IEnumerable<(string Action, string Keyboard, string Controller)> For(bool wasd, bool modern, bool menus)
    {
        var (forward, back, left, right) = wasd ? ("W", "S", "A", "D") : ("↑", "↓", "←", "→");
        string Pad(string button) => modern ? button : "–";
        return
        [
            ("Move forward / back", $"{forward} / {back}", "Left stick"),
            ("Turn left / right", $"{left} / {right}", "Left stick"),
            ("Strafe", $"Alt + {left} / {right}", "B + left stick"),
            ("Jump", "Space, right mouse", modern ? "A" : "X"),
            ("Fire", "Ctrl", modern ? "X" : "A"),
            ("Dodge", "Alt", "B"),
            ("Previous / next weapon", "[ / ]", Pad("LB / RB")),
            ("Select weapon", "1 – 7", "–"),
            ("Look up / down", "Page Up / Down", "–"),
            ("Center view", "Delete", Pad("Left stick click")),
            ("Scores", "Tab", Pad("Back")),
            ("Menu", "Esc", Pad("Start")),
            ("Pause", "P", "–"),
            ("Menus: pointer, click", "Mouse", menus ? "Left stick, A" : "–"),
            ("Menus: back, confirm", "Esc, Enter", menus ? "B, Start" : "–"),
        ];
    }

    public void Show(Choice c)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        int row = 0;
        void Add(string action, string keyboard, string controller, bool header)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            string[] cells = [action, keyboard, controller];
            for (int column = 0; column < cells.Length; column++)
            {
                var cell = new TextBlock
                {
                    Text = cells[column],
                    FontSize = 14,
                    Margin = new Thickness(0, 1),
                    Foreground = header ? dim : column == 0 ? Brushes.White : Brushes.Gainsboro,
                    FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
            row++;
        }
        Add("Action", "Keyboard", "Controller", header: true);
        foreach (var (action, keyboard, controller) in For(c.Wasd, c.Controller, c.MenuController)) Add(action, keyboard, controller, header: false);
    }
}
