using System.Text.Json;
using System.Xml.Linq;

namespace PadLauncher;

public class Row
{
    public string Label { get; set; } = "";       // e.g. character name, for the user
    public string Profile { get; set; } = "";     // Windower profile name
    public string PadAddress { get; set; } = "";  // stable pad identity (see Pads.AddressOf)
}

public class Config
{
    public bool? SetupCompleted { get; set; } // null preserves pre-wizard installs; false resumes canceled first-run setup.
    public string WindowerExe { get; set; } = @"C:\Program Files (x86)\Windower4\Windower.exe";
    // {profile} is replaced. User confirmed 2026-09-21: -p "<profile>" starts that profile directly.
    public string WindowerArgs { get; set; } = "-p \"{profile}\"";
    public int PauseAfterClientSeconds { get; set; } = 5;
    public bool MarkClients { get; set; } = true;  // rename each launched client window to "label | pad" so clients can be told apart
    public bool HidePads { get; set; } = true;  // while forwarding: hide the real pads from the game with HidHide (if installed)
    public List<string> HiddenPaths { get; set; } = new();  // pads we hid with HidHide; undone on stop, or on the next start after a crash
    public bool VerboseInput { get; set; }  // log every idle/active change of the virtual pads (noisy; for diagnosing)
    public bool UseVirtual { get; set; }  // forward physical pads through ViGEm virtual pads (IDEAS #38 virtual pad layer)
    public bool ClaudeStatusFile { get; set; }  // 0.12.0: write claude-state.json beside the log for a local Claude session (off by default)
    public List<Row> Rows { get; set; } = new();
    // Main / secondary quality (0.9.0). The main character launches with FullProfile, every other one with LiteProfile; both are
    // Windower profiles the user made in Windower (the launcher only picks them). Off while any of the three is blank, or while
    // MainLabel names no row: then each row launches with its own profile as before.
    public string MainLabel { get; set; } = "";
    public string FullProfile { get; set; } = "";
    public string LiteProfile { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public bool QualityRolesOn => FullProfile.Length > 0 && LiteProfile.Length > 0 && Rows.Any(r => IsMain(r));
    public bool IsMain(Row r) => MainLabel.Length > 0 && string.Equals(r.Label, MainLabel, StringComparison.OrdinalIgnoreCase);
    /// The Windower profile this row launches with.
    public string ProfileFor(Row r) => !QualityRolesOn ? r.Profile : IsMain(r) ? FullProfile : LiteProfile;
    // Pad address -> the user's own description of the physical pad ("white, tape on back").
    public Dictionary<string, string> PadNotes { get; set; } = new();

    public string NoteFor(string address) => PadNotes.TryGetValue(address, out var n) ? n : "";
    public void SetNote(string address, string note)
    {
        note = note.Trim().Replace("[", "(").Replace("]", ")");  // brackets would confuse AddressFromLabel
        if (note.Length == 0) PadNotes.Remove(address); else PadNotes[address] = note;
    }
    /// Pad as shown in the Controller dropdown and the log: the note first, when there is one.
    /// Always ends in "[address]", which is how a row's choice is read back.
    public string PadLabel(Pad p) => NoteFor(p.Address) is { Length: > 0 } n ? $"{n}  ({p.Name})  [{p.Address}]" : p.Label;

    static string Dir => Environment.GetEnvironmentVariable("PADLAUNCHER_DATA") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ffxi-pad-launcher");
    public static string FilePath => Path.Combine(Dir, "config.json");
    public static string LogPath => Path.Combine(Dir, "launcher.log");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static int logWrites;
    /// Append one line to launcher.log. The file is capped: past 1 MB it becomes launcher.old.log (the previous old one is replaced).
    public static void AppendLog(string line)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (Interlocked.Increment(ref logWrites) % 200 == 1 && File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                File.Move(LogPath, Path.Combine(Dir, "launcher.old.log"), overwrite: true);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }

    static bool primaryInvalid;
    public static string? RecoveryNotice { get; private set; }
    public static Config Load()
    {
        RecoveryNotice = null; primaryInvalid = false;
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty settings");
                if (loaded.WindowerExe == null || loaded.WindowerArgs == null || loaded.Rows == null || loaded.PadNotes == null || loaded.HiddenPaths == null || loaded.MainLabel == null || loaded.FullProfile == null || loaded.LiteProfile == null || loaded.Rows.Any(r => r == null || r.Label == null || r.Profile == null || r.PadAddress == null)) throw new InvalidDataException("Invalid settings");
                if (path != FilePath) RecoveryNotice = "Your settings were recovered from the backup.";
                return loaded;
            }
            catch { if (path == FilePath) { primaryInvalid = true; try { File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff"), false); } catch { } } RecoveryNotice = "Settings could not be read. The original files have been preserved; check Setup before playing."; }
        }
        return new();
    }
    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        if (primaryInvalid && File.Exists(FilePath)) { File.Move(FilePath, FilePath + ".replaced-" + Guid.NewGuid().ToString("N")); File.Move(temp, FilePath); primaryInvalid = false; }
        else if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }

    // Windower's Default profile is <profile name=""> in settings.xml (seen on the user's PC 2026-09-29).
    // Shown and saved under this name; started as -p "".
    public const string DefaultProfile = "(Default)";
    public static string ProfileArg(string profile) => profile == DefaultProfile ? "" : profile;

    public List<string> WindowerProfiles() => ProfileSummaries().Keys.ToList();

    /// Profile name -> one read-only line ("Window, 1920x1080, UI 1.3, sound off") from Windower's settings.xml.
    /// The launcher never writes that file: settings are edited in Windower. An absent tag is Windower's default.
    /// Tags seen on the user's PC 2026-09-29: windowmode, width, height, uiscale, supersampling, sound.
    public Dictionary<string, string> ProfileSummaries()
    {
        var result = new Dictionary<string, string>();
        try
        {
            var xml = Path.Combine(Path.GetDirectoryName(WindowerExe) ?? "", "settings.xml");
            foreach (var p in XDocument.Load(xml).Descendants("profile").Where(p => p.Attribute("name") != null))
            {
                var name = (string)p.Attribute("name")! is { Length: > 0 } n ? n : DefaultProfile;
                string? Tag(string t) => p.Element(t)?.Value.Trim() is { Length: > 0 } v ? v : null;
                var parts = new List<string>
                {
                    Tag("windowmode") ?? "default window mode",
                    Tag("width") is { } w && Tag("height") is { } h ? $"{w}x{h}" : "default size",
                    "UI " + (Tag("uiscale") ?? "default"),
                };
                if (Tag("supersampling") is { } ss) parts.Add("supersampling " + ss);
                parts.Add(Tag("sound") == "false" ? "sound off" : "sound on");
                result.TryAdd(name, string.Join(", ", parts));
            }
        }
        catch { }
        return result;
    }
}

