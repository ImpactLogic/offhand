namespace PadLauncher;

/// The guided main window: a three-step strip that says where you are, a short help sentence, the few buttons a normal session needs,
/// a plain-words status line, and everything else behind "More" and "Details". Nothing was removed: the old controls live in the same
/// places' successors, only tucked away. The aim is to guide, not to control.
public partial class MainForm
{
    readonly Label[] stepLabels = { new(), new(), new() };
    readonly Label helpLine = new() { AutoSize = true, MaximumSize = new Size(840, 0), Padding = new Padding(4, 4, 4, 2) };
    readonly Label statusLine = new() { AutoSize = true, MaximumSize = new Size(840, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(4, 4, 0, 4) };
    readonly FlowLayoutPanel morePanel = new() { Dock = DockStyle.Fill, AutoSize = true, Visible = false, WrapContents = true };
    readonly Panel detailsPanel = new() { Dock = DockStyle.Fill, Height = 150, Visible = false };
    readonly CheckBox advBox = new() { Text = "Show technical columns (address, GUID, profile settings)", AutoSize = true };
    FlowLayoutPanel? characterActions;
    readonly ToolTip tips = new() { AutoPopDelay = 12000 };
    // Main / secondary quality (0.9.0): the main character launches with the Full Windower profile, the others with the Lite one.
    readonly Label qualityLine = new() { AutoSize = true, Margin = new Padding(14, 8, 0, 0) };
    readonly Button mainBtn = new() { AutoSize = true };
    readonly ComboBox fullBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    readonly ComboBox liteBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    const string NoProfile = "(not set)";
    bool fillingQuality;
    Button moreBtn = null!;
    Panel padsPanel = null!;   // the controller list: shown while pads need attention, or on demand (Controllers button)
    bool padsToggle;

    void BuildGuidedLayout(Button refresh, Button up, Button down, Button save, Button setBtn, Button setupBtn, Button diagBtn, Button aboutBtn)
    {
        // plain words on the controls that stay in view
        launchBtn.Text = "Launch all";
        cancelBtn.Text = "Stop launching"; cancelBtn.Visible = false;
        StyleGoStop();
        padList.Columns[1].HeaderText = "Name it (what it looks like, e.g. \"white, tape on back\")";
        padList.Columns[0].HeaderText = "Controller";
        grid.Columns[0].HeaderText = "Character";
        grid.Columns[2].HeaderText = "Controller it uses";

        tips.SetToolTip(launchBtn, "Starts each Windower profile in the order listed, then waits for the game to load before the next one.");
        tips.SetToolTip(swapBtn, "Swap the two pads between the first two windows. No restart; nothing gets stuck.");
        tips.SetToolTip(fwdBox, "Each window gets its own virtual Xbox pad, so a pad that sleeps or is swapped keeps working in the same window. Needs the ViGEmBus and HidHide drivers (see Setup).");
        tips.SetToolTip(cancelBtn, "Stop launching. Windows already started stay open.");
        tips.SetToolTip(refresh, "Look for pads again.");
        tips.SetToolTip(up, "Launch this character earlier.");
        tips.SetToolTip(down, "Launch this character later.");
        tips.SetToolTip(save, "Save now. Finished character edits also save automatically.");
        tips.SetToolTip(setBtn, "Simple mode only: tell FFXI which controller to use for the NEXT client you start by hand.");
        tips.SetToolTip(setupBtn, "Where Windower is, which drivers are installed, and the routine for every session.");
        tips.SetToolTip(diagBtn, "Puts one zip on your desktop to send with a bug report: version, drivers, pads, the log, your settings. No passwords.");
        tips.SetToolTip(aboutBtn, "Version, credits, and the honest note about the game's rules.");

        // ----- three-step strip
        var strip = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Height = 26, Margin = new Padding(0, 0, 0, 2) };
        for (int i = 0; i < 3; i++)
        {
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            stepLabels[i].Dock = DockStyle.Fill; stepLabels[i].TextAlign = ContentAlignment.MiddleCenter;
            stepLabels[i].Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold); stepLabels[i].Margin = new Padding(2);
            strip.Controls.Add(stepLabels[i], i, 0);
        }

