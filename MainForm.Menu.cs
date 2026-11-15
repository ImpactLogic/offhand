namespace PadLauncher;

/// The Windows-style menu bar (0.11.0). Every item calls something the window already did with a button; the buttons that used to sit
/// under "More" are gone from the window, so the menu is now the one place for them. Shortcuts follow Windows convention.
public partial class MainForm
{
    void BuildMenuBar()
    {
        var menu = new MenuStrip();
        ToolStripMenuItem Top(string text) { var t = new ToolStripMenuItem(text); menu.Items.Add(t); return t; }
        static ToolStripMenuItem Item(ToolStripMenuItem parent, string text, Action act, Keys keys = Keys.None, string? keyText = null)
        {
            var i = new ToolStripMenuItem(text, null, (_, _) => act()) { ShortcutKeys = keys };
            if (keyText != null) i.ShortcutKeyDisplayString = keyText;
            parent.DropDownItems.Add(i); return i;
        }
        static ToolStripMenuItem Check(ToolStripMenuItem parent, string text, Func<bool> get, Action toggle)
        {
            var i = new ToolStripMenuItem(text) { CheckOnClick = false };
            i.Click += (_, _) => toggle();
            parent.DropDown.Opening += (_, _) => i.Checked = get();
            parent.DropDownItems.Add(i); return i;
        }
        static void Sep(ToolStripMenuItem parent) => parent.DropDownItems.Add(new ToolStripSeparator());

        var file = Top("&File");
        var launchItem = Item(file, "&Launch all", () => { if (launchBtn.Enabled) _ = LaunchAll(); }, Keys.Control | Keys.L);
        file.DropDown.Opening += (_, _) => { UpdateGuide(); launchItem.Enabled = launchBtn.Enabled; };
        Sep(file);
        Item(file, "&Save now", () => { SaveRows(); Log("Saved " + Config.FilePath); }, Keys.Control | Keys.S);
        Item(file, "&Export characters...", ExportCharacters, Keys.Control | Keys.E);
        Item(file, "&Import characters...", ImportCharacters, Keys.Control | Keys.I);
        Sep(file);
        Item(file, "Save &diagnostics", SaveDiagnostics);
        Sep(file);
        Item(file, "&Hide window (keep running in the tray)", Hide, Keys.Control | Keys.W);
        Item(file, "E&xit Offhand", ExitApp, Keys.Control | Keys.Q);

        var chars = Top("&Characters");
        Item(chars, "&Add character", () => { if (characterActions?.Enabled != false) AddCharacter(); }, Keys.Control | Keys.N);
        Item(chars, "&Remove selected", () => { if (!SessionActive && grid.CurrentRow != null) RemoveCharacter(grid.CurrentRow.Index); }, Keys.None, "Del");
        Item(chars, "Move &up", () => { if (!SessionActive) MoveRow(-1); }, Keys.Alt | Keys.Up);
        Item(chars, "Move &down", () => { if (!SessionActive) MoveRow(1); }, Keys.Alt | Keys.Down);
        Sep(chars);
        Item(chars, "Make the &other character the main...", SwitchMain);
        Check(chars, "&Quality profiles (full / lite)", () => morePanel.Visible, () => { NavigateTo("Characters"); morePanel.Visible = !morePanel.Visible; });

        var ctl = Top("Con&trollers");
        Item(ctl, "&Refresh controllers", () => { Pads.InvalidatePathCache(); RefreshPads(); }, Keys.F5);
        Item(ctl, "&Swap pads", () => { if (swapBtn.Enabled) swapBtn.PerformClick(); }, Keys.Control | Keys.P);
        Sep(ctl);
        Check(ctl, "Own &virtual pad for each window", () => fwdBox.Checked, () => { if (fwdBox.Enabled) fwdBox.Checked = !fwdBox.Checked; });
        Item(ctl, "Set FFXI controller to selected &row (simple mode)", SetSelected);

        var view = Top("&View");
        Check(view, "&Controller list", () => padsToggle || padsPanel.Visible, () => NavigateTo("Controllers"));
        Check(view, "&Log", () => detailsPanel.Visible, () => NavigateTo("Activity"));
        Check(view, "&Technical columns", () => advBox.Checked, () => advBox.Checked = !advBox.Checked);

        var tools = Top("&Tools");
        Item(tools, "&Setup...", ShowSetup, Keys.Control | Keys.Oemcomma);
        Item(tools, "POL &input diagnostic...", () =>
        {
            using var dialog = new PrivatePolInputForm(PrivateTrackedPids, Log);
            dialog.ShowDialog(this);
        });
        Item(tools, "PlayOnline &sign-in settings...", ShowPrivateLoginSettings);
        Check(tools, "Write &status file for Claude", () => cfg.ClaudeStatusFile, ToggleStatusFile);

        var help = Top("&Help");
        Item(help, "&Quick start", ShowQuickStart, Keys.F1);
        Item(help, "Open &log folder", () => { try { System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(Config.FilePath) ?? ""); } catch (Exception e) { Log("Could not open the folder: " + e.Message); } });
        Item(help, "Ask &Claude about a problem...", AskClaudeAbout, Keys.Control | Keys.Shift | Keys.H);
        Item(help, "&What is in the Claude bundle", () => MessageBox.Show(this, AskClaude.Contents + "\n\nOffhand sends nothing anywhere. You paste the zip into a Claude chat yourself.", "Claude bundle"));
        Sep(help);
        Item(help, "&About Offhand", ShowAbout);

