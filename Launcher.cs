using System.Diagnostics;

namespace PadLauncher;

/// Launch All: for each row, set FFXI's controller, start that Windower profile, wait for the new client
/// to load the game (FFXiMain.dll) so it has read the setting, then the next. Restores the original setting.
public class Launcher
{
    readonly Config cfg;
    readonly Action<string> log;
    readonly List<Row> rows;
    HashSet<int> baseline = new();
    DateTime launchStarted;
    public bool CanStop => DateTime.Now - launchStarted > TimeSpan.FromSeconds(10) && !Process.GetProcessesByName("pol").Any(p => !baseline.Contains(p.Id) && !GameLoaded(p));
    public int[] PendingPids { get; private set; } = Array.Empty<int>();
    public string Status { get; private set; } = "Preparing your characters...";
    /// Client process id per row index, for marking windows (filled as clients start).
    public System.Collections.Concurrent.ConcurrentDictionary<int, int[]> ClientPids { get; } = new();
    readonly Func<int, Guid?>? virtualGuid;  // set while forwarding: row i points FFXI at virtual pad i instead of a physical pad
    public Launcher(Config cfg, Action<string> log, Func<int, Guid?>? virtualGuid = null) { this.cfg = cfg; this.log = log; this.virtualGuid = virtualGuid; rows = cfg.Rows.Select(r => new Row { Label = r.Label, Profile = cfg.ProfileFor(r), PadAddress = r.PadAddress }).ToList(); }


    public async Task RunAsync(CancellationToken ct)
    {
        string? original = null;
        try
        {
            original = Pads.ReadFfxiPad();
            log($"FFXI controller before launch: {original ?? "(none)"}");
            for (int i = 0; i < rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[i];
                if (virtualGuid?.Invoke(i) is Guid vg)
                {
                    // Forwarding: the client gets virtual pad i; whichever physical pad feeds it can change later.
                    Pads.WriteFfxiPad(Pads.Format(vg));
                    log($"{i + 1}. {row.Label}: controller set to virtual pad {i + 1} {Pads.Format(vg)} (fed by {(row.PadAddress.Length > 0 ? row.PadAddress : "no pad yet")})");
                }
                else
                {
                    var pad = Pads.List().FirstOrDefault(p => p.Address == row.PadAddress);
                    if (pad == null) { log($"STOP: {row.Label}: controller {row.PadAddress} is not connected. Turn it on and try again."); return; }
                    Pads.WriteFfxiPad(Pads.Format(pad.Guid));
                    log($"{i + 1}. {row.Label}: controller set to {cfg.PadLabel(pad)} {Pads.Format(pad.Guid)}");
                }

                var before = Process.GetProcessesByName("pol").Select(p => p.Id).ToHashSet();
                baseline = before; launchStarted = DateTime.Now;
                var args = cfg.WindowerArgs.Replace("{profile}", Config.ProfileArg(row.Profile));
                Process.Start(new ProcessStartInfo(cfg.WindowerExe, args)
                    { WorkingDirectory = Path.GetDirectoryName(cfg.WindowerExe), UseShellExecute = true });
                log($"   started Windower {args}{(cfg.QualityRolesOn ? (cfg.IsMain(cfg.Rows[i]) ? " (main, full quality)" : " (secondary, lite)") : "")}; waiting for the game to load (log in as usual)");

                Status = $"Log in as {row.Label}. Take your time; its controller assignment is being held.";
                var progress = new LaunchProgress();
                bool reminded = false; var reminder = DateTime.Now.AddMinutes(3);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var fresh = Process.GetProcessesByName("pol").Where(p => !before.Contains(p.Id)).ToList();
                    PendingPids = fresh.Select(p => p.Id).ToArray();
                    if (fresh.Count > 0) ClientPids[i] = fresh.Select(p => p.Id).ToArray();  // PlayOnline can run more than one pol.exe per client: mark every one that has a window
                    var phase = progress.Observe(fresh.Count, fresh.Any(GameLoaded));
                    if (phase == LaunchPhase.Ready) { PendingPids = Array.Empty<int>(); log($"   {row.Label}: game loaded (pol {string.Join(",", fresh.Select(p => p.Id))})"); break; }
                    if (phase == LaunchPhase.Closed) { Status = $"{row.Label}: PlayOnline closed. Launch stopped; you can try again."; log(Status); return; }
                    if (!reminded && DateTime.Now > reminder) { reminded = true; log($"Still waiting for {row.Label}. Controller assignment held; no login time limit."); }
                    await Task.Delay(1000, ct);
                }
                Status = $"{row.Label} is ready. Preparing the next character...";
                await Task.Delay(cfg.PauseAfterClientSeconds * 1000, ct);
            }
            Status = "All characters are ready. Have fun!"; log(Status);
        }
        catch (OperationCanceledException) { Status = "Launch stopped. Ready when you are."; log(Status); }
        catch (Exception e) { Status = "Launch could not finish: " + e.Message; log(Status); }
        finally
        {
            if (original != null) { try { Pads.WriteFfxiPad(original); log($"FFXI controller restored to {original}"); } catch (Exception e) { log("Could not restore: " + e.Message); } }
        }
    }

    // pol.exe starts as PlayOnline; the game itself is FFXiMain.dll, loaded after login.
    static bool GameLoaded(Process p) => IsGameLoaded(p) == true;

    /// true/false when the module list could be read, null when it could not (access denied, process ending): callers that act on "no game"
    /// must treat null as "unknown" and leave the process alone.
    public static bool? IsGameLoaded(Process p)
    {
        try { p.Refresh(); return p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("FFXiMain.dll", StringComparison.OrdinalIgnoreCase)); }
        catch { return null; }
    }
}
