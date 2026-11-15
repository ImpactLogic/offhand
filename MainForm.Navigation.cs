namespace PadLauncher;

public partial class MainForm
{
    readonly Dictionary<string, Control> pages = new();
    readonly Dictionary<string, Button> navigation = new();
    readonly Panel pageHost = new() { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12) };

    void BuildSidebarLayout(Control strip, Control heading, Control characters, Control primary)
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sidebar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Color.FromArgb(238, 242, 247), Padding = new Padding(10, 18, 10, 10) };
        sidebar.Controls.Add(new Label { Text = "Offhand", AutoSize = false, Width = 132, Height = 34, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Margin = new Padding(4, 0, 0, 24) });
        foreach (var name in new[] { "Characters", "Controllers", "Activity", "Help" })
        {
            var button = new Button { Text = name, Width = 132, Height = 44, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 6), AccessibleName = name };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => NavigateTo(name);
            navigation.Add(name, button); sidebar.Controls.Add(button);
        }
        var setup = new Button { Text = "Setup...", Width = 132, Height = 38, Margin = new Padding(0, 24, 0, 0) };
        setup.Click += (_, _) => ShowSetup(); sidebar.Controls.Add(setup);
        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 10, 8) };
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.Controls.Add(pageHost, 0, 0); workspace.Controls.Add(primary, 0, 1); workspace.Controls.Add(statusLine, 0, 2);
        workspace.SizeChanged += (_, _) => statusLine.MaximumSize = new Size(Math.Max(250, workspace.ClientSize.Width - 20), 0);
        shell.Controls.Add(sidebar, 0, 0); shell.Controls.Add(workspace, 1, 0);

        var characterPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        foreach (var size in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize })
            characterPage.RowStyles.Add(new RowStyle(size, size == SizeType.Percent ? 100 : 0));
        var content = new Control[] { strip, helpLine, heading, characters, morePanel };
        for (int i = 0; i < content.Length; i++) characterPage.Controls.Add(content[i], 0, i);
        characterPage.SizeChanged += (_, _) => { int w = Math.Max(250, characterPage.ClientSize.Width - 20); helpLine.MaximumSize = new Size(w, 0); };
        pages.Add("Characters", characterPage);

        var controllerPage = new Panel { Dock = DockStyle.Fill };
        var controllerActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
        var refresh = new Button { Text = "Refresh controllers", AutoSize = true };
        refresh.Click += (_, _) => { Pads.InvalidatePathCache(); RefreshPads(); };
        controllerActions.Controls.AddRange(new Control[] { refresh, advBox });
        controllerPage.Controls.Add(padsPanel); controllerPage.Controls.Add(controllerActions);
        pages.Add("Controllers", controllerPage);
        detailsPanel.Dock = DockStyle.Fill; detailsPanel.Visible = true;
        pages.Add("Activity", detailsPanel);

        var help = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        help.Controls.Add(new Label { Text = "Ready for your next session", AutoSize = true, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Margin = new Padding(0, 0, 0, 16) });
        help.Controls.Add(new Label { Text = "1. Set up Windower and add your characters.\n\n2. Open Controllers to identify and name each pad, then assign it on Characters.\n\n3. Choose Launch on Characters and log in as each window opens.\n\nFor virtual pads: turn the real pads off before starting Offhand, then turn them on after the virtual pads are ready.\n\nThe window's X keeps Offhand running in the tray. Use File > Exit Offhand when finished.", AutoSize = true, MaximumSize = new Size(610, 0) });
        var wizard = new Button { Text = "Open setup wizard", AutoSize = true, Margin = new Padding(0, 20, 0, 8) };
        wizard.Click += (_, _) => ShowSetup(); help.Controls.Add(wizard);
        var diagnostics = new Button { Text = "Save diagnostics...", AutoSize = true };
        diagnostics.Click += (_, _) => SaveDiagnostics(); help.Controls.Add(diagnostics);
        pages.Add("Help", help);
        foreach (var page in pages.Values) { page.Visible = false; pageHost.Controls.Add(page); }
        Controls.Add(shell);
        NavigateTo("Characters");
    }

    void NavigateTo(string name)
    {
        if (!pages.ContainsKey(name)) return;
        // Commit completed edits before moving to a different page; use the existing save path.
        if (initialized && !SessionActive) { grid.EndEdit(); padList.EndEdit(); SaveRows(); }
        foreach (var (key, page) in pages) page.Visible = key == name;
        pages[name].BringToFront();
        foreach (var (key, button) in navigation)
        {
            button.BackColor = key == name ? Color.FromArgb(210, 225, 242) : Color.FromArgb(238, 242, 247);
            button.AccessibleDescription = key == name ? "Current section" : "Open " + key;
        }
    }
}
