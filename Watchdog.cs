namespace PadLauncher;

/// Freeze evidence. The 2 s tick on the window thread calls Beat(); a background timer notices when that stops and writes
/// to the log file directly (Log() itself waits for the window thread, so it is silent exactly when the window is frozen).
/// It also writes an "alive" line every 10 minutes, so a quiet log means "nothing happened", not "stuck".
public static class Watchdog
{
    static long lastBeat = Environment.TickCount64;
    static volatile string step = "starting";
    static bool reported;
    static int checks;
    static System.Threading.Timer? timer;
    static long nextMarkMb = 300;
    static int observing;
    static int controllerEvent;

    public static void ControllerEvent(string detail)
    {
        Interlocked.Exchange(ref controllerEvent, 1);
        Write("CONTROLLER TIMELINE " + detail);
    }
    static readonly OperationMonitor operations = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> lastNotes = new();
    public static OperationTrace Track(string name) => operations.Track(name);
    public static void Note(string key, string text)
    {
        long now = Environment.TickCount64;
        if (lastNotes.TryGetValue(key, out var previous) && now - previous < 30_000) return;
        lastNotes[key] = now;
        Write("DIAG " + text);
    }

    /// Offhand's own size, for the ALIVE line: a 22-hour run died of an unhandled error on 10/15 with no record of its memory (IDEAS #217).
    /// This is a 32-bit process, so private bytes near 2 GB (or a handle count that only ever grows) is the thing to watch.
    public static string Self()
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            return $"Offhand private {p.PrivateMemorySize64 / 1048576} MB, working set {p.WorkingSet64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB, handles {p.HandleCount}, threads {p.Threads.Count}";
        }
        catch { return "Offhand memory ?"; }
    }

    /// Last words: an out-of-memory error is logged the moment it is first thrown, with a short line that needs almost no memory itself.
    public static void WatchOutOfMemory()
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (e.Exception is OutOfMemoryException) { try { Write("OUTOFMEMORY first thrown; " + Self()); } catch { } }
        };
    }

    /// Name the slow thing the window thread is about to do (shown in the freeze line).
    public static void Step(string name) => step = name;

    public static void Beat()
    {
        var lag = (Environment.TickCount64 - Interlocked.Exchange(ref lastBeat, Environment.TickCount64)) / 1000.0;
        step = "idle";
        if (reported) { reported = false; Write($"WATCHDOG window thread recovered after {lag:F0} s"); }
    }

    public static void Start(Func<string> status, Func<string> controllerEvidence)
    {
        var sampler = new DiagnosticStatusSampler(status);
        sampler.TryRefresh();
        var evidence = new DiagnosticStatusSampler(controllerEvidence);
        timer = new System.Threading.Timer(_ =>
        {
            if (Interlocked.CompareExchange(ref observing, 1, 0) != 0) return;
            try
            {
                operations.Observe(Environment.TickCount64, line =>
                {
                    Write(line);
                    if (line.StartsWith("DIAG pending:", StringComparison.Ordinal) &&
                        (line.Contains("physical-input", StringComparison.Ordinal) ||
                         line.Contains("controller-refresh", StringComparison.Ordinal)))
                        evidence.TryRefresh(collect => Write("CONTROLLER EVIDENCE " + collect));
                });
                var lag = (Environment.TickCount64 - Interlocked.Read(ref lastBeat)) / 1000.0;
                if (lag > 10 && !reported) { reported = true; Write($"WATCHDOG window thread stuck for {lag:F0} s in: {step}"); }
                else if (lag > 10 && lag % 60 < 5) Write($"WATCHDOG window thread still stuck, {lag:F0} s in: {step}");
                if (Interlocked.Exchange(ref controllerEvent, 0) != 0 &&
                    !evidence.TryRefresh(collect => Write("CONTROLLER EVIDENCE after transition: " + collect)))
                    Interlocked.Exchange(ref controllerEvent, 1);
                if (++checks % 12 == 0)  // once a minute: log each 100 MB step of growth, so a leak shows its slope
                {
                    sampler.TryRefresh();
                    evidence.TryRefresh();
                    using var me = System.Diagnostics.Process.GetCurrentProcess();
                    long mb = me.PrivateMemorySize64 / 1048576;
                    if (mb >= nextMarkMb) { Write($"MEMORY crossed {nextMarkMb} MB; {Self()}"); nextMarkMb = (mb / 100 + 1) * 100; }
                }
                if (checks % 120 == 0) Write($"ALIVE window lag {lag:F0} s; {sampler.Snapshot(Environment.TickCount64)}; controllers: {evidence.Snapshot(Environment.TickCount64)}; {Self()}");
            }
            catch { }
            finally { Volatile.Write(ref observing, 0); }
        }, null, 5000, 5000);
    }

    static void Write(string text) => Config.AppendLog($"[{DateTime.Now:MM-dd HH:mm:ss}] {text}");
}
