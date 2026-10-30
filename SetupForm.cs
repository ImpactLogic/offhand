using System.Diagnostics;

namespace PadLauncher;

/// First-run setup: where Windower is, whether the two drivers are installed, and what to do next. Opens by itself on the first run
/// (no config.json yet) and from the "Setup..." button. It only reads the machine and saves the Windower path; the drivers are installed
/// by their own installers (links open the author's release pages in the browser).
public class SetupForm : Form
{
    readonly Config cfg;
    readonly TextBox windowerBox = new() { Dock = DockStyle.Fill };
    readonly Label windowerState = new() { AutoSize = true };
    readonly Label vigemState = new() { AutoSize = true };
    readonly Label hidhideState = new() { AutoSize = true };
    readonly Label padState = new() { AutoSize = true };

    static readonly string[] WindowerGuesses =
    {
        @"C:\Program Files (x86)\Windower4\Windower.exe",
        @"C:\Program Files\Windower4\Windower.exe",
        @"C:\Windower4\Windower.exe",
        @"C:\Windower\Windower.exe",
    };

    public SetupForm(Config cfg)
    {
        this.cfg = cfg;
        Text = "Offhand: setup";
        Width = 780; Height = 700;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14), AutoScroll = true };
        layout.Controls.Add(Heading("1. Where is Windower?"));
        var pathRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Title = "Find Windower.exe", Filter = "Windower.exe|Windower.exe|Programs|*.exe", FileName = "Windower.exe" };
            if (d.ShowDialog(this) == DialogResult.OK) { windowerBox.Text = d.FileName; Refresh_(); }
        };
        windowerBox.Text = File.Exists(cfg.WindowerExe) ? cfg.WindowerExe : WindowerGuesses.FirstOrDefault(File.Exists) ?? cfg.WindowerExe;
        windowerBox.TextChanged += (_, _) => Refresh_();
        pathRow.Controls.Add(windowerBox, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        layout.Controls.Add(pathRow);
        layout.Controls.Add(windowerState);

        layout.Controls.Add(Heading("2. Drivers (one-time installs)"));
        layout.Controls.Add(Line("ViGEmBus makes the virtual Xbox pads that each game client reads. Needed for forwarding.", vigemState,
            "ViGEmBus downloads", "https://github.com/nefarius/ViGEmBus/releases"));
        layout.Controls.Add(Line("HidHide hides your real pads from the game while the launcher runs, and gives them back when it exits. Needed for forwarding.", hidhideState,
            "HidHide downloads", "https://github.com/nefarius/HidHide/releases"));
        layout.Controls.Add(Small("Run each installer as administrator (it is a driver) and restart if it asks. Without the drivers the launcher still works the plain way: it sets which controller each client uses when it starts."));

        layout.Controls.Add(Heading("3. Your controllers"));
        padState.MaximumSize = new Size(690, 0); layout.Controls.Add(padState);
        layout.Controls.Add(Small("Press a button on a pad in the main window to highlight it, and type in its Notes cell what it looks like (\"white, tape on back\"). Add one row per character: label, Windower profile, controller."));

        layout.Controls.Add(Heading("4. Every time you play"));
        layout.Controls.Add(Small("Turn the pads OFF, start the launcher, then turn the pads ON (the virtual pads must be created first). Tick \"Give each window its own virtual pad\", press Launch all, and log in on each client. To swap pads between clients: the Swap button or the tray menu. Quit the clients last-launched first. Exit the launcher from the tray icon when you are done: that gives the pads back to Steam and Windows."));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var done = new Button { Text = "Save and close", AutoSize = true, DialogResult = DialogResult.OK };
        var recheck = new Button { Text = "Check again", AutoSize = true };
        recheck.Click += (_, _) => Refresh_();
        done.Click += (_, _) => { cfg.WindowerExe = windowerBox.Text.Trim(); cfg.Save(); };
        buttons.Controls.AddRange(new Control[] { done, recheck });
        AcceptButton = done;
        Controls.Add(layout);
        Controls.Add(buttons);
        Refresh_();
    }

    static Label Heading(string text) => new() { Text = text, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 12, 0, 4) };
    static Label Small(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(0, 2, 0, 2) };

    Control Line(string text, Label state, string linkText, string url)
    {
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 700 };
        p.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(690, 0) });
        var row = new FlowLayoutPanel { AutoSize = true };
        var link = new LinkLabel { Text = linkText, AutoSize = true };
        link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
        row.Controls.Add(state);
        row.Controls.Add(link);
        p.Controls.Add(row);
        return p;
    }

    void Refresh_()
    {
        bool w = File.Exists(windowerBox.Text.Trim());
        windowerState.Text = w ? "Found Windower." : "Windower.exe not found at that path: use Browse...";
        windowerState.ForeColor = w ? Color.ForestGreen : Color.Firebrick;
        bool vigem = false;
        try { vigem = VirtualPads.DriverInstalled(); } catch { }
        vigemState.Text = vigem ? "ViGEmBus: installed.   " : "ViGEmBus: NOT installed.   ";
        vigemState.ForeColor = vigem ? Color.ForestGreen : Color.Firebrick;
        bool hh = HidHide.Installed;
        hidhideState.Text = hh ? "HidHide: installed.   " : "HidHide: NOT installed.   ";
        hidhideState.ForeColor = hh ? Color.ForestGreen : Color.Firebrick;
        int pads = 0;
        try { pads = Pads.List().Count; } catch { }
        padState.Text = pads == 0 ? "No controller is on right now (turn your pads on and press Check again)." : $"{pads} controller(s) connected.";
    }
}
