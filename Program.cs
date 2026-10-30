using System.Runtime.InteropServices;

namespace PadLauncher;

static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string? className, string title);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [STAThread]
    static void Main(string[] args)
    {
        // Developer aid: "--screenshot out.png" draws the main window with sample data into a PNG and exits (set PADLAUNCHER_DATA to a
        // scratch folder first so real settings are never touched). It forwards nothing and hides nothing.
        if (args.Length == 2 && args[0] == "--screenshot") { Screenshot(args[1]); return; }
        if (args.Length == 2 && args[0] == "--screenshot-setup")
        {
            ApplicationConfiguration.Initialize();
            using var s = new SetupForm(new Config());
            s.StartPosition = FormStartPosition.Manual; s.Location = new Point(40, 40); s.Show();
            for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(100); }
            using var b = new Bitmap(s.Width, s.Height); s.DrawToBitmap(b, new Rectangle(0, 0, s.Width, s.Height)); b.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            Environment.Exit(0);
        }
        // Show and log any unhandled error instead of closing, so a crash leaves its cause in launcher.log.
        Watchdog.WatchOutOfMemory();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        // One launcher only: a second one would create a second set of virtual pads and push FFXI's pad GUIDs out of order.
        using var single = new Mutex(true, @"Local\FfxiPadLauncher", out bool first);
        if (!first)
        {
            var existing = FindWindowW(null, "Offhand " + Diagnostics.Version);
            if (existing != IntPtr.Zero) { ShowWindowAsync(existing, 9); SetForegroundWindow(existing); }
            else MessageBox.Show("Offhand is already running. Open it from its tray icon (it may be a different version).", "Offhand");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    static void Screenshot(string path)
    {
        ApplicationConfiguration.Initialize();
        var cfg = new Config();
        cfg.Rows.Add(new Row { Label = "Player 1", Profile = "(Default)", PadAddress = "00:11:22:33:44:01" });
        cfg.Rows.Add(new Row { Label = "Player 2", Profile = "No Sound - Controller", PadAddress = "00:11:22:33:44:02" });
        cfg.PadNotes["00:11:22:33:44:01"] = "Blue Camo"; cfg.PadNotes["00:11:22:33:44:02"] = "Green";
        cfg.Save();
        using var f = new MainForm();
        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(40, 40);
        f.Show();
        if (Environment.GetEnvironmentVariable("PADLAUNCHER_EXPAND") == "1") { f.ExpandForScreenshot(); f.Height = 900; }
        for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(100); }
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Environment.Exit(0);
    }

    static void Report(Exception? e)
    {
        var text = e?.ToString() ?? "unknown error";
        Config.AppendLog($"[{DateTime.Now:MM-dd HH:mm:ss}] CRASH {text}");
        MessageBox.Show(text, "Offhand error (also in launcher.log)", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
