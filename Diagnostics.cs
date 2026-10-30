using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace PadLauncher;

/// "Save diagnostics": one zip a tester can send instead of describing what happened. Contains the version, Windows and driver
/// status, the connected pads, the last part of launcher.log and the settings file. It holds pad Bluetooth addresses and your notes,
/// character labels and the Windower path; no passwords or account names are stored by this program.
public static class Diagnostics
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "?";

    public static string AboutText =>
        $"Offhand {Version}\n\nOne controller per game window, with live swapping.\n" +
        "Reads your pads and presents virtual ones; it never reads or writes game memory, sends keystrokes or automates play.\n\n" +
        "Square Enix's rules do not allow third-party programs (Windower is one). Use it at your own risk.\n\n" +
        "Built on ViGEmBus and HidHide by Nefarius, the ViGEm client and Vortice libraries.\n" +
        "Settings and log: " + Path.GetDirectoryName(Config.LogPath);

    public static string Save(Config cfg, string? folder = null)
    {
        folder ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var work = Path.Combine(Path.GetTempPath(), "offhand-diag-" + stamp);
        Directory.CreateDirectory(work);
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Offhand {Version}");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}, 64-bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Windower.exe found: {File.Exists(cfg.WindowerExe)} ({cfg.WindowerExe})");
            bool vigem = false; try { vigem = VirtualPads.DriverInstalled(); } catch { }
            sb.AppendLine($"ViGEmBus usable: {vigem}");
            sb.AppendLine($"HidHide installed: {HidHide.Installed}");
            try { sb.AppendLine($"HidHide cloak on: {HidHide.CloakOn}"); } catch { }
            sb.AppendLine($"Forwarding setting: {cfg.UseVirtual}, hide pads setting: {cfg.HidePads}, rows: {cfg.Rows.Count}");
            sb.AppendLine($"Running now: Offhand x{Process.GetProcessesByName("Offhand").Length}, pol.exe x{Process.GetProcessesByName("pol").Length}, Windower.exe x{Process.GetProcessesByName("Windower").Length}");
            sb.AppendLine("Pads Windows lists:");
            try { foreach (var p in Pads.List()) sb.AppendLine($"  {p.Name}  [{p.Address}]  {Pads.Format(p.Guid)}  {Pads.Transport(p)}"); }
            catch (Exception e) { sb.AppendLine("  (could not list: " + e.Message + ")"); }
            File.WriteAllText(Path.Combine(work, "info.txt"), sb.ToString());

            if (File.Exists(Config.LogPath))
            {
                var lines = File.ReadAllLines(Config.LogPath);
                File.WriteAllLines(Path.Combine(work, "launcher.log"), lines.Length > 3000 ? lines[^3000..] : lines);
            }
            if (File.Exists(Config.FilePath)) File.Copy(Config.FilePath, Path.Combine(work, "config.json"));
            var zip = Path.Combine(folder, $"Offhand-diagnostics-{stamp}.zip");
            ZipFile.CreateFromDirectory(work, zip);
            return zip;
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}
