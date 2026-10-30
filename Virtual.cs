using System.Diagnostics;
using System.Runtime.InteropServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Vortice.DirectInput;

namespace PadLauncher;

/// A ViGEmBus virtual Xbox 360 pad. FFXI sees it as "Controller (XBOX 360 For Windows)" for as long as it exists.
public sealed class ViGemPad : IVirtualPad
{
    static readonly (ushort Bit, Xbox360Button Button)[] Buttons =
    {
        (Xb.Up, Xbox360Button.Up), (Xb.Down, Xbox360Button.Down), (Xb.Left, Xbox360Button.Left), (Xb.Right, Xbox360Button.Right),
        (Xb.Start, Xbox360Button.Start), (Xb.Back, Xbox360Button.Back), (Xb.LeftThumb, Xbox360Button.LeftThumb), (Xb.RightThumb, Xbox360Button.RightThumb),
        (Xb.LB, Xbox360Button.LeftShoulder), (Xb.RB, Xbox360Button.RightShoulder),
        (Xb.A, Xbox360Button.A), (Xb.B, Xbox360Button.B), (Xb.X, Xbox360Button.X), (Xb.Y, Xbox360Button.Y),
    };
    readonly IXbox360Controller pad;
    /// XInput slot (0..3) the system gave this pad; -1 if unknown. Games that read XInput only look at the low slots.
    public int XInputSlot { get { try { return (int)pad.UserIndex; } catch { return -1; } } }
    public Guid DiGuid { get; internal set; }  // DirectInput instance id FFXI stores (resolved by VirtualPads)
    internal ViGemPad(IXbox360Controller pad) { this.pad = pad; pad.AutoSubmitReport = false; }
    public void Connect() => pad.Connect();
    public void Submit(PadState s)
    {
        foreach (var (bit, button) in Buttons) pad.SetButtonState(button, (s.Buttons & bit) != 0);
        pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.LT);
        pad.SetSliderValue(Xbox360Slider.RightTrigger, s.RT);
        pad.SetAxisValue(Xbox360Axis.LeftThumbX, s.LX);
        pad.SetAxisValue(Xbox360Axis.LeftThumbY, s.LY);
        pad.SetAxisValue(Xbox360Axis.RightThumbX, s.RX);
        pad.SetAxisValue(Xbox360Axis.RightThumbY, s.RY);
        pad.SubmitReport();
    }
    public void Disconnect() { try { pad.Disconnect(); } catch { } }
}

/// The set of virtual pads, created in slot order and kept alive: DirectInput hands out the same instance GUID per
/// creation order (tested 2026-10-05: 1st = {91394EF0-...-8001}, 2nd = {9D2A6820-...-8002}, same over 4 runs), which
/// is what keeps FFXI's saved padguid000 valid. GUIDs are read back from DirectInput rather than assumed.
public sealed class VirtualPads : IDisposable
{
    readonly ViGEmClient client;
    public List<ViGemPad> Pads { get; } = new();

    public static bool DriverInstalled()
    {
        try { using var c = new ViGEmClient(); return true; } catch { return false; }
    }

    /// The virtual pads' DirectInput instance GUIDs in the order DirectInput lists them. FFXI picks its pad by GUID but, measured
    /// 2026-10-06, reads the XInput slot that matches the GUID's position in that list, not the pad the GUID belongs to.
    public List<Guid> EnumOrder { get; private set; } = new();

    public VirtualPads(int count)
    {
        client = new ViGEmClient();  // throws if ViGEmBus is not installed
        try
        {
        for (int i = 0; i < count; i++)
        {
            var p = new ViGemPad(client.CreateXbox360Controller());
            p.Connect();
            Pads.Add(p);
        }
        Thread.Sleep(1200);  // let Windows enumerate the new devices
        ResolveGuids();
        if (Pads.Any(p => p.DiGuid == Guid.Empty) || EnumOrder.Count != count) throw new InvalidOperationException("Virtual controllers could not be identified uniquely. Close other virtual-controller tools and try again.");
        }
        catch { foreach (var p in Pads) p.Disconnect(); client.Dispose(); throw; }
    }

    /// Press A on one virtual pad at a time and see which DirectInput "XBOX 360" device reacts.
    void ResolveGuids()
    {
        using var di = DInput.DirectInput8Create();
        var devs = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .Where(d => d.ProductGuid.ToString().StartsWith("028e045e", StringComparison.OrdinalIgnoreCase)).ToList();
        EnumOrder = devs.Select(d => d.InstanceGuid).ToList();
        var opened = devs.Select(d =>
        {
            var dev = di.CreateDevice(d.InstanceGuid);
            dev.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            dev.SetDataFormat<RawJoystickState>(); dev.Acquire();
            return (Guid: d.InstanceGuid, Dev: dev);
        }).ToList();
        try
        {
            foreach (var p in Pads)
            {
                p.Submit(new PadState(Xb.A, 0, 0, 0, 0, 0, 0));
                var sw = Stopwatch.StartNew();
                while (p.DiGuid == Guid.Empty && sw.ElapsedMilliseconds < 1000)
                {
                    foreach (var (guid, dev) in opened)
                        if (!Pads.Any(o => o.DiGuid == guid) && dev.GetCurrentJoystickState().Buttons[0]) { p.DiGuid = guid; break; }
                    Thread.Sleep(5);
                }
                p.Submit(PadState.Neutral);
                Thread.Sleep(30);
            }
        }
        finally { foreach (var (_, dev) in opened) dev.Dispose(); }
    }

