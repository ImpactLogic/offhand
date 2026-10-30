using System.Text.RegularExpressions;
using Microsoft.Win32;
using Vortice.DirectInput;

namespace PadLauncher;

public static class Pads
{
    // Same key the FFXI Config tool writes (verified 2026-09-21: padguid000 = the selected pad's DirectInput GUID).
    public const string FfxiKey = @"SOFTWARE\WOW6432Node\PlayOnlineUS\SquareEnix\FinalFantasyXI";
    public const string PadValue = "padguid000";

    static IDirectInput8? di;
    static IDirectInput8 DI => di ??= DInput.DirectInput8Create();

    /// Our own virtual pads (while forwarding): never listed as controllers.
    public static HashSet<Guid> Hidden { get; } = new();

    public static List<Pad> List() => ListWith(DI);

    // The interface path of a DirectInput instance never changes while the instance exists, so it is looked up once. Before this, every
    // list (once a second from the forwarding refresh, every 2 s from the window tick) opened and released a DirectInput device for each
    // pad: about 130,000 create/dispose cycles per pad in a 24 h session, in a 32-bit process. A pad that comes back gets a new instance
    // id (or the same one with the same path), so the cache is pruned to the instances currently listed.
    // The cache is keyed by instance id, but Windows hands the SAME instance id to whichever pad arrives (seen 2026-10-17: Green and Blue Camo
    // took turns holding {49D8C990-...-8002}). A pad that came back under a reused id therefore kept the OTHER pad's path, so it was listed
    // with the wrong address, never logged "PAD connected", and showed "(not connected)" until Offhand was restarted (0.9.2-0.9.3). So an entry
    // expires after PathTtl, the whole cache is dropped when Raw Input reports a pad arriving or leaving (and for PathBypass afterwards the cache
    // is not trusted), and a listing that shows one address twice is repeated once without the cache.
    static readonly Dictionary<Guid, (string Path, long At)> pathCache = new();
    static readonly object pathLock = new();
    static readonly long PathTtlMs = 60_000, PathBypassMs = 10_000;
    static long bypassUntil, lastDupFix = -60_000;

    /// Forget every remembered interface path and do not trust the cache for the next few seconds. Call when a pad arrives, leaves or is refreshed by hand.
    public static void InvalidatePathCache()
    {
        lock (pathLock) { pathCache.Clear(); bypassUntil = Environment.TickCount64 + PathBypassMs; }
    }

    public static List<Pad> ListWith(IDirectInput8 di)
    {
        var pads = ListOnce(di);
        // At most once per 30 s: a device that legitimately lists twice must not switch the cache off for good.
        if (pads.GroupBy(p => p.Address).Any(g => g.Count() > 1) && Environment.TickCount64 - lastDupFix > 30_000)
        {
            lastDupFix = Environment.TickCount64;
            Log?.Invoke("PAD two controllers resolved to the same address (reused instance id): path cache dropped, listing again");
            InvalidatePathCache();
            pads = ListOnce(di);
        }
        return pads;
    }

    /// Set by the window so these events reach launcher.log.
    public static Action<string>? Log;

    static List<Pad> ListOnce(IDirectInput8 di)
    {
        var pads = new List<Pad>();
        var present = new HashSet<Guid>();
        foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly).Where(d => !Hidden.Contains(d.InstanceGuid)))
        {
            present.Add(d.InstanceGuid);
            string? path = null;
            lock (pathLock)
                if (Environment.TickCount64 >= bypassUntil && pathCache.TryGetValue(d.InstanceGuid, out var hit) && Environment.TickCount64 - hit.At < PathTtlMs) path = hit.Path;
            if (path == null)
            {
                path = "";
                try { using var dev = di.CreateDevice(d.InstanceGuid); path = dev.Properties.InterfacePath ?? ""; }
                catch { }
                if (path.Length > 0) lock (pathLock) pathCache[d.InstanceGuid] = (path, Environment.TickCount64);  // a failed lookup is retried next time
            }
            pads.Add(new Pad(d.InstanceGuid, d.ProductName.Trim(), AddressOf(path, d.InstanceGuid), path));
        }
        lock (pathLock) foreach (var g in pathCache.Keys.Where(g => !present.Contains(g)).ToList()) pathCache.Remove(g);
        return pads;
    }

    // Bluetooth pads: "...&rev_0509&001122334401&ig_00#..." -> 00:11:22:33:44:01.
    // Anything else: the HID instance part of the path, or the GUID as a last resort.
    static string AddressOf(string path, Guid guid)
    {
        var m = Regex.Match(path, @"&([0-9a-f]{12})&ig_", RegexOptions.IgnoreCase);
        if (m.Success) return string.Join(":", Enumerable.Range(0, 6).Select(i => m.Groups[1].Value.Substring(i * 2, 2))).ToUpperInvariant();
        var parts = path.Split('#');
        return parts.Length > 2 ? parts[2].ToUpperInvariant() : guid.ToString("B").ToUpperInvariant();
    }

    // Devices kept open for the highlight poll (window thread only). Opening, acquiring and releasing a device for every pad every 150 ms
    // (about 575,000 cycles per pad in 24 h) was the heaviest DirectInput churn in the app; now one open device per pad is polled.
    static readonly Dictionary<Guid, IDirectInputDevice8> pressDevices = new();

    /// Indexes of pads with any button held right now (for "press a button on the pad for ...").
    public static HashSet<Guid> Pressed(IEnumerable<Pad> pads)
    {
        var hit = new HashSet<Guid>();
        var list = pads.ToList();
        foreach (var g in pressDevices.Keys.Where(g => list.All(p => p.Guid != g)).ToList()) DropPressDevice(g);  // pad gone: release its device
        foreach (var p in list)
        {
            try
            {
                if (!pressDevices.TryGetValue(p.Guid, out var dev))
                {
                    dev = DI.CreateDevice(p.Guid);
                    dev.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    dev.SetDataFormat<RawJoystickState>();
                    dev.Acquire();
                    pressDevices[p.Guid] = dev;
                }
                dev.Poll();
                if (dev.GetCurrentJoystickState().Buttons.Any(b => b)) hit.Add(p.Guid);
            }
            catch { DropPressDevice(p.Guid); }  // input lost: reopen on the next poll
        }
        return hit;
    }

    /// Release the highlight devices (window hidden, or the program is closing).
    public static void ClosePressDevices() { foreach (var g in pressDevices.Keys.ToList()) DropPressDevice(g); }

    static void DropPressDevice(Guid g)
    {
        if (pressDevices.Remove(g, out var d)) { try { d.Dispose(); } catch { } }
    }

    public static string? ReadFfxiPad()
    {
        using var k = Registry.LocalMachine.OpenSubKey(FfxiKey);
        return k?.GetValue(PadValue) as string;
    }

    public static void WriteFfxiPad(string guid)
    {
        using var k = Registry.LocalMachine.OpenSubKey(FfxiKey, writable: true)
            ?? throw new InvalidOperationException("FFXI registry key not found: HKLM\\" + FfxiKey);
        k.SetValue(PadValue, guid, RegistryValueKind.String);
    }

    /// "bluetooth" when the HID path carries a Bluetooth address; anything else (USB while charging, unknown) is
    /// reported as such. The raw path is logged too, so the guess can be checked against the real thing.
    public static string Transport(Pad p) =>
        Regex.IsMatch(p.Path, @"&[0-9a-f]{12}&ig_", RegexOptions.IgnoreCase) ? "bluetooth" : "no-bt-address (USB or unknown)";

    public static string Format(Guid g) => g.ToString("B").ToUpperInvariant();
}