        // ----- primary action bar: only what a normal session needs
        var primary = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        primary.Controls.AddRange(new Control[] { launchBtn, cancelBtn, swapBtn, qualityLine, mainBtn, fwdBox, slotStatus });
        fwdBox.Text = "Give each window its own virtual pad (recommended)";
        fwdBox.Margin = new Padding(14, 8, 0, 0);

        // ----- More / Details toggles
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        moreBtn = new Button { Text = "More  v", AutoSize = true, FlatStyle = FlatStyle.Flat };
        var detailsBtn = new Button { Text = "Details  v", AutoSize = true, FlatStyle = FlatStyle.Flat };
        var padsBtn = new Button { Text = "Controllers  v", AutoSize = true, FlatStyle = FlatStyle.Flat };
        padsBtn.Click += (_, _) => { padsToggle = !padsToggle; padsBtn.Text = padsToggle ? "Controllers  ^" : "Controllers  v"; UpdateGuide(); };
        tips.SetToolTip(padsBtn, "Show the list of connected controllers (to name a pad). It also appears by itself when a pad needs attention.");
        moreBtn.Click += (_, _) => { morePanel.Visible = !morePanel.Visible; moreBtn.Text = morePanel.Visible ? "More  ^" : "More  v"; };
        detailsBtn.Click += (_, _) => { detailsPanel.Visible = !detailsPanel.Visible; detailsBtn.Text = detailsPanel.Visible ? "Details  ^" : "Details  v"; };
        // the toggles now live in the menu bar (View, Characters > Quality profiles); the row stays empty so the layout rows keep their numbers
        tips.SetToolTip(moreBtn, "Less common things: quality profiles, refresh, save, export/import, setup, diagnostics, about.");
        tips.SetToolTip(detailsBtn, "The running log, in case something looks wrong.");

        advBox.CheckedChanged += (_, _) => ApplyAdvanced();
        detailsPanel.Controls.Add(logBox);