    public void Dispose() { foreach (var p in Pads) p.Disconnect(); client.Dispose(); }
}

/// Reads physical pads by address through DirectInput (own DirectInput object: used from the forwarding thread).
/// A pad that sleeps or drops makes Read return null; when it comes back (possibly with a new GUID) the next
/// Refresh finds it again by its address.
public sealed class DiSource : IPadSource, IDisposable
{
    readonly IDirectInput8 di = DInput.DirectInput8Create();       // forwarding thread only: opens and reads devices
    readonly IDirectInput8 listDi = DInput.DirectInput8Create();   // refresh task only: lists pads. A COM object is never used from two threads (heap corruption crash 2026-10-06 02:00)
    readonly object listLock = new();
    bool disposed;
    readonly Dictionary<string, (Guid Guid, IDirectInputDevice8 Dev)> open = new();
    volatile Dictionary<string, Guid> known = new();
    readonly HashSet<Guid> ignore;
    readonly IntPtr hwnd;  // a real window: DirectInput background input is gated on it (null handle: nothing arrives while another window has focus)
    public Action<string>? Log;
    readonly HashSet<string> seenInput = new();
    readonly RawPads raw = new();  // background-safe reading; DirectInput below is only the fallback (it delivers while our window is in front)
    public DiSource(IEnumerable<Guid> ignoreVirtual, IntPtr hwnd) { ignore = ignoreVirtual.ToHashSet(); this.hwnd = hwnd; raw.Log = m => Log?.Invoke("FORWARD " + m); }

    public IReadOnlyDictionary<string, Guid> Known => known;
    public bool RawKnows(string address) => raw.Knows(address);
    public void RawRescan() => raw.Reregister();

    public void Refresh()
    {
        raw.Reregister();
        var map = new Dictionary<string, Guid>();
        lock (listLock) { if (disposed) return; foreach (var p in Pads.ListWith(listDi).Where(p => !ignore.Contains(p.Guid))) map.TryAdd(p.Address, p.Guid); }
        known = map;
    }

    public PadState? Read(string address)
    {
        var map = known;
        if (!map.TryGetValue(address, out var guid)) { Drop(address); raw.Forget(address); return null; }
        if (raw.Ok && raw.TryGet(address, out var rs))
        {
            var rd = Dead(rs);
            if (!rd.IsNeutral && seenInput.Add(address)) Log?.Invoke($"FORWARD first input from {address} (raw input, works in the background): {rd}");
            return rd;
        }
        try
        {
            if (open.TryGetValue(address, out var o) && o.Guid != guid) Drop(address);  // same pad, new GUID: reopen
            if (!open.TryGetValue(address, out o))
            {
                var dev = di.CreateDevice(guid);
                dev.SetCooperativeLevel(hwnd, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                dev.SetDataFormat<RawJoystickState>();
                dev.Acquire();
                Log?.Invoke($"FORWARD reading {address} through DirectInput {guid:B}");
                open[address] = o = (guid, dev);
            }
            o.Dev.Poll();
            var s = o.Dev.GetCurrentJoystickState();
            var st = Dead(DiMap.FromDirectInput(s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.PointOfViewControllers[0], s.Buttons));
            if (!st.IsNeutral && seenInput.Add(address)) Log?.Invoke($"FORWARD first input from {address}: {st}");  // proof that DirectInput delivers in the background
            return st;
        }
        catch { Drop(address); return null; }  // input lost: the router sends neutral
    }

    /// Idle stick drift (a few hundred units) is not input: keep it from reaching the game and from the first-input log.
    const int StickDead = 3000, TriggerDead = 6;
    static short Z(short v) => Math.Abs((int)v) < StickDead ? (short)0 : v;
    static PadState Dead(PadState s) => s with { LX = Z(s.LX), LY = Z(s.LY), RX = Z(s.RX), RY = Z(s.RY), LT = s.LT < TriggerDead ? (byte)0 : s.LT, RT = s.RT < TriggerDead ? (byte)0 : s.RT };

    void Drop(string address)
    {
        if (open.Remove(address, out var o)) { try { o.Dev.Dispose(); } catch { } }
    }

    public void Dispose() { raw.Dispose(); foreach (var a in open.Keys.ToList()) Drop(a); di.Dispose(); lock (listLock) { disposed = true; listDi.Dispose(); } }
}

/// Wraps a virtual pad and logs each change between idle and active, so the log shows whether input really reaches it.
sealed class LoggedPad : IVirtualPad
{
    public static volatile bool Verbose;  // set from the VerboseInput setting; off by default so the log stays readable
    readonly IVirtualPad inner; readonly string name; readonly Action<string> log; bool active; DateTime lastLog;
    public LoggedPad(IVirtualPad inner, string name, Action<string> log) { this.inner = inner; this.name = name; this.log = log; }
    public void Submit(PadState s)
    {
        inner.Submit(s);
        bool now = !s.IsNeutral;
        if (Verbose && now != active && (DateTime.Now - lastLog).TotalMilliseconds > 700) { log($"FORWARD {name} -> virtual pad: {(now ? "active " + s : "idle")}"); lastLog = DateTime.Now; active = now; }
    }
}

/// Runs the forwarding loop on its own thread: a Router over DiSource and the virtual pads, one slot per game client.
public sealed class Forwarder : IDisposable
{
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    readonly object gate = new();
    readonly VirtualPads virtualPads;
    readonly DiSource source;
    readonly Router router;
    readonly Thread thread;
    volatile bool stop;
    readonly Action<string> log;

