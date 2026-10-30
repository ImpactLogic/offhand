using System.Diagnostics;
using System.Text.Json;

namespace PadLauncher;

/// Thin wrapper over HidHideCLI (Nefarius HidHide, a free driver by the author of ViGEmBus). While the "cloak" is on,
/// the devices on the hide list are invisible to every program except the ones on the application list. We put
/// Offhand (and Steam, so Steam keeps seeing the real pads) on that list, hide the real pads, and turn the cloak on
/// only while forwarding runs: the game then only sees the virtual pads. Everything is undone on stop/exit.
public static class HidHide
{
    static readonly string Cli = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
    public static bool Installed => File.Exists(Cli);

    static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var step = "HidHide " + string.Join(' ', args.Select(a => a.Length > 40 ? a[..40] + "..." : a));
        Watchdog.Step(step);
        var started = Stopwatch.StartNew();
        try { return RunProcess(psi); }
        finally { if (started.ElapsedMilliseconds > 1500) Config.AppendLog($"[{DateTime.Now:MM-dd HH:mm:ss}] SLOW {step} took {started.ElapsedMilliseconds} ms"); }
    }

    static string RunProcess(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(5000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("HidHide did not respond. Your cleanup records have been kept.");
        }
        if (!Task.WaitAll(new Task[] { output, error }, 1000)) throw new TimeoutException("HidHide output did not finish.");
        if (p.ExitCode != 0) throw new InvalidOperationException($"HidHide failed ({p.ExitCode}): {error.Result.Trim()}");
        return output.Result;
    }

    public static bool CloakOn => Installed && Run("--cloak-state").Contains("--cloak-on", StringComparison.OrdinalIgnoreCase);
    public static void SetCloak(bool on) { if (Installed) Run(on ? "--cloak-on" : "--cloak-off"); }

    public static void AllowApp(string exePath)
    {
        if (!Installed || !File.Exists(exePath)) return;
        if (!Run("--app-list").Contains(exePath, StringComparison.OrdinalIgnoreCase)) Run("--app-reg", exePath);
    }

    /// Device instance paths (HID children and the Bluetooth parent) of the pad with this Bluetooth address.
    /// Matched on the 12 hex digits in the path, exactly like Pads.AddressOf; virtual pads never match.
    public static List<string> DevicePathsFor(string address)
    {
        var hex = address.Replace(":", "").ToLowerInvariant();
        var result = new List<string>();
        if (!Installed || hex.Length != 12) return result;
        try
        {
            using var doc = JsonDocument.Parse(Run("--dev-all"));
            foreach (var group in doc.RootElement.EnumerateArray())
                foreach (var dev in group.GetProperty("devices").EnumerateArray())
                {
                    var inst = dev.GetProperty("deviceInstancePath").GetString() ?? "";
                    var link = dev.GetProperty("symbolicLink").GetString() ?? "";
                    bool pad = inst.Contains(hex, StringComparison.OrdinalIgnoreCase) || link.Contains(hex, StringComparison.OrdinalIgnoreCase);
                    if (pad && !inst.Contains("PID_028E", StringComparison.OrdinalIgnoreCase)) result.Add(inst);
                }
        }
        catch (JsonException e) { throw new InvalidOperationException("HidHide returned an unreadable device list.", e); }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Hide(string instancePath) => Run("--dev-hide", instancePath);
    public static void Unhide(string instancePath) => Run("--dev-unhide", instancePath);
}