        // ----- the two lists, with plain headings
        var pc = new Label { Text = "Your controllers: turn a pad on and press a button, its row lights up", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 6, 0, 2) };
        charsHeader = new Label { Text = "Your characters: the top one launches first", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 6, 0, 2) };
        var cc = charsHeader;

        // The list controls sit right under the list: add, remove, reorder.
        var addBtn = new Button { Text = "+ Add character", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(6, 2, 6, 2) };
        up.Text = "Move up"; down.Text = "Move down";
        var exportBtn = new Button { Text = "Export...", AutoSize = true };
        var importBtn = new Button { Text = "Import...", AutoSize = true };
        exportBtn.Click += (_, _) => ExportCharacters();
        importBtn.Click += (_, _) => ImportCharacters();
        tips.SetToolTip(exportBtn, "Save the character list to a file: a backup, or a setup to give someone.");
        tips.SetToolTip(importBtn, "Replace the character list with one saved by Export.");
        addBtn.Click += (_, _) => AddCharacter();
        tips.SetToolTip(addBtn, "Add a row for another character: a name, the Windower profile and the controller it uses.");
        var charBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        characterActions = charBar;
        charBar.Controls.AddRange(new Control[] { addBtn, up, down });  // Remove is on every row; Export/Import live under More
        var qualityBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 2, 0, 2) };
        qualityBar.Controls.AddRange(new Control[] { new Label { Text = "Full-quality profile:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, fullBox,
            new Label { Text = "Lite profile:", AutoSize = true, Margin = new Padding(14, 8, 0, 0) }, liteBox });
        morePanel.Controls.Add(qualityBar);  // everything else that lived here is in the menu bar
        tips.SetToolTip(fullBox, "The Windower profile for the main character (full resolution). Make profiles in Windower; Offhand only picks one.");
        tips.SetToolTip(liteBox, "The Windower profile for every other character (smaller window, lower graphics, less memory). Make it in Windower.");
        fullBox.SelectedIndexChanged += (_, _) => QualityPicked(fullBox, true);
        liteBox.SelectedIndexChanged += (_, _) => QualityPicked(liteBox, false);
        mainBtn.Click += (_, _) => SwitchMain();
        tips.SetToolTip(mainBtn, "Choose which character gets the full-quality window. Takes effect at the next Launch all (a window that is already open keeps its graphics).");
        var charPanel = new Panel { Dock = DockStyle.Fill };
        charPanel.Controls.Add(grid);     // Fill first, then the bottom bar
        charPanel.Controls.Add(charBar);
        UpdateCharCount();

        padsPanel = new Panel { Dock = DockStyle.Fill, Height = 160 };
        padsPanel.Controls.Add(padList);  // Fill first, then the heading
        padsPanel.Controls.Add(pc);
        pc.Dock = DockStyle.Top;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Padding = new Padding(10, 8, 10, 6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // strip
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // help
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // controller list (collapsible)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // characters label
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // character list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // primary
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // toggles
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // more panel
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // details
        var rowsInOrder = new Control[] { strip, helpLine, padsPanel, cc, charPanel, primary, statusLine, toggles, morePanel, detailsPanel };
        for (int i = 0; i < rowsInOrder.Length; i++) root.Controls.Add(rowsInOrder[i], 0, i);
        Controls.Add(root);
        root.SizeChanged += (_, _) => { var w = Math.Max(300, root.ClientSize.Width - 30); helpLine.MaximumSize = new Size(w, 0); statusLine.MaximumSize = new Size(w, 0); };

        slotStatus.MaximumSize = new Size(800, 0);
        ApplyAdvanced();
        UpdateGuide();
    }

    Label charsHeader = null!;

    /// Fill the Full / Lite dropdowns from Windower's profile list (names only; the launcher never writes Windower's settings).
    void FillQualityProfiles()
    {
        fillingQuality = true;
        try
        {
            foreach (var (box, cur) in new[] { (fullBox, cfg.FullProfile), (liteBox, cfg.LiteProfile) })
            {
                box.Items.Clear();
                box.Items.Add(NoProfile);
                box.Items.AddRange(profiles.Keys.Cast<object>().ToArray());
                box.SelectedItem = cur.Length > 0 && profiles.ContainsKey(cur) ? cur : NoProfile;
            }
        }
        finally { fillingQuality = false; }
    }

    void QualityPicked(ComboBox box, bool full)
    {
        if (fillingQuality) return;
        var pick = box.SelectedItem as string ?? NoProfile;
        var name = pick == NoProfile ? "" : pick;
        if (full) cfg.FullProfile = name; else cfg.LiteProfile = name;
        tips.SetToolTip(box, name.Length > 0 && profiles.TryGetValue(name, out var summary) ? summary : box.Tag as string ?? "");
        try { cfg.Save(); } catch (Exception e) { Log("Could not save: " + e.Message); }
        Log($"Quality profiles: full = {(cfg.FullProfile.Length > 0 ? cfg.FullProfile : "(not set)")}, lite = {(cfg.LiteProfile.Length > 0 ? cfg.LiteProfile : "(not set)")}");
        UpdateQuality();
    }

    /// Make the other character the main (full quality). Applies at the next Launch all; a running window keeps the graphics it started with.
    void SwitchMain()
    {
        if (cfg.FullProfile.Length == 0 || cfg.LiteProfile.Length == 0)
        {
            morePanel.Visible = true;
            Explain("Pick a Full-quality profile and a Lite profile under Characters > Quality profiles first. Make the Lite one in Windower (smaller window, lower graphics); Offhand only chooses between profiles.");
            return;
        }
        var names = cfg.Rows.Select(r => r.Label).Where(l => l.Length > 0).ToList();
        var next = names.FirstOrDefault(l => !string.Equals(l, cfg.MainLabel, StringComparison.OrdinalIgnoreCase));
        if (next == null) return;
        cfg.MainLabel = next;
        try { cfg.Save(); } catch (Exception e) { Log("Could not save: " + e.Message); }
        Log($"Main (full quality) is now {next}; the others launch with the lite profile. Applies at the next Launch all.");
        UpdateQuality();
    }

    /// The Quality column, the line beside Launch all, and the switch button.
    void UpdateQuality()
    {
        bool on = cfg.QualityRolesOn;
        foreach (DataGridViewRow r in grid.Rows)
            if (!r.IsNewRow) r.Cells[4].Value = !on ? "" : string.Equals(r.Cells[0].Value as string, cfg.MainLabel, StringComparison.OrdinalIgnoreCase) ? "Main (full)" : "Lite";
        var names = cfg.Rows.Select(r => r.Label).Where(l => l.Length > 0).ToList();
        var next = names.FirstOrDefault(l => !string.Equals(l, cfg.MainLabel, StringComparison.OrdinalIgnoreCase));
        bool profilesSet = cfg.FullProfile.Length > 0 && cfg.LiteProfile.Length > 0;
        qualityLine.Text = on ? $"Full quality: {cfg.MainLabel}" + (SessionActive ? " (changes apply next launch)" : "")
            : profilesSet ? "Full quality: nobody chosen" : "Quality: one profile per character";
        mainBtn.Text = !profilesSet ? "Set up full / lite..." : next == null ? "Make main" : $"Make {next} the main";
        mainBtn.Visible = !profilesSet || next != null;
    }

    /// The heading says how many characters there are, and what the cap is.
    void UpdateCharCount()
    {
        if (charsHeader == null) return;
        int n = grid.Rows.Count;
        charsHeader.Text = $"Your characters ({n}): the top one launches first" + (n > 4 ? "   (this release supports two characters)" : "");
    }

    /// The one action a new player has to find: Launch is big and green, Stop is big and red (same place while launching).
    void StyleGoStop()
    {
        static void Style(Button b, Color on, Color off)
        {
            b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false;
            b.Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f, FontStyle.Bold);
            b.Padding = new Padding(18, 8, 18, 8); b.Margin = new Padding(3, 6, 12, 6);
            b.ForeColor = Color.White; b.BackColor = b.Enabled ? on : off;
            b.FlatAppearance.BorderSize = 0;
        }
        Style(launchBtn, Color.FromArgb(30, 150, 70), Color.FromArgb(150, 175, 155));
        Style(cancelBtn, Color.FromArgb(198, 40, 40), Color.FromArgb(185, 150, 150));
    }

    // Screenshot aid only: open the More and Details panels.
    public void ExpandForScreenshot() { morePanel.Visible = true; detailsPanel.Visible = true; advBox.Checked = true; }

    void ApplyAdvanced()
    {
        bool adv = advBox.Checked;
        padList.Columns[2].Visible = adv; padList.Columns[3].Visible = adv;
        grid.Columns[3].Visible = adv;
    }

    /// Recomputed on every 2 s tick: which step you are on, what to do next, and one plain sentence about the state of things.
    void UpdateGuide()
    {
        var rows = cfg.Rows.Where(r => r.Profile.Length > 0 && r.PadAddress.Length > 0).ToList();
        var configured = cfg.Rows.Select(r => r.PadAddress).Where(a => a.Length > 0).Distinct().ToList();
        var present = pads.Select(p => p.Address).ToHashSet();
        bool padsDone = pads.Count > 0 && configured.All(present.Contains);
        bool charsDone = rows.Count > 0;
        bool playing = clientPids.Values.Any(ids => ids.Any(IsAlive));
        bool launching = running != null;
        padsPanel.Visible = padsToggle || !padsDone;
        UpdateQuality();
        grid.Enabled = !SessionActive;
        if (characterActions != null) characterActions.Enabled = !SessionActive;
        launchBtn.Enabled = !SessionActive;
        launchBtn.Text = cfg.Rows.Count == 0 ? "\u25B6  Launch (add a character first)" : "\u25B6  Launch " + (cfg.Rows.Count == 1 ? "1 character" : cfg.Rows.Count + " characters");
        StyleGoStop();
        fwdBox.Enabled = !SessionActive;

        stepLabels[0].Text = pads.Count == 0 ? "1  Pads: none on yet" : $"1  Pads: {pads.Count} connected";
        stepLabels[1].Text = charsDone ? $"2  Characters: {rows.Count} set up" : "2  Characters: none yet";
        stepLabels[2].Text = playing ? "3  Playing" : "3  Play";
        int current = !padsDone ? 0 : !charsDone ? 1 : 2;
        bool[] done = { padsDone, charsDone, playing };
        for (int i = 0; i < 3; i++)
        {
            stepLabels[i].BackColor = Color.Transparent;  // flat text, not boxes: green = done, amber = do this next, grey = later
            stepLabels[i].ForeColor = done[i] ? Color.FromArgb(30, 110, 50) : i == current ? Color.FromArgb(160, 100, 0) : SystemColors.GrayText;
            stepLabels[i].Text = (done[i] ? "\u2713 " : "") + stepLabels[i].Text;
        }

        helpLine.Text = launching ? "Log in on the window that just opened. We will open the next character once FFXI loads." : playing
            ? "You're playing. Swap the pads any time with Swap pads. Quit the window you launched LAST first, and use Exit in the tray icon when you're done (that gives your pads back to Steam)."
            : current == 0
                ? "Turn your pads on (hold the Xbox button), then press a button on one: its row lights up. Name each pad so you can tell them apart."
                : current == 1
                    ? "Press + Add character under the list for each player: a name, the Windower profile, and the controller that player uses. Remove drops a row; the top row launches first."
                    : fwdBox.Checked
                        ? "Ready. Press Launch all and log in on each window; the title bar says whose window is whose."
                        : "Ready. Press Launch all. For pad swapping and pads that survive sleep, also tick \"Give each window its own virtual pad\".";

        cancelBtn.Visible = launching;
        statusLine.Text = PlainStatus(configured, present, launching);
        statusLine.ForeColor = statusLine.Text.StartsWith("Heads up") || statusLine.Text.Contains("turn it on") || statusLine.Text.StartsWith("Windower") ? Color.Firebrick : Color.FromArgb(30, 110, 50);
    }

    string PlainStatus(List<string> configured, HashSet<string> present, bool launching)
    {
        if (!File.Exists(cfg.WindowerExe)) return "Windower isn't set up yet: open Tools > Setup to point the launcher at Windower.exe.";
        if (launching) return running?.Status ?? "Preparing your characters...";
        if (fwd == null)
            return cfg.UseVirtual ? "Starting the virtual pads..." : "Simple mode: each window gets the pad chosen for it when it starts. Tick the box above for live swapping.";
        var off = configured.Where(a => !present.Contains(a)).Select(a => cfg.NoteFor(a) is { Length: > 0 } n ? n : a).ToList();
        if (off.Count > 0) return $"{string.Join(" and ", off)} {(off.Count == 1 ? "is" : "are")} off: turn {(off.Count == 1 ? "it" : "them")} on.";
        bool lowSlots = Enumerable.Range(0, fwd.Count).All(i => SessionChecks.ValidSlot(fwd.XInputSlot(i), i));
        if (!lowSlots) return "Heads up: your pads were on when the launcher started. Finish any open session, turn your pads off, and restart Offhand.";
        return hideReady ? "Ready: your pads are connected and hidden from the game, so each window only sees its own virtual pad."
             : hideStatus.Contains("learning") ? "A pad just came back: one moment while the launcher finds it again..."
             : "Controllers are not ready yet. Check Setup and wait for both pads to be connected and hidden.";
    }

    static bool IsAlive(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; } catch { return false; }
    }

    /// Before the virtual pads are created the real pads should be off (they take the first XInput slots otherwise).
    bool AskPadsOff()
    {
        if (Pads.List().Count == 0) return true;
        var r = MessageBox.Show(this,
            "Your pads are on right now. For each window to get its own virtual pad, the pads must be OFF while this starts.\n\n" +
            "1. Turn your pads off (hold the Xbox button until the light goes out).\n2. Click OK.\n3. After a few seconds, turn them back on.",
            "Turn your pads off first", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (r != DialogResult.OK) return false;
        statusLine.Text = "Waiting for your pads to turn off...";
        for (int i = 0; i < 30 && Pads.List().Count > 0; i++) { Application.DoEvents(); Thread.Sleep(500); }
        if (Pads.List().Count > 0) { Explain("Your pads are still on. Turn them off, then try enabling virtual pads again."); return false; }
        return true;
    }
}