    // Tick timing, for the latency report: how long one forwarding pass takes (all slots).
    long ticks; double sumMs, maxMs;
    public (long Ticks, double AvgMs, double MaxMs) Stats { get { lock (gate) return (ticks, ticks == 0 ? 0 : sumMs / ticks, maxMs); } }

    public Forwarder(IReadOnlyList<string?> addresses, Action<string> log, IntPtr hwnd)
    {
        this.log = log;
        virtualPads = new VirtualPads(addresses.Count);
        try
        {
        source = new DiSource(virtualPads.Pads.Select(p => p.DiGuid), hwnd) { Log = log };
        router = new Router(source);
        // Slot i is the virtual pad that holds XInput slot i, and client i is given the i-th GUID in DirectInput's list:
        // that is the pad the game ends up reading for that GUID (see VirtualPads.EnumOrder).
        slotPads = virtualPads.Pads.OrderBy(p => p.XInputSlot < 0 ? int.MaxValue : p.XInputSlot).ToList();
        for (int i = 0; i < addresses.Count; i++) router.AddSlot($"slot {i + 1}", new LoggedPad(slotPads[i], $"slot {i + 1}", log), string.IsNullOrEmpty(addresses[i]) ? null : addresses[i]);
        source.Refresh();
        thread = new Thread(Loop) { IsBackground = true, Name = "pad-forwarder", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        }
        catch { virtualPads.Dispose(); source?.Dispose(); throw; }
    }

    public int Count => router.Slots.Count;
    readonly List<ViGemPad> slotPads;
    public Guid VirtualGuid(int slot) => slot < virtualPads.EnumOrder.Count ? virtualPads.EnumOrder[slot] : slotPads[slot].DiGuid;
    public int XInputSlot(int slot) => slotPads[slot].XInputSlot;
    public string? AddressOf(int slot) { lock (gate) return router.Slots[slot].Address; }
    public IReadOnlyDictionary<string, Guid> Connected => source.Known;
    /// The background reader has looked this pad up (so it can be hidden from games without losing its input).
    public bool RawKnows(string address) => source.RawKnows(address);
    public void RawRescan() => source.RawRescan();

    /// Bind a physical pad (by address) to slot i; null empties the slot. A pad that another slot has moves.
    public void Assign(int slot, string? address)
    {
        lock (gate) router.Assign(router.Slots[slot], string.IsNullOrEmpty(address) ? null : address);
        log($"FORWARD slot {slot + 1} <- {(string.IsNullOrEmpty(address) ? "(none)" : address)}");
    }

    public void Swap(int a, int b)
    {
        lock (gate) router.Swap(router.Slots[a], router.Slots[b]);
        log($"FORWARD swapped slot {a + 1} and slot {b + 1}");
    }

    void Loop()
    {
        timeBeginPeriod(1);
        try
        {
            var refresh = Stopwatch.StartNew();
            var t = new Stopwatch();
            Task? refreshing = null;
            while (!stop)
            {
                if (refresh.ElapsedMilliseconds > 1000 && (refreshing == null || refreshing.IsCompleted))
                {
                    refresh.Restart();
                    refreshing = Task.Run(() => { try { source.Refresh(); } catch { } });
                }
                t.Restart();
                lock (gate) { try { router.Tick(); } catch (Exception e) { log("FORWARD error: " + e.Message); } }
                var ms = t.Elapsed.TotalMilliseconds;
                lock (gate) { ticks++; sumMs += ms; if (ms > maxMs) maxMs = ms; }
                Thread.Sleep(1);
            }
        }
        finally { timeEndPeriod(1); }
    }

    public void Dispose()
    {
        stop = true;
        if (!thread.Join(3000)) throw new TimeoutException("Forwarding is still stopping; controllers were kept alive to avoid unsafe cleanup.");
        lock (gate) foreach (var s in router.Slots) s.Pad.Submit(PadState.Neutral);
        virtualPads.Dispose();
        source.Dispose();
    }
}