        ApplyStatusFile();
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    void ShowPrivateLoginSettings()
    {
        using var dialog = new PrivateLoginForm(cfg.Rows.Select(r => r.Label));
        dialog.ShowDialog(this);
    }

    void SaveDiagnostics()
    {
        try
        {
            var report = Diagnostics.Preview(cfg);
            using var preview = new Form { Text = "Preview diagnostics", StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(700, 475), MinimizeBox = false, ShowInTaskbar = false };
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, Text = string.Join("\r\n\r\n", report.Select(entry => $"--- {entry.Key} ---\r\n{entry.Value}")) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            var save = new Button { Text = "Save ZIP…", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { save, cancel });
            preview.Controls.Add(text);
            preview.Controls.Add(buttons);
            preview.Controls.Add(new Label { Dock = DockStyle.Top, Height = 42,
                Text = "Review what will be saved. Known identifiers are redacted. Passwords and sign-in recordings are excluded." });
            preview.CancelButton = cancel;
            if (preview.ShowDialog(this) != DialogResult.OK) return;
            using var destination = new SaveFileDialog { Filter = "ZIP archive|*.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                FileName = $"Offhand-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
            if (destination.ShowDialog(this) != DialogResult.OK) return;
            Diagnostics.SaveReport(destination.FileName, report);
            Log("Diagnostics saved: " + destination.FileName);
        }
        catch (Exception e) { MessageBox.Show(this, "Could not save: " + e.Message, "Save diagnostics"); }
    }

    System.Windows.Forms.Timer? statusTimer;

    void ToggleStatusFile()
    {
        cfg.ClaudeStatusFile = !cfg.ClaudeStatusFile;
        SaveRows();
        ApplyStatusFile();
        Log("Status file for Claude: " + (cfg.ClaudeStatusFile ? "on (" + AskClaude.StatusPath + ")" : "off, file removed"));
    }

    /// Starts or stops the 5-second status-file writer to match the setting. Also called once on startup.
    void ApplyStatusFile()
    {
        if (cfg.ClaudeStatusFile)
        {
            statusTimer ??= new System.Windows.Forms.Timer { Interval = 5000 };
            statusTimer.Tick -= StatusTick; statusTimer.Tick += StatusTick;
            statusTimer.Start(); StatusTick(null, EventArgs.Empty);
        }
        else { statusTimer?.Stop(); AskClaude.RemoveStatus(); }
    }

    void StatusTick(object? s, EventArgs e) => AskClaude.WriteStatus(cfg, SessionActive);

    void AskClaudeAbout()
    {
        try
        {
            var zip = AskClaude.Build(cfg);
            Clipboard.SetText(AskClaude.Prompt);
            Log("Claude bundle saved: " + zip);
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + zip + "\"");
            MessageBox.Show(this, "Saved to your desktop:\n" + zip + "\n\nThe prompt is on your clipboard. Open a Claude chat, attach the zip and paste.\n\n" + AskClaude.Contents, "Ask Claude");
        }
        catch (Exception e) { MessageBox.Show(this, "Could not build the bundle: " + e.Message, "Ask Claude"); }
    }

    void ShowAbout() => MessageBox.Show(this, Diagnostics.AboutText, "About Offhand");

    void ShowQuickStart() => MessageBox.Show(this,
        "1. Turn each controller on (hold the Xbox button) and press a button. Name it in the list so you can tell them apart.\n" +
        "2. Add a character for each player: a name, the Windower profile and the controller that player uses. The top one launches first.\n" +
        "3. Press the green Launch button (Ctrl+L) and log in on each window as it opens.\n\n" +
        "Stop launching: Esc, while a launch is in progress. Menus: Alt + the underlined letter. Everything else is in the menu bar.",
        "Quick start");

    /// Esc stops a launch in progress. Only then, and not while a grid cell is being edited (Esc cancels that edit).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && cancelBtn.Visible && cancelBtn.Enabled && !grid.IsCurrentCellInEditMode && !padList.IsCurrentCellInEditMode)
        {
            cancelBtn.PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

