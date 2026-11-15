using System.Text;
using System.Text.RegularExpressions;

namespace PadLauncher;

/// "Ask Claude" (0.12.0): a redacted copy of the diagnostics plus a prompt to paste into a Claude chat. Nothing is sent anywhere by
/// Offhand: it writes a zip, copies the prompt to the clipboard and opens the folder. Also the optional status file for local Claude sessions.
public static class AskClaude
{
    static readonly Regex Mac = new(@"\b[0-9A-Fa-f]{2}([:\-][0-9A-Fa-f]{2}){5}\b|\b[0-9A-Fa-f]{12}\b", RegexOptions.Compiled);
    static readonly Regex Ip = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);
    static readonly Regex Hid = new(@"(?i)\\\\\?\\hid#[^\s\]]+|VID_[0-9A-F]{4}&PID_[0-9A-F]{4}[^\s\]]*", RegexOptions.Compiled);

    /// Replaces the Windows user name, pad addresses, device paths and IPs. Character labels are replaced by the caller (they need the config).
    public static string Redact(string text, IEnumerable<string>? names = null)
    {
        var s = text;
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName })
            if (!string.IsNullOrEmpty(root)) s = s.Replace(root, root.Contains('\\') ? "%USERPROFILE%" : "<user>", StringComparison.OrdinalIgnoreCase);
        if (names != null)
        {
            int n = 0;
            foreach (var name in names.Where(x => x.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length))
                s = s.Replace(name, "<character " + (++n) + ">", StringComparison.OrdinalIgnoreCase);
        }
        s = Hid.Replace(s, "<device>");
        s = Mac.Replace(s, "<pad address>");
        return Ip.Replace(s, "<ip>");
    }

    static IEnumerable<string> Names(Config cfg) => cfg.Rows.Select(r => r.Label).Concat(new[] { cfg.MainLabel });

    /// What the bundle holds, shown before and after saving.
    public const string Contents =
        "The bundle includes version and driver status, basic settings, up to 3000 recent log lines and PROMPT.txt.\n" +
        "It excludes saved passwords, sign-in recordings and the configuration file. Known identifiers are redacted. Review the log before sharing.";

    public static string Build(Config cfg, string? folder = null)
    {
        folder ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var report = new Dictionary<string, string>(Diagnostics.Preview(cfg)) { ["PROMPT.txt"] = Prompt };
        var zip = Path.Combine(folder, $"Offhand-for-Claude-{stamp}.zip");
        Diagnostics.SaveReport(zip, report);
        return zip;
    }

    public const string Prompt =
        "I use Offhand, a Windows tool that gives each FFXI window its own controller. Something went wrong (describe it here: ______).\n" +
        "The attached zip is its redacted diagnostics: info.txt, launcher.log, settings.txt. Please read the end of launcher.log first, tell me what most likely\n" +
        "happened and what to try, and say what you are unsure about.";

    /// claude-state.json beside the log, for a Claude session on this PC to read. Off unless the user ticks Tools > Write status file for Claude.
    public static string StatusPath => Path.Combine(Path.GetDirectoryName(Config.LogPath) ?? "", "claude-state.json");
    static string lastStatus = "";

    public static void WriteStatus(Config cfg, bool sessionActive)
    {
        try
        {
            string tail = "";
            if (File.Exists(Config.LogPath))
            {
                using var fs = new FileStream(Config.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var q = new Queue<string>();
                for (string? l; (l = sr.ReadLine()) != null;) { q.Enqueue(l); if (q.Count > 200) q.Dequeue(); }
                tail = string.Join("\n", q);
            }
            var state = new
            {
                app = "Offhand", version = Diagnostics.Version, written = DateTime.Now.ToString("s"),
                sessionActive, characters = cfg.Rows.Count, forwarding = cfg.UseVirtual, hidePads = cfg.HidePads,
                windowerFound = File.Exists(cfg.WindowerExe), note = "Read-only snapshot. Character names and pad addresses are not included.",
                logTail = Redact(tail, Names(cfg).Concat(cfg.PadNotes.Values)).Split('\n')
            };
            var json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            if (json.Replace(state.written, "") == lastStatus) return;
            lastStatus = json.Replace(state.written, "");
            var tmp = StatusPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, StatusPath, true);
        }
        catch { }
    }

    public static void RemoveStatus() { try { File.Delete(StatusPath); } catch { } }
}
