using System.Collections.Concurrent;

namespace PadLauncher;

public readonly record struct OperationSnapshot(int Sequence, string Phase, string Context, long Started, long Completed, int Thread);

/// One writer per tracker. Readers retry briefly rather than taking the routing/device locks.
public sealed class OperationTrace(string name)
{
    public string Name { get; } = name;
    int sequence, thread;
    string phase = "", context = "";
    long started, completed;
    public void Begin(string operation, string detail = "", long? now = null)
    {
        Interlocked.Increment(ref sequence);
        phase = operation; context = detail; started = now ?? Environment.TickCount64; thread = Environment.CurrentManagedThreadId;
        Interlocked.Increment(ref sequence);
    }
    public void End(long? now = null)
    {
        Interlocked.Increment(ref sequence);
        completed = now ?? Environment.TickCount64; phase = "";
        Interlocked.Increment(ref sequence);
    }
    public OperationSnapshot? Read()
    {
        for (int i = 0; i < 3; i++)
        {
            int before = Volatile.Read(ref sequence);
            if ((before & 1) != 0) continue;
            var snapshot = new OperationSnapshot(before, phase, context, Interlocked.Read(ref started), Interlocked.Read(ref completed), thread);
            if (before == Volatile.Read(ref sequence)) return snapshot;
        }
        return null;
    }
}

public sealed class OperationMonitor
{
    readonly ConcurrentDictionary<string, OperationTrace> traces = new();
    readonly Dictionary<string, (int Sequence, long Reported, long Started, string Phase, string Context)> warned = new();
    public OperationTrace Track(string name) { var trace = new OperationTrace(name); traces[name] = trace; return trace; }
    // Called only by the watchdog's single observer. No reads from the device or routing objects.
    public void Observe(long now, Action<string> write)
    {
        foreach (var (name, trace) in traces)
        {
            var snapshot = trace.Read();
            if (snapshot is not { } s) continue;
            if (warned.TryGetValue(name, out var old) && (s.Sequence != old.Sequence || s.Phase.Length == 0))
            {
                write($"DIAG ended observed wait: {name} phase={old.Phase} context={old.Context}; observed {now - old.Started} ms since start (exact completion may be earlier)");
                warned.Remove(name);
            }
            if (s.Phase.Length == 0 || now - s.Started < 2000) continue;
            if (!warned.TryGetValue(name, out old) || now - old.Reported >= 60_000)
            {
                write($"DIAG pending: {name} phase={s.Phase} context={s.Context} managed-thread={s.Thread} elapsed={now - s.Started} ms; observed pending, cause unconfirmed");
                warned[name] = (s.Sequence, now, s.Started, s.Phase, s.Context);
            }
        }
    }
}

/// A slow status reader cannot block the watchdog timer or create overlapping collection tasks.
public sealed class DiagnosticStatusSampler(Func<string> read)
{
    int pending;
    string cached = "status not collected";
    long requested, updated;
    public bool Pending => Volatile.Read(ref pending) != 0;
    public string Snapshot(long now) => $"{Volatile.Read(ref cached)}; status-age={(Interlocked.Read(ref updated) == 0 ? "unknown" : (now - Interlocked.Read(ref updated)) + " ms")}" + (Pending ? $"; status-read pending {now - Interlocked.Read(ref requested)} ms" : "");
    public bool TryRefresh(Action<string>? onCollected = null)
    {
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return false;
        Interlocked.Exchange(ref requested, Environment.TickCount64);
        _ = Task.Run(() =>
        {
            try
            {
                var value = read();
                Volatile.Write(ref cached, value);
                Interlocked.Exchange(ref updated, Environment.TickCount64);
                onCollected?.Invoke(value);
            }
            catch (Exception e) { Volatile.Write(ref cached, "status collection failed: " + e.GetType().Name); }
            finally { Volatile.Write(ref pending, 0); }
        });
        return true;
    }
}
