using System.Diagnostics;

namespace PadLauncher;

/// Edits a draft. Back and Cancel never change the live configuration or start a client/virtual pad.
public class SetupForm : Form
{
    readonly Config cfg;
    readonly TextBox path = new() { Dock = DockStyle.Top };
    readonly Label pathState = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    readonly Label drivers = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    readonly Label padState = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    readonly Label title = new() { Dock = DockStyle.Top, Height = 58, Font = new Font(SystemFonts.DefaultFont.FontFamily, 16, FontStyle.Bold) };
    readonly Label summary = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    readonly Panel host = new() { Dock = DockStyle.Fill, Padding = new Padding(20) };
    readonly Button next = new() { Text = "Next >", AutoSize = true };
    readonly Button back = new() { Text = "< Back", AutoSize = true };
    readonly DataGridView characters = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, BackgroundColor = SystemColors.Window };
    readonly List<Control> pages = new();
    int step;
    readonly Dictionary<string, string> controllerChoices = new();

    public SetupForm(Config cfg)
    {
        this.cfg = cfg;
        Text = "Offhand — setup wizard"; ClientSize = new Size(740, 530); MinimumSize = new Size(700, 540);
        StartPosition = FormStartPosition.CenterParent;
        var intro = Stack();
        intro.Controls.Add(TextLine("Choose Windower.exe. Offhand reads your profiles; you continue to edit graphics and sound in Windower."));
        path.Text = cfg.WindowerExe;
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) => { using var file = new OpenFileDialog { Filter = "Windower.exe|Windower.exe", Title = "Find Windower.exe" }; if (file.ShowDialog(this) == DialogResult.OK) path.Text = file.FileName; };
        path.Width = 620; intro.Controls.Add(path); intro.Controls.Add(browse); intro.Controls.Add(pathState);
        path.TextChanged += (_, _) => CheckPath();
        pages.Add(intro);

        var hardware = Stack();
        hardware.Controls.Add(TextLine("Simple mode assigns a controller when each client starts. Virtual pads additionally support live swaps and reconnection; they need both drivers below. Choose virtual pads from Characters after setup."));
        hardware.Controls.Add(drivers);
        foreach (var (name, url) in new[] { ("ViGEmBus downloads", "https://github.com/nefarius/ViGEmBus/releases"), ("HidHide downloads", "https://github.com/nefarius/HidHide/releases") })
        {
            var link = new LinkLabel { Text = name, AutoSize = true };
            link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(this, e.Message, "Could not open downloads"); } };
            hardware.Controls.Add(link);
        }
        hardware.Controls.Add(TextLine("Turn your controllers on to assign them. You can name and identify them on Controllers after setup. Install drivers with their own installers and restart Windows if asked."));
        hardware.Controls.Add(padState);
        var check = new Button { Text = "Check again", AutoSize = true };
        check.Click += (_, _) => CheckHardware(); hardware.Controls.Add(check); pages.Add(hardware);

        characters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Character", FillWeight = 30 });
        characters.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Windower profile", FlatStyle = FlatStyle.Flat, FillWeight = 35 });
        characters.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Controller", FlatStyle = FlatStyle.Flat, FillWeight = 55 });
        characters.DataError += (_, _) => { }; // A disconnected selection stays available until it can be replaced.
        var characterPage = new Panel { Dock = DockStyle.Fill };
        var note = TextLine("Add up to two characters in launch order. Each needs its own controller. You can change this list later on Characters."); note.Dock = DockStyle.Top;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var add = new Button { Text = "+ Add character", AutoSize = true };
        add.Click += (_, _) => { if (characters.Rows.Count < 2) characters.Rows.Add("", null!, null!); };
        var remove = new Button { Text = "Remove selected", AutoSize = true };
        remove.Click += (_, _) => { if (characters.CurrentRow != null) characters.Rows.Remove(characters.CurrentRow); };
        actions.Controls.AddRange(new Control[] { add, remove });
        characterPage.Controls.Add(characters); characterPage.Controls.Add(note); characterPage.Controls.Add(actions); pages.Add(characterPage);
        var review = Stack(); review.Controls.Add(summary);
        review.Controls.Add(TextLine("Save setup returns to Characters. Nothing launches automatically.\n\nFor virtual pads, turn your controllers OFF before starting Offhand, then ON after its virtual pads are ready.\n\nWhile playing, the window's X hides Offhand in the tray. Quit clients last-launched first, then use File > Exit Offhand to release the controllers.")); pages.Add(review);

        foreach (var page in pages) { page.Dock = DockStyle.Fill; page.Visible = false; host.Controls.Add(page); }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        next.Click += (_, _) => Advance(); back.Click += (_, _) => { step--; ShowStep(); };
        buttons.Controls.AddRange(new Control[] { cancel, next, back });
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 0) }; body.Controls.Add(host); body.Controls.Add(title);
        Controls.Add(body); Controls.Add(buttons); AcceptButton = next; CancelButton = cancel;
        LoadChoices();
        foreach (var row in cfg.Rows) characters.Rows.Add(row.Label, row.Profile, row.PadAddress);
        CheckPath(); CheckHardware(); ShowStep();
    }

    static FlowLayoutPanel Stack() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    static Label TextLine(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 4, 0, 16) };
    void CheckPath()
    {
        bool valid = ValidPath(); pathState.Text = valid ? "Windower found." : "Choose an existing file named Windower.exe.";
        pathState.ForeColor = valid ? Color.ForestGreen : Color.Firebrick;
    }
    bool ValidPath() => File.Exists(path.Text.Trim()) && string.Equals(Path.GetFileName(path.Text.Trim()), "Windower.exe", StringComparison.OrdinalIgnoreCase);
    void CheckHardware()
    {
        bool vigem = false; try { vigem = VirtualPads.DriverInstalled(); } catch { }
        drivers.Text = $"ViGEmBus: {(vigem ? "installed" : "not installed")}\nHidHide: {(HidHide.Installed ? "installed" : "not installed")}";
        LoadControllerChoices();
        padState.Text = $"{connectedCount} controller(s) connected. Missing drivers do not block simple mode.";
    }
    int connectedCount;
    void LoadChoices()
    {
        var profiles = new Config { WindowerExe = path.Text.Trim() }.WindowerProfiles();
        var col = (DataGridViewComboBoxColumn)characters.Columns[1]; col.Items.Clear();
        foreach (var profile in profiles.Concat(cfg.Rows.Select(r => r.Profile)).Where(x => x.Length > 0).Distinct()) col.Items.Add(profile);
        LoadControllerChoices();
    }
    void LoadControllerChoices()
    {
        List<Pad> pads; try { pads = Pads.List(); } catch { pads = new(); }
        connectedCount = pads.Count;
        foreach (var pad in pads) controllerChoices[pad.Address] = cfg.PadLabel(pad);
        foreach (var row in cfg.Rows.Where(r => r.PadAddress.Length > 0)) controllerChoices.TryAdd(row.PadAddress, cfg.NoteFor(row.PadAddress) is { Length: > 0 } n ? n + " (saved)" : row.PadAddress + " (saved)");
        var col = (DataGridViewComboBoxColumn)characters.Columns[2];
        col.ValueMember = "Key"; col.DisplayMember = "Value"; col.DataSource = controllerChoices.ToList();
    }
    List<Row> DraftRows() => characters.Rows.Cast<DataGridViewRow>().Select(r => new Row { Label = Convert.ToString(r.Cells[0].Value)?.Trim() ?? "", Profile = Convert.ToString(r.Cells[1].Value) ?? "", PadAddress = Convert.ToString(r.Cells[2].Value) ?? "" }).ToList();
    string? CharacterError()
    {
        var rows = DraftRows();
        var profiles = new Config { WindowerExe = path.Text.Trim() }.WindowerProfiles();
        return rows.Count is < 1 or > 2 || rows.Any(r => r.Label.Length == 0 || !profiles.Contains(r.Profile) || r.PadAddress.Length == 0)
            || rows.Select(r => r.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count
            || rows.Select(r => r.PadAddress).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count
            ? "Add one or two characters with different names, a current Windower profile, and a different controller for each." : null;
    }
    void Advance()
    {
        if (step == 0 && !ValidPath()) { MessageBox.Show(this, "Choose Windower.exe before continuing.", "Windower path"); return; }
        if (step == 0) LoadChoices();
        if (step == 2)
        {
            characters.EndEdit(); var rows = DraftRows();
            if (CharacterError() is { } error) { MessageBox.Show(this, error, "Check characters"); return; }
            summary.Text = "Ready to save\n\nWindower: " + path.Text.Trim() + "\n\nLaunch order:\n" + string.Join("\n", rows.Select((r, i) => $"{i + 1}. {r.Label} — {r.Profile}"));
        }
        if (step < pages.Count - 1) { step++; ShowStep(); return; }
        var oldPath = cfg.WindowerExe; var oldRows = cfg.Rows; var oldCompleted = cfg.SetupCompleted;
        try { cfg.WindowerExe = path.Text.Trim(); cfg.Rows = DraftRows(); cfg.SetupCompleted = true; cfg.Save(); DialogResult = DialogResult.OK; Close(); }
        catch (Exception e) { cfg.WindowerExe = oldPath; cfg.Rows = oldRows; cfg.SetupCompleted = oldCompleted; MessageBox.Show(this, "Could not save setup: " + e.Message, "Setup"); }
    }
    void ShowStep()
    {
        title.Text = $"{step + 1} of 4 — " + new[] { "Find Windower", "Controllers and drivers", "Your characters", "Ready to play" }[step];
        for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == step;
        pages[step].BringToFront(); back.Enabled = step > 0; next.Text = step == pages.Count - 1 ? "Save setup" : "Next >";
    }
}
