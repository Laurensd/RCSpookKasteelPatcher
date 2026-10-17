namespace LauncherSpike;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new MainForm());
    }
}

sealed class MainForm : Form
{
    readonly Button[] buttons;
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 80, TextAlign = ContentAlignment.MiddleCenter };
    readonly Pad pad = new();
    int focus;

    public MainForm()
    {
        Text = "RedCat launcher-proef (WinForms)";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(FontFamily.GenericSansSerif, 16);

        var play = new Button { Text = "Spelen", Size = new Size(260, 70), Location = new Point(190, 80) };
        var quit = new Button { Text = "Afsluiten", Size = new Size(260, 70), Location = new Point(190, 170) };
        play.Click += (_, _) => Play();
        quit.Click += (_, _) => Close();
        play.GotFocus += (_, _) => focus = 0;
        quit.GotFocus += (_, _) => focus = 1;
        buttons = [play, quit];
        Controls.AddRange([play, quit, status]);
        AcceptButton = play;
        CancelButton = quit;

        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) => OnPad(pad.Poll());
        timer.Start();

        Shown += (_, _) =>
        {
            play.Focus();
            var line = $"WinForms opgestart in {Spike.Elapsed()} ({Spike.Describe()})";
            status.Text = line;
            Spike.Log(line);
            if (Spike.Measure) BeginInvoke(Close);
        };
    }

    void OnPad(Pad.Action action)
    {
        switch (action)
        {
            case Pad.Action.Previous: MoveFocus(-1); break;
            case Pad.Action.Next: MoveFocus(1); break;
            case Pad.Action.Activate: buttons[focus].PerformClick(); break;
            case Pad.Action.Back: Close(); break;
        }
    }

    void MoveFocus(int delta) => buttons[(focus + delta + buttons.Length) % buttons.Length].Focus();

    void Play()
    {
        var error = Spike.StartGame();
        if (error is null) Close();
        else status.Text = error;
    }
}
