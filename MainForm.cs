namespace PadLauncher;

public partial class MainForm : Form
{
    readonly bool firstRun = !File.Exists(Config.FilePath);  // before Load: no config.json yet means a new user
    readonly Config cfg = Config.Load();
    List<Pad> pads = new();
    // Connected controllers. Only the Notes column is editable: the user's own words for the physical pad.
    readonly DataGridView padList = new() { BackgroundColor = SystemColors.Window, Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly DataGridView grid = new() { BackgroundColor = SystemColors.Window, Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersWidth = 30, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly TextBox logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly System.Windows.Forms.Timer identify = new() { Interval = 150 };
    // Connection log (IDEAS #38): polls the pad list and writes connects, disconnects and id changes to launcher.log.
    readonly System.Windows.Forms.Timer watch = new() { Interval = 2000 };
    Dictionary<string, Pad>? seen;
    bool padsChanged;
    string? lastFfxi;
    readonly Button launchBtn = new() { Text = "Launch all", AutoSize = true };

    readonly Button cancelBtn = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    CancellationTokenSource? cts;
    Dictionary<string, string> profiles = new();
    Launcher? running;
    // Virtual pad layer: one ViGEm pad per client slot, fed by the physical pad assigned to that slot (tray menu).
    Forwarder? fwd;
    readonly CheckBox fwdBox = new() { Text = "Forward through virtual pads (tray)", AutoSize = true };
    readonly NotifyIcon tray = new() { Icon = AppIcon.Make(), Text = "Offhand" };
    readonly ContextMenuStrip trayMenu = new();
    bool exiting;
    bool restoringForwarding;
    bool initialized;
    bool savingRows;
    // Asked many times per 2 s tick (UpdateGuide alone asks six times); each ask used to list every process on the machine and leave the
    // Process objects for the finalizer. One answer is reused for a second, and the objects are disposed.
    long polCheckedAt = -10_000;
    bool polRunning;
    bool SessionActive
    {
        get
        {
            if (running != null) return true;
            var now = Environment.TickCount64;
            if (now - polCheckedAt >= 1000)
            {
                var found = System.Diagnostics.Process.GetProcessesByName("pol");
                polRunning = found.Length > 0;
                foreach (var p in found) p.Dispose();
                polCheckedAt = now;
            }
            return polRunning;
        }
    }
    void Explain(string message) { Log(message); MessageBox.Show(this, message, "Offhand", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    readonly HashSet<string> hiddenPaths = new(StringComparer.OrdinalIgnoreCase);  // device paths we hid; unhidden on stop
    string hideStatus = "";
    bool hideReady;
    bool relearning;  // cloak lifted for a moment so a reconnected pad can be found again
    readonly Dictionary<int, int[]> clientPids = new();  // row index -> pol.exe id, from the last Launch all
    const uint WM_SETTEXT = 0x000C, SMTO_BLOCK = 0x0001, SMTO_ABORTIFHUNG = 0x0002;
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeoutMs, out IntPtr result);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hWnd);
    readonly Button swapBtn = new() { Text = "Swap pads", AutoSize = true, Enabled = false };
    readonly Label slotStatus = new() { AutoSize = true, Visible = false, Padding = new Padding(8, 6, 0, 0) };

    public MainForm()
    {
        Text = "Offhand " + Diagnostics.Version;
        Icon = AppIcon.Make();
        Width = 900; Height = 720;

        padList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Controller", ReadOnly = true, FillWeight = 28 });
        padList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notes (what it looks like; click to edit)", FillWeight = 34 });
        padList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Address", ReadOnly = true, FillWeight = 16 });
        padList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DirectInput GUID", ReadOnly = true, FillWeight = 22 });
        padList.CellEndEdit += (_, e) => NoteEdited(e.RowIndex);

        var labelCol = new DataGridViewTextBoxColumn { HeaderText = "Character (label)", DataPropertyName = "Label", FillWeight = 20 };
        var profCol = new DataGridViewComboBoxColumn { HeaderText = "Windower profile", FillWeight = 36, FlatStyle = FlatStyle.Flat };
        var padCol = new DataGridViewComboBoxColumn { HeaderText = "Controller", FillWeight = 46, FlatStyle = FlatStyle.Flat };
        // Read-only: what the profile does, from Windower's settings.xml. Edit profiles in Windower, not here.
        var infoCol = new DataGridViewTextBoxColumn { HeaderText = "Profile settings (edit in Windower)", ReadOnly = true, FillWeight = 35 };
        infoCol.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        // Per-row remove: the obvious way to drop a character (the Delete key still works too).
        var removeCol = new DataGridViewButtonColumn { HeaderText = "", Text = "Remove", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 80 };
        // Read-only: which quality this character launches with (Main = full, Lite = reduced), see the Quality line below the list.
        var qualCol = new DataGridViewTextBoxColumn { HeaderText = "Quality", ReadOnly = true, FillWeight = 16 };
        grid.Columns.AddRange(labelCol, profCol, padCol, infoCol, qualCol, removeCol);
        grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == removeCol.Index) RemoveCharacter(e.RowIndex); };
        grid.RowsAdded += (_, _) => UpdateCharCount();
        grid.RowsRemoved += (_, _) => UpdateCharCount();
        grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 1) ShowProfileInfo(grid.Rows[e.RowIndex]); };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellEndEdit += (_, _) => { if (initialized && !savingRows && !SessionActive) BeginInvoke(() => { if (!savingRows && !SessionActive) { SaveRows(); UpdateGuide(); } }); };
        grid.DataError += (_, e) => e.ThrowException = false;

        var refresh = new Button { Text = "Refresh controllers", AutoSize = true };
        var setBtn = new Button { Text = "Set FFXI controller to selected row", AutoSize = true };
        var up = new Button { Text = "Up", AutoSize = true };
        var down = new Button { Text = "Down", AutoSize = true };
        var save = new Button { Text = "Save", AutoSize = true };
        refresh.Click += (_, _) => { Pads.InvalidatePathCache(); RefreshPads(); };
        setBtn.Click += (_, _) => SetSelected();
        up.Click += (_, _) => MoveRow(-1);
        down.Click += (_, _) => MoveRow(1);
        save.Click += (_, _) => { SaveRows(); Log("Saved " + Config.FilePath); };
        launchBtn.Click += async (_, _) => await LaunchAll();

        cancelBtn.Click += (_, _) => { if (running != null && !running.CanStop) { Explain("Close the PlayOnline window that is still waiting to enter FFXI, then press Stop launching. If the window has just opened, give it a few seconds to finish starting. Its controller assignment stays held until then."); return; } if (MessageBox.Show(this, "Stop launching? Close unfinished PlayOnline windows before logging in again: their controller assignment will be released. Characters already in FFXI can keep playing.", "Stop launching", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) cts?.Cancel(); };
        identify.Tick += (_, _) => Highlight();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var setupBtn = new Button { Text = "Setup...", AutoSize = true };
        setupBtn.Click += (_, _) => ShowSetup();
        var diagBtn = new Button { Text = "Save diagnostics", AutoSize = true };
        diagBtn.Click += (_, _) => SaveDiagnostics();
        var aboutBtn = new Button { Text = "About", AutoSize = true };
        aboutBtn.Click += (_, _) => ShowAbout();
        buttons.Controls.AddRange(new Control[] { refresh, up, down, save, setBtn, launchBtn, cancelBtn, fwdBox, swapBtn, slotStatus, setupBtn, diagBtn, aboutBtn });
        if (firstRun) Shown += (_, _) => ShowSetup();
        swapBtn.Click += (_, _) => { if (fwd != null && fwd.Count >= 2) { fwd.Swap(0, 1); ReflectSlots(); UpdateSlotStatus(); } };
        fwdBox.Checked = cfg.UseVirtual;
        fwdBox.CheckedChanged += (_, _) => { if (restoringForwarding) return; if (running != null || (SessionActive && !fwdBox.Checked)) { restoringForwarding = true; fwdBox.Checked = cfg.UseVirtual; restoringForwarding = false; Explain("Finish this session before changing virtual pads. You can still swap controllers while playing."); return; } cfg.UseVirtual = fwdBox.Checked; if (fwdBox.Checked) { if (!AskPadsOff()) { fwdBox.Checked = false; return; } StartForwarding(); } else StopForwarding(); SaveRows(); };
        tray.ContextMenuStrip = trayMenu;
        trayMenu.Opening += (_, _) => BuildTrayMenu();
        tray.DoubleClick += (_, _) => ShowWindow();
        tray.Visible = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.Controls.Add(new Label { Text = "Controllers (press a button on one to highlight it; type in Notes to name it, e.g. \"white, tape on back\"):", AutoSize = true }, 0, 0);
        layout.Controls.Add(padList, 0, 1);
        layout.Controls.Add(new Label { Text = "Launch order (top first): who plays with which controller", AutoSize = true }, 0, 2);
        layout.Controls.Add(grid, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        layout.Controls.Add(logBox, 0, 5);
        BuildGuidedLayout(refresh, up, down, save, setBtn, setupBtn, diagBtn, aboutBtn);
        BuildMenuBar();  // added after the layout so the window content fills what is left below it

        RefreshPads();
        profiles = cfg.ProfileSummaries();
        profCol.Items.AddRange(profiles.Keys.Cast<object>().ToArray());
        foreach (var r in cfg.Rows) grid.Rows.Add(r.Label, (profCol.Items.Contains(r.Profile) ? r.Profile : null)!, PadLabel(r.PadAddress)!);
        foreach (DataGridViewRow r in grid.Rows) ShowProfileInfo(r);
        FillQualityProfiles();
        UpdatePadChoices();
        identify.Start();
        watch.Tick += (_, _) =>
        {
            Watchdog.Beat();
            Watchdog.Step("WatchPads"); WatchPads();
            Watchdog.Step("HideRealPads"); HideRealPads(false);
            Watchdog.Step("UpdateSlotStatus"); UpdateSlotStatus();
            Watchdog.Step("MarkClients"); MarkClients();
            Watchdog.Step("UpdateGuide"); UpdateGuide();
            Watchdog.Beat();
        };
        Pads.Log = Log;
        watch.Start();
        Watchdog.Start(() => (fwd == null ? "forwarding off" : $"forwarding {fwd.Stats.Ticks} passes, longest {fwd.Stats.MaxMs:F1} ms") + "; " + ClientMemory());
        WatchPads();
        RecoverHidden();
        if (Config.RecoveryNotice != null) Shown += (_, _) => Explain(Config.RecoveryNotice);
        Log($"FFXI controller now: {Pads.ReadFfxiPad() ?? "(none)"}");
        FormClosing += (_, e) =>
        {
            if (running != null && (e.CloseReason != CloseReason.UserClosing || exiting)) { e.Cancel = true; Explain("Stop the unfinished launch before exiting Offhand. Close its waiting PlayOnline window first."); return; }
            SaveRows();
            if (!exiting && e.CloseReason == CloseReason.UserClosing)  // the window's X only hides it: forwarding keeps running (live clients depend on the virtual pads); File > Exit quits
            {
                e.Cancel = true; Hide();
                if (!trayHintShown) { trayHintShown = true; tray.ShowBalloonTip(4000, "Offhand is still running", "It lives in the tray. Use File > Exit Offhand to quit.", ToolTipIcon.Info); }
                return;
            }
            StopForwarding();
            tray.Visible = false;
        };
        initialized = true;
        if (cfg.UseVirtual) Shown += (_, _) => { if (running == null && AskPadsOff()) StartForwarding(); };  // 0.9.5: also with game windows already open (a restart after a Offhand crash)
    }

    /// A client past this many private bytes is near the native-heap crash seen at about 2.7 GB (IDEAS #219).
    const long RestartRemindMb = 2300;
    readonly HashSet<int> restartReminded = new();

    /// Private bytes of every running client (pol.exe), for the 10-minute alive line: a long session leaves a record of memory growth.
    /// Also says once per client per run, in the window and the log, when it passes about 2.3 GB, so a restart can be picked at a quiet moment.
    /// Read-only: only the process list and its memory counter are read; nothing is sent to a client.
    string ClientMemory()
    {
        try
        {
            var parts = new List<string>();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("pol"))
                using (p)
                {
                    try
                    {
                        long mb = p.PrivateMemorySize64 / 1048576;
                        parts.Add($"pol {p.Id} {mb} MB");
                        if (mb >= RestartRemindMb && restartReminded.Add(p.Id))
                            Log($"RESTART REMINDER: game client pol {p.Id} is at {mb} MB. It crashed near 2700 MB before, so restart it at a quiet moment.");
                    }
                    catch { }
                }
            return (parts.Count > 0 ? string.Join(", ", parts) : "no clients") + ClientZones();
        }
        catch { return "clients ?"; }
    }

    /// Zone of each character, from the files the memwatch add-on writes (addons\memwatch\data\zone_<Name>.txt: "id|name|unix time"),
    /// so a memory step can be matched to a zone. Empty when memwatch is not loaded. Read-only.
    string ClientZones()
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(cfg.WindowerExe) ?? "", "addons", "memwatch", "data");
            if (!Directory.Exists(dir)) return "";
            var zones = new List<string>();
            foreach (var file in Directory.GetFiles(dir, "zone_*.txt"))
            {
                var bits = File.ReadAllText(file).Split('|');
                if (bits.Length < 3 || !long.TryParse(bits[2], out var t)) continue;
                var age = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - t) / 3600;
                zones.Add($"{Path.GetFileNameWithoutExtension(file)[5..]} in {bits[1]} (since {age} h ago)");
            }
            return zones.Count > 0 ? "; zone " + string.Join(", ", zones) : "";
        }
        catch { return ""; }
    }
    /// Shows which physical pad feeds which client right now (live), so a swap is visible and checkable.
    void UpdateSlotStatus()
    {
        swapBtn.Enabled = fwd != null && fwd.Count >= 2; slotStatus.Visible = fwd != null;
        if (fwd == null) return;
        var parts = new List<string>();
        for (int i = 0; i < fwd.Count; i++)
        {
            var a = fwd.AddressOf(i);
            var who = i < cfg.Rows.Count && cfg.Rows[i].Label.Length > 0 ? cfg.Rows[i].Label : "client " + (i + 1);
            var pad = a == null ? "(no pad)" : (cfg.NoteFor(a) is { Length: > 0 } n ? n : a) + (fwd.Connected.ContainsKey(a) ? "" : " (off)");
            parts.Add($"{who} <- {pad}");
        }
        slotStatus.Text = "Your pads: " + string.Join("   |   ", parts) + "     " + hideStatus;
        tray.Text = $"Offhand: forwarding {fwd.Count} pads";
    }

    /// Put "<label> | <pad>" in each launched client's window title (an OS call on the window; nothing is read from or written to the game).
    /// Windower sets its own title after login, so this is re-applied every 2 s.
    /// SetWindowText on another process's window waits for that process, so a frozen game window froze Offhand's window thread too
    /// (Offhand "stopped responding" on 10/11, 10/12 and 10/13 each lined up with a pol.exe hang). The titles are now worked out on the
    /// window thread but applied on a pool thread, through SendMessageTimeout that gives up on a hung window after 250 ms.
    int markBusy;
    void MarkClients()
    {
        if (!cfg.MarkClients) return;
        if (running != null) foreach (var (k, v) in running.ClientPids) clientPids[k] = v;  // live: tag while the launch is still running
        var work = new List<(int Pid, string Title)>();
        foreach (var (i, pids) in clientPids.ToList())
        {
            if (i >= cfg.Rows.Count) continue;
            var row = cfg.Rows[i];
            var addr = fwd != null && i < fwd.Count ? fwd.AddressOf(i) ?? "" : row.PadAddress;
            var pad = addr.Length == 0 ? "no pad" : cfg.NoteFor(addr) is { Length: > 0 } n ? n : addr;
            var title = $"{(row.Label.Length > 0 ? row.Label : "client " + (i + 1))} | {pad}" + (fwd != null ? $" (virtual {i + 1})" : "") + (cfg.QualityRolesOn ? (cfg.IsMain(row) ? " [main]" : " [lite]") : "");
            foreach (var pid in pids) work.Add((pid, title));
        }
        if (work.Count == 0 || Interlocked.CompareExchange(ref markBusy, 1, 0) != 0) return;  // one pass at a time: a stuck one is not stacked on
        Task.Run(() =>
        {
            try
            {
                foreach (var (pid, title) in work)
                {
                    try
                    {
                        using var p = System.Diagnostics.Process.GetProcessById(pid);
                        if (p.HasExited) continue;
                        var h = p.MainWindowHandle;
                        if (h == IntPtr.Zero || IsHungAppWindow(h)) continue;
                        if (p.MainWindowTitle != title) SendMessageTimeout(h, WM_SETTEXT, IntPtr.Zero, title, SMTO_ABORTIFHUNG | SMTO_BLOCK, 250, out _);
                    }
                    catch { }
                }
            }
            finally { Interlocked.Exchange(ref markBusy, 0); }
        });
    }

    // The 2 s tick used to start HidHideCLI.exe three times per pass even when nothing had changed (about 130,000 process starts in a 24 h
    // session, on the window thread). Once every pad is hidden and the cloak is confirmed, the full check now repeats only when the set of
    // pads (connected / known to the reader) changes, or once a minute as a safety net.
    string hideSig = "";
    long hideSigAt;
    bool hideSteady;

    void HideRealPads(bool first)
    {
        if (fwd == null) { hideReady = false; hideSteady = false; return; }
        if (!cfg.HidePads || !HidHide.Installed) { hideReady = false; hideSteady = false; hideStatus = HidHide.Installed ? "Real pads hidden from games: no (off in settings)" : "Real pads hidden from games: no (HidHide not installed)"; return; }
        var addrs = cfg.Rows.Select(r => r.PadAddress).Concat(Enumerable.Range(0, fwd.Count).Select(i => fwd.AddressOf(i) ?? "")).Where(a => a.Length > 0).Distinct().ToList();
        var sig = string.Join('|', addrs.Select(a => a + (fwd.Connected.ContainsKey(a) ? "+" : "-") + (fwd.RawKnows(a) ? "k" : "u")));
        if (!first && hideSteady && sig == hideSig && Environment.TickCount64 - hideSigAt < 60_000) return;  // nothing changed: keep hideReady / hideStatus as they were
        hideSteady = false;
        hideReady = false;
        try
        {
            if (first)
            {
                HidHide.AllowApp(Environment.ProcessPath!);
                foreach (var steam in new[] { @"C:\Program Files (x86)\Steam\steam.exe", @"C:\Program Files\Steam\steam.exe" }) HidHide.AllowApp(steam);
            }
            // Windows' Raw Input opens a pad when it ARRIVES; a pad that arrives while the cloak is on is never listed by Raw Input (not even for
            // us), and nothing reads it in the background (measured 2026-10-06: the XInput slot still moved, Raw Input listed only the virtual pads).
            // So the cloak is lifted as soon as a configured pad is missing, and goes back on only when every pad is present and known to the reader.
            if (!first && addrs.Any(a => !fwd.Connected.ContainsKey(a)))
            {
                if (!relearning) { relearning = true; HidHide.SetCloak(false); Log("HIDE a pad is off: cloak lifted so it comes back visible to Windows"); }
                hideStatus = "Real pads hidden from games: paused (a pad is off)";
                return;
            }
            if (!first && addrs.Any(a => !fwd.RawKnows(a)))
            {
                fwd.RawRescan();
                hideStatus = relearning ? "Real pads hidden from games: paused (learning a returned pad; if it stays, turn that pad off and on)" : "Real pads hidden from games: no (learning pads)";
                return;
            }
            if (relearning) { relearning = false; HidHide.SetCloak(true); Log("HIDE all pads known again: cloak on"); }
            int added = 0;
            foreach (var a in addrs.Where(a => fwd.RawKnows(a)))  // hide a pad only after the background reader knows it
                foreach (var path in HidHide.DevicePathsFor(a))
                    if (!hiddenPaths.Contains(path)) { hiddenPaths.Add(path); cfg.HiddenPaths = hiddenPaths.ToList(); cfg.Save(); HidHide.Hide(path); added++; }
            if (added > 0) Log($"HIDE hid {added} device(s) of the real pads from games (Steam and Offhand can still see them)");
            if (added > 0) HidHide.SetCloak(true);  // never cloak with nothing to hide: a pad arriving into a cloaked system is never listed by Raw Input
            hideReady = hiddenPaths.Count > 0 && HidHide.CloakOn;
            hideStatus = hiddenPaths.Count > 0 ? $"Real pads hidden from games: yes ({hiddenPaths.Count} devices)" : "Real pads hidden from games: no (no pad connected yet)";
            hideSig = sig; hideSigAt = Environment.TickCount64; hideSteady = hideReady;  // only a fully hidden, cloaked state counts as steady
        }
        catch (Exception e) { hideStatus = "Real pads hidden from games: error"; Log("HIDE error: " + e.Message); }
    }

    void UnhideRealPads()
    {
        if (!HidHide.Installed) return;
        try
        {
            HidHide.SetCloak(false);
            foreach (var path in hiddenPaths.ToList()) { HidHide.Unhide(path); hiddenPaths.Remove(path); cfg.HiddenPaths = hiddenPaths.ToList(); cfg.Save(); }
            Log("HIDE cleanup complete: controllers released to Windows");
            hideReady = false; hideSteady = false;
            hideStatus = "";
        }
        catch (Exception e) { Log("HIDE error while undoing: " + e.Message); }
    }

    /// A crash or kill while forwarding would leave the pads hidden from every other program. Undo that on the next start.
    void RecoverHidden()
    {
        if (cfg.HiddenPaths.Count == 0 || !HidHide.Installed) return;
        foreach (var path in cfg.HiddenPaths.ToList()) hiddenPaths.Add(path);
        Log($"HIDE last session did not clean up: unhiding {hiddenPaths.Count} device(s)");
        UnhideRealPads();
    }

    void ShowSetup()
    {
        if (SessionActive) { Explain("Finish this session before changing setup. Live controller swaps are still available."); return; }
        using var f = new SetupForm(cfg);
        f.ShowDialog(this);
        profiles = cfg.ProfileSummaries();  // the Windower path may have changed
        var col = (DataGridViewComboBoxColumn)grid.Columns[1];
        col.Items.Clear();
        col.Items.AddRange(profiles.Keys.Cast<object>().ToArray());
        FillQualityProfiles();
        RefreshPads();
    }

    void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    /// Create the virtual pads (slot 1, then 2, ...) and start forwarding. Slots = max(2, rows), kept alive until Exit.
    void StartForwarding()
    {
        if (fwd != null) return;
        SaveRows();
        try
        {
            if (HidHide.Installed && HidHide.CloakOn) throw new InvalidOperationException("HidHide is already active. Finish the other controller-hiding session before starting Offhand.");
            // XInput has four slots and the real pads need room too: at most 4 virtual pads, and never fewer than 2 (a spare for a swap).
            if (cfg.Rows.Count > 2) throw new InvalidOperationException("This release supports up to two characters.");
            var addrs = Enumerable.Range(0, 2).Select(i => i < cfg.Rows.Count ? cfg.Rows[i].PadAddress : null).ToList();
            LoggedPad.Verbose = cfg.VerboseInput;
            fwd = new Forwarder(addrs, Log, Handle);
            for (int i = 0; i < fwd.Count; i++) Pads.Hidden.Add(fwd.VirtualGuid(i));
            HideRealPads(true);
            for (int i = 0; i < fwd.Count; i++) Log($"FORWARD slot {i + 1}: XInput slot {fwd.XInputSlot(i)}");
            if (Enumerable.Range(0, fwd.Count).Any(i => fwd.XInputSlot(i) > 1))
                Log("FORWARD WARNING: the virtual pads did not get XInput slots 0 and 1 because real pads were already on. Games that read XInput only see slots 0 and 1. Turn the pads OFF, restart Offhand, then turn them on.");
            UpdateSlotStatus();
            for (int i = 0; i < fwd.Count; i++) Log($"FORWARD slot {i + 1}: virtual pad {Pads.Format(fwd.VirtualGuid(i))} <- {fwd.AddressOf(i) ?? "(no pad)"}");
        }
        catch (Exception e)
        {
            Log("FORWARD cannot start: " + e.Message + " (is ViGEmBus installed?)");
            fwdBox.Checked = false;
        }
    }

    void StopForwarding()
    {
        if (fwd == null) return;
        var (ticks, avg, max) = fwd.Stats;
        Log($"FORWARD stopped: {ticks} passes, average {avg:F3} ms, longest {max:F2} ms; virtual pads removed (running clients lose their pad now)");
        try { fwd.Dispose(); }
        finally { UnhideRealPads(); }
        fwd = null; Pads.Hidden.Clear();
        UpdateSlotStatus();
    }

    /// Apply the rows' controller choices to the slots (row i = slot i). Called after the rows are saved.
    void SyncAssignments()
    {
        if (fwd == null) return;
        if (cfg.Rows.Count > fwd.Count) Log($"FORWARD has {fwd.Count} slots; rows {fwd.Count + 1}+ need forwarding turned off and on again");
        for (int i = cfg.Rows.Count; i < fwd.Count; i++) if (fwd.AddressOf(i) != null) fwd.Assign(i, null);
        for (int i = 0; i < Math.Min(fwd.Count, cfg.Rows.Count); i++)
        {
            var a = cfg.Rows[i].PadAddress;
            if ((string.IsNullOrEmpty(a) ? null : a) != fwd.AddressOf(i)) fwd.Assign(i, a);
        }
    }

    /// Write the slots' pads back into the grid rows, after a change from the tray menu.
    void ReflectSlots()
    {
        if (fwd == null) return;
        for (int i = 0; i < Math.Min(fwd.Count, grid.Rows.Count); i++)
            grid.Rows[i].Cells[2].Value = PadLabel(fwd.AddressOf(i) ?? "");
        SaveRows();
    }

    void BuildTrayMenu()
    {
        foreach (var old in trayMenu.Items.Cast<ToolStripItem>().ToList()) { trayMenu.Items.Remove(old); old.Dispose(); }  // Clear() leaves the items (and their handlers) for the finalizer
        trayMenu.Items.Add("Open Offhand", null, (_, _) => ShowWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        if (fwd == null)
        {
            trayMenu.Items.Add("Start forwarding (create virtual pads)", null, (_, _) => fwdBox.Checked = true);
        }
        else
        {
            var connected = Pads.List();
            for (int i = 0; i < fwd.Count; i++)
            {
                int slot = i;
                var who = slot < cfg.Rows.Count && cfg.Rows[slot].Label.Length > 0 ? cfg.Rows[slot].Label : "client " + (slot + 1);
                var cur = fwd.AddressOf(slot);
                var curText = cur == null ? "(no pad)" : cfg.NoteFor(cur) is { Length: > 0 } n ? n : cur;
                var m = new ToolStripMenuItem($"Slot {slot + 1} {who}: {curText}" + (cur != null && connected.All(p => p.Address != cur) ? " (off)" : ""));
                foreach (var p in connected)
                {
                    var item = new ToolStripMenuItem(cfg.PadLabel(p)) { Checked = p.Address == cur };
                    item.Click += (_, _) => { fwd.Assign(slot, p.Address); ReflectSlots(); };
                    m.DropDownItems.Add(item);
                }
                var none = new ToolStripMenuItem("(no pad)");
                none.Click += (_, _) => { fwd.Assign(slot, null); ReflectSlots(); };
                m.DropDownItems.Add(none);
                trayMenu.Items.Add(m);
            }
            for (int i = 0; i + 1 < fwd.Count; i++)
            {
                int a = i;
                trayMenu.Items.Add($"Swap slot {a + 1} and slot {a + 2}", null, (_, _) => { fwd.Swap(a, a + 1); ReflectSlots(); });
            }
            trayMenu.Items.Add("Stop forwarding (removes the virtual pads)", null, (_, _) => fwdBox.Checked = false);
        }
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApp());
    }

    bool trayHintShown;

    /// Quit Offhand (File > Exit and the tray's Exit). Exiting removes the virtual pads, so a running client would lose its controller: ask first.
    /// A PlayOnline window with no game in it (they stay behind after a client quits) can be closed here too. Nothing is killed: the window is
    /// asked to close. A process whose modules cannot be read counts as a live client and is never touched.
    void ExitApp()
    {
        var pols = System.Diagnostics.Process.GetProcessesByName("pol").ToList();
        var live = new List<System.Diagnostics.Process>();
        var idle = new List<System.Diagnostics.Process>();
        foreach (var p in pols)
        {
            var t = Task.Run(() => Launcher.IsGameLoaded(p));  // off the window thread: reading modules of a stuck process can block
            bool? loaded = t.Wait(2000) ? t.Result : null;
            string responding; try { responding = p.Responding.ToString(); } catch { responding = "?"; }
            Log($"EXIT pol {p.Id}: game loaded = {(loaded == null ? "unknown" : loaded.ToString())}, responding = {responding}");
            if (loaded == false) idle.Add(p); else live.Add(p);
        }
        if (fwd != null && live.Count > 0 &&
            MessageBox.Show(this, "Game clients are still running. Exiting removes the virtual pads, so they lose their controllers until you start again.\n\nExit anyway?",
                "Offhand", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (idle.Count > 0)
        {
            var r = MessageBox.Show(this, $"{idle.Count} PlayOnline window{(idle.Count == 1 ? " is" : "s are")} still open with no game in {(idle.Count == 1 ? "it" : "them")} (they are left behind when a client quits).\n\nClose {(idle.Count == 1 ? "it" : "them")} too?",
                "Offhand", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes)
            {
                foreach (var p in idle) { try { p.CloseMainWindow(); } catch { } }
                for (int i = 0; i < 8 && idle.Any(p => { try { return !p.HasExited; } catch { return false; } }); i++) { Application.DoEvents(); Thread.Sleep(500); }
                foreach (var p in idle) { try { if (!p.HasExited) Log($"EXIT pol {p.Id} did not close when asked; close it from Task Manager"); } catch { } }
            }
        }
        foreach (var p in pols) p.Dispose();
        exiting = true; Close();
    }

    void ShowProfileInfo(DataGridViewRow r)
    {
        if (r.IsNewRow) return;
        r.Cells[3].Value = r.Cells[1].Value is string p && profiles.TryGetValue(p, out var s) ? s : "";
        UpdateQuality();
    }

    string? PadLabel(string address)
    {
        var pad = pads.FirstOrDefault(p => p.Address == address);
        if (pad != null) return cfg.PadLabel(pad);
        if (address.Length == 0) return null;
        var note = cfg.NoteFor(address);
        return note.Length > 0 ? $"{note}  (not connected)  [{address}]" : $"(not connected) [{address}]";
    }

    void RefreshPads()
    {
        pads = Pads.List();
        var current = Pads.ReadFfxiPad();
        padList.Rows.Clear();
        foreach (var p in pads)
        {
            var g = Pads.Format(p.Guid);
            int i = padList.Rows.Add(p.Name + (g == current ? "   <- FFXI now" : ""), cfg.NoteFor(p.Address), p.Address, g);
            padList.Rows[i].Tag = p;
        }
        // Pads with a note that are off right now, so every note stays visible and editable.
        foreach (var (addr, note) in cfg.PadNotes.Where(kv => pads.All(p => p.Address != kv.Key)).OrderBy(kv => kv.Value))
        {
            int i = padList.Rows.Add("(not connected)", note, addr, "");
            padList.Rows[i].Tag = addr;
            padList.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        UpdatePadChoices();
    }

    /// Rebuild the Controller dropdown (labels carry the notes) and re-point each row at its pad's new label.
    void UpdatePadChoices()
    {
        var col = (DataGridViewComboBoxColumn)grid.Columns[2];
        var rows = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
        var addrs = rows.Select(r => AddressFromLabel(r.Cells[2].Value as string)).ToList();
        var keep = addrs.Select(PadLabel).Where(l => l != null).Cast<object>();
        col.Items.Clear();
        col.Items.AddRange(pads.Select(p => (object)cfg.PadLabel(p)).Concat(keep).Distinct().ToArray());
        for (int i = 0; i < rows.Count; i++) rows[i].Cells[2].Value = PadLabel(addrs[i]);
    }

    void NoteEdited(int rowIndex)
    {
        if (rowIndex < 0) return;
        var r = padList.Rows[rowIndex];
        var addr = r.Tag is Pad p ? p.Address : r.Tag as string;
        if (string.IsNullOrEmpty(addr)) return;
        cfg.SetNote(addr, r.Cells[1].Value as string ?? "");
        BeginInvoke(() =>  // after the edit has fully ended; the grid can't be changed from inside CellEndEdit
        {
            if (rowIndex < padList.Rows.Count) padList.Rows[rowIndex].Cells[1].Value = cfg.NoteFor(addr);
            UpdatePadChoices();
            SaveRows();
        });
    }

    string Describe(Pad p, string? ffxi)
    {
        var note = cfg.NoteFor(p.Address);
        var g = Pads.Format(p.Guid);
        return $"{(note.Length > 0 ? note + " " : "")}({p.Name}) addr={p.Address} guid={g} transport={Pads.Transport(p)}"
             + (g == ffxi ? " <- FFXI padguid000" : "") + $" path={p.Path}";
    }

    /// Log what changed since the last poll, so a "drifting" controller leaves evidence: did its GUID change, did it
    /// vanish and return, did it come back on USB (no Bluetooth address), and does FFXI's saved pad still exist?
    void WatchPads()
    {
        List<Pad> now;
        try { now = Pads.List(); }
        catch (Exception e) { Log("PAD watch failed: " + e.Message); return; }
        var ffxi = Pads.ReadFfxiPad();
        var byAddr = now.GroupBy(p => p.Address).ToDictionary(g => g.Key, g => g.First());
        if (seen == null)
        {
            foreach (var p in byAddr.Values) Log($"PAD present: {Describe(p, ffxi)}");
            Log($"PAD FFXI padguid000={ffxi ?? "(none)"} " + (now.Any(p => Pads.Format(p.Guid) == ffxi) ? "(a connected pad)" : "(matches no connected pad)"));
        }
        else
        {
            foreach (var (addr, p) in byAddr)
            {
                if (!seen.TryGetValue(addr, out var old)) Log($"PAD connected: {Describe(p, ffxi)}");
                else if (old.Guid != p.Guid) Log($"PAD id changed: {Describe(p, ffxi)} was guid={Pads.Format(old.Guid)}");
            }
            foreach (var (addr, old) in seen)
                if (!byAddr.ContainsKey(addr)) Log($"PAD disconnected: {Describe(old, ffxi)}");
            if (ffxi != lastFfxi)
                Log($"PAD FFXI padguid000 now {ffxi ?? "(none)"} " + (now.Any(p => Pads.Format(p.Guid) == ffxi) ? "(a connected pad)" : "(matches no connected pad)"));
        }
        // A pad that arrived, left or changed id is picked up by itself (the lists and the step strip used to wait for Refresh controllers).
        // Not while a cell is being edited: refreshing would throw that edit away, so it waits for a later 2 s tick.
        if (seen != null && (seen.Count != byAddr.Count || byAddr.Any(kv => !seen.TryGetValue(kv.Key, out var prev) || prev.Guid != kv.Value.Guid))) padsChanged = true;
        seen = byAddr;
        lastFfxi = ffxi;
        if (padsChanged && !padList.IsCurrentCellInEditMode && !grid.IsCurrentCellInEditMode) { padsChanged = false; RefreshPads(); }
    }

    void Highlight()
    {
        // Nobody can see the rows while the window is in the tray or minimized: no polling then, and the open devices are released.
        if (!Visible || WindowState == FormWindowState.Minimized) { Pads.ClosePressDevices(); return; }
        var hit = Pads.Pressed(pads);
        foreach (DataGridViewRow r in padList.Rows)
            r.DefaultCellStyle.BackColor = r.Tag is Pad p && hit.Contains(p.Guid) ? Color.Gold : SystemColors.Window;
    }

    void SaveRows()
    {
        if (savingRows) return;
        savingRows = true;
        try
        {
            grid.EndEdit();
            cfg.Rows = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => new Row
            {
                Label = r.Cells[0].Value as string ?? "",
                Profile = r.Cells[1].Value as string ?? "",
                PadAddress = AddressFromLabel(r.Cells[2].Value as string),
            }).Where(r => r.Profile.Length > 0 || r.Label.Length > 0).ToList();
            cfg.Save();
            SyncAssignments();
        }
        finally { savingRows = false; }
    }

    static string AddressFromLabel(string? label)
    {
        if (label == null) return "";
        int a = label.LastIndexOf('['), b = label.LastIndexOf(']');
        return a >= 0 && b > a ? label[(a + 1)..b] : "";
    }

    /// Add a blank character row at the bottom and start typing its name.
    void AddCharacter()
    {
        if (SessionActive) { Explain("Finish this session before changing setup. Live controller swaps are still available."); return; }
        if (grid.Rows.Count >= 2) { Explain("This release supports two characters. We will expand support after testing larger sessions."); return; }
        int i = grid.Rows.Add("", null!, null!);
        grid.CurrentCell = grid.Rows[i].Cells[0];
        grid.BeginEdit(true);  // saved when the edit ends, like any other change
    }

    /// Write the character list (name, profile, controller) to a file the user picks: a backup, or a setup to hand to someone.
    void ExportCharacters()
    {
        SaveRows();
        using var dlg = new SaveFileDialog { Title = "Export characters", Filter = "Offhand characters (*.json)|*.json", FileName = "offhand-characters.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(cfg.Rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log($"Exported {cfg.Rows.Count} characters to {dlg.FileName}");
        }
        catch (Exception e) { MessageBox.Show(this, "Could not export: " + e.Message, "Export characters"); }
    }

    /// Replace the character list with one read from a file made by Export.
    void ImportCharacters()
    {
        if (SessionActive) { Explain("Finish this session before changing setup. Live controller swaps are still available."); return; }
        using var dlg = new OpenFileDialog { Title = "Import characters", Filter = "Offhand characters (*.json)|*.json|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        List<Row>? rows;
        try { rows = System.Text.Json.JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(dlg.FileName)); }
        catch (Exception e) { MessageBox.Show(this, "That file is not a Offhand character list: " + e.Message, "Import characters"); return; }
        if (rows != null && (rows.Count > 2 || rows.Any(r => r == null))) { Explain("Import a list containing one or two characters."); return; }
        if (rows == null || rows.Count == 0) { MessageBox.Show(this, "That file has no characters in it.", "Import characters"); return; }
        if (grid.Rows.Count > 0 && MessageBox.Show(this, $"Replace your {grid.Rows.Count} current characters with the {rows.Count} in this file?", "Import characters", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        grid.EndEdit();
        grid.Rows.Clear();
        foreach (var r in rows) grid.Rows.Add(r.Label ?? "", (((DataGridViewComboBoxColumn)grid.Columns[1]).Items.Contains(r.Profile) ? r.Profile : null)!, PadLabel(r.PadAddress ?? "")!);
        foreach (DataGridViewRow r in grid.Rows) ShowProfileInfo(r);
        SaveRows();
        Log($"Imported {rows.Count} characters from {dlg.FileName}");
    }

    void RemoveCharacter(int index)
    {
        if (SessionActive) { Explain("Finish this session before changing setup. Live controller swaps are still available."); return; }
        if (index < 0 || index >= grid.Rows.Count) return;
        var name = grid.Rows[index].Cells[0].Value as string;
        grid.EndEdit();
        grid.Rows.RemoveAt(index);
        if (grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[Math.Min(index, grid.Rows.Count - 1)].Cells[0];
        SaveRows();
        Log($"Removed character {(string.IsNullOrEmpty(name) ? "(unnamed)" : name)}; {grid.Rows.Count} left");
    }

    void MoveRow(int dir)
    {
        if (SessionActive) { Explain("Finish this session before changing setup. Live controller swaps are still available."); return; }
        if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) return;
        int i = grid.CurrentRow.Index, j = i + dir;
        if (j < 0 || j >= grid.Rows.Count) return;
        var vals = grid.Rows[i].Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray();  // includes the info column
        grid.Rows.RemoveAt(i);
        grid.Rows.Insert(j, vals!);
        grid.CurrentCell = grid.Rows[j].Cells[0];
        SaveRows();
    }

    void SetSelected()
    {
        if (SessionActive) { Explain("Finish this session before changing setup. Live controller swaps are still available."); return; }
        SaveRows();
        if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) { Log("Select a row first."); return; }
        var addr = AddressFromLabel(grid.CurrentRow.Cells[2].Value as string);
        var pad = Pads.List().FirstOrDefault(p => p.Address == addr);
        if (pad == null) { Log("That controller is not connected."); return; }
        Pads.WriteFfxiPad(Pads.Format(pad.Guid));
        Log($"FFXI controller set to {cfg.PadLabel(pad)} {Pads.Format(pad.Guid)} (the next client you start uses it)");
        RefreshPads();
    }

    async Task LaunchAll()
    {
        if (SessionActive) { Explain("A session is already open. Close its game and PlayOnline windows before launching another session."); return; }
        SaveRows();
        if (cfg.Rows.Count == 0) { Log("Add at least one row."); return; }
        if (cfg.Rows.Any(r => cfg.ProfileFor(r).Length == 0 || r.PadAddress.Length == 0)) { Log("Every row needs a Windower profile and a controller."); return; }
        var setupError = SessionChecks.Validate(cfg.Rows.Select(r => r.PadAddress).ToArray());
        if (setupError != null) { Explain(setupError); return; }
        if (!File.Exists(cfg.WindowerExe)) { Explain("Open Setup and choose Windower.exe before launching."); return; }
        if (fwdBox.Checked && fwd == null) { Explain("Virtual pads could not start. Open Setup to check the drivers."); return; }
        if (fwd != null)
        {
            HideRealPads(false);
            if (!cfg.HidePads || !HidHide.Installed || !hideReady || !HidHide.CloakOn || cfg.Rows.Any(r => !fwd.RawKnows(r.PadAddress))) { Explain("Your controllers are not ready to be hidden from the game. Turn both pads on, press a button on each, and wait for Ready. Check Setup if needed."); return; }
            if (Enumerable.Range(0, 2).Any(i => !SessionChecks.ValidSlot(fwd.XInputSlot(i), i) || fwd.VirtualGuid(i) == Guid.Empty) || fwd.VirtualGuid(0) == fwd.VirtualGuid(1)) { Explain("The virtual pads need slots 0 and 1. With all clients closed, turn your pads off and restart Offhand."); return; }
        }
        cts = new CancellationTokenSource();
        running = new Launcher(cfg, Log, fwd == null ? null : i => i < fwd.Count ? fwd.VirtualGuid(i) : null);
        launchBtn.Enabled = false; cancelBtn.Enabled = true;
        try { await running.RunAsync(cts.Token); foreach (var (k, v) in running.ClientPids) clientPids[k] = v; }
        finally { cancelBtn.Enabled = false; running = null; cts.Dispose(); cts = null; RefreshPads(); UpdateGuide(); }
    }

    void Log(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(text)); return; }
        var line = $"[{DateTime.Now:MM-dd HH:mm:ss}] {text}";
        // The box is a window, the file is the record. TextLength is a cheap property; the old check read Lines, which splits the whole
        // text into a new string array on every log call.
        if (logBox.TextLength > 150_000) logBox.Text = logBox.Text[^100_000..];
        logBox.AppendText(line + Environment.NewLine);
        Config.AppendLog(line);
    }
}
