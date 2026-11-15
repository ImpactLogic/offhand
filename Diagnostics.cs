using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace PadLauncher;

/// Read-only diagnostic report assembled from an explicit list of fields.
public static class Diagnostics
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "?";

    public static string AboutText =>
        $"Offhand {Version}\n\nOne controller per game window, with live swapping.\n" +
        "Routes controllers and offers optional PlayOnline sign-in. Sign-in sends input to the PlayOnline window.\n\n" +
        "Square Enix's rules do not allow third-party programs (Windower is one). Use it at your own risk.\n\n" +
        "Built on ViGEmBus and HidHide by Nefarius, the ViGEm client and Vortice libraries.\n" +
        "Settings and log: " + Path.GetDirectoryName(Config.LogPath);

    public static IReadOnlyDictionary<string, string> Preview(Config cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Offhand {Version}");
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}, 64-bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"Windower.exe found: {File.Exists(cfg.WindowerExe)}");
        bool vigem = false; try { vigem = VirtualPads.DriverInstalled(); } catch { }
        sb.AppendLine($"ViGEmBus usable: {vigem}");
        sb.AppendLine($"HidHide installed: {HidHide.Installed}");
        try { sb.AppendLine($"HidHide cloak on: {HidHide.CloakOn}"); } catch { }
        sb.AppendLine($"Forwarding setting: {cfg.UseVirtual}, hide pads setting: {cfg.HidePads}, rows: {cfg.Rows.Count}");
        sb.AppendLine($"Running now: Offhand x{Process.GetProcessesByName("Offhand").Length}, pol.exe x{Process.GetProcessesByName("pol").Length}, Windower.exe x{Process.GetProcessesByName("Windower").Length}");
        var names = cfg.Rows.Select(r => r.Label).Concat(cfg.PadNotes.Values)
            .Concat(new[] { cfg.MainLabel, cfg.WindowerExe, cfg.WindowerArgs });
        var report = new Dictionary<string, string>
        {
            ["info.txt"] = sb.ToString(),
            ["settings.txt"] = $"Characters: {cfg.Rows.Count}\nVirtual pads: {cfg.UseVirtual}\nHide real pads: {cfg.HidePads}\nVerbose input log: {cfg.VerboseInput}\n"
        };
        if (File.Exists(Config.LogPath))
        {
            using var file = new FileStream(Config.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            var lines = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > 3000) lines.Dequeue();
            }
            report["launcher.log"] = AskClaude.Redact(string.Join(Environment.NewLine, lines), names);
        }
        return report;
    }

    public static void SaveReport(string path, IReadOnlyDictionary<string, string> report)
    {
        // Build the archive beside the destination so a failed write leaves any prior ZIP intact.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var (name, text) in report)
                {
                    using var entry = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                    entry.Write(text);
                }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
