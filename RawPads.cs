using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PadLauncher;

/// Reads game pads through Windows Raw Input, which keeps delivering while another window has focus
/// (DirectInput stops giving these Bluetooth Xbox pads' input to a program that is not in front, 2026-10-06).
/// Every report names its device, and the device path carries the Bluetooth address, so a pad is identified exactly
/// as elsewhere in the launcher. Reports are decoded with HidP (the pad's own descriptor), not with hard-coded offsets.
/// Layout assumed for Xbox Wireless Controller over Bluetooth LE (checked against XInput with tools\pad-probe-vigem raw):
///   X,Y = left stick, Rx,Ry = right stick (up = 0), Z = LT/RT on one axis (LT up from 32768, RT down), hat 1..8 (0 = none), buttons page 9 in XInput order.
public sealed class RawPads : IDisposable
{
    const uint WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_QUIT = 0x0012, WM_USER = 0x0400;
    const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIDI_PREPARSEDDATA = 0x20000005;
    const uint RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS { public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClassW(string cls, IntPtr instance);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint n, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRaw, uint cmd, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfoW(IntPtr dev, uint cmd, IntPtr data, ref uint size);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr preparsed, byte[] report, uint reportLen);
    [DllImport("hid.dll")] static extern int HidP_GetUsages(int type, ushort page, ushort link, [Out] ushort[] usages, ref uint len, IntPtr preparsed, byte[] report, uint reportLen);

    sealed record Dev(string Address, IntPtr Preparsed);

    readonly ConcurrentDictionary<IntPtr, Dev?> devices = new();           // raw device handle -> pad (null = not a pad we track)
    readonly ConcurrentDictionary<string, PadState> state = new();         // address -> latest decoded state
    readonly ConcurrentDictionary<string, long> lastTick = new();
    readonly WndProc proc;
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new();
    IntPtr hwnd;
    public Action<string>? Log;
    /// For diagnostics: every decoded report, with the raw usage values.
    public Action<string, string>? Trace;

    public RawPads()
    {
        proc = Handle;
        thread = new Thread(Loop) { IsBackground = true, Name = "raw-input" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(3000);
    }

    readonly ConcurrentDictionary<IntPtr, bool> arrived = new();
    public bool Ok => hwnd != IntPtr.Zero;
    public bool TryGet(string address, out PadState s) => state.TryGetValue(address, out s);
    public void Forget(string address) => state.TryRemove(address, out _);

    void Loop()
    {
        try
        {
            var cls = "PadLauncherRawInput";
            var wc = new WNDCLASS { lpfnWndProc = proc, hInstance = GetModuleHandleW(null), lpszClassName = cls };
            RegisterClassW(ref wc);
            hwnd = CreateWindowExW(0, cls, "", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) { Log?.Invoke("RAW could not create its window"); return; }
            if (!Register(true)) { Log?.Invoke("RAW RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error()); hwnd = IntPtr.Zero; return; }
            Enumerate();
            ready.Set();
            while (GetMessageW(out var m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
        }
        catch (Exception e) { Log?.Invoke("RAW thread error: " + e.Message); }
        finally
        {
            if (hwnd != IntPtr.Zero) { DestroyWindow(hwnd); hwnd = IntPtr.Zero; }
            foreach (var d in devices.Values) if (d != null) Marshal.FreeHGlobal(d.Preparsed);
            devices.Clear(); state.Clear();
            UnregisterClassW("PadLauncherRawInput", GetModuleHandleW(null));
            ready.Set();
        }
    }

    // notify = device arrival/removal messages. EVERY registration must ask for them: a registration without them switches them off, and
    // then a sleeping pad that returns (new device handle) is never seen as gone, so it is never looked up again (0.4.7 bug). The arrival
    // handler only logs, so the arrivals Windows resends on each registration do no harm.
    bool Register(bool notify = true)
    {
        uint f = RIDEV_INPUTSINK | (notify ? RIDEV_DEVNOTIFY : 0);
        var reg = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 5, dwFlags = f, hwndTarget = hwnd },  // gamepad
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 4, dwFlags = f, hwndTarget = hwnd },  // joystick
        };
        return RegisterRawInputDevices(reg, (uint)reg.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// Pads that connect after the first registration were not delivered reliably: register again, from the window thread.
    public void Reregister() { if (hwnd != IntPtr.Zero) PostMessageW(hwnd, WM_USER, IntPtr.Zero, IntPtr.Zero); }

    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? list, ref uint count, uint size);

    /// Known pads (by address) are looked up before anything is hidden: a pad that first appears already hidden may not answer the lookup.
    public bool Knows(string address) => devices.Values.Any(d => d != null && d.Address == address);

    /// Look up every HID device Windows lists now (name, address, descriptor), so the first report needs no lookup.
    void Enumerate()
    {
        uint n = 0, sz = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref n, sz) != 0 || n == 0) return;
        var list = new RAWINPUTDEVICELIST[n];
        if (GetRawInputDeviceList(list, ref n, sz) == uint.MaxValue) return;
        for (int i = 0; i < n; i++) if (list[i].dwType == 2) Lookup(list[i].hDevice);
    }

    IntPtr Handle(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_USER + 1) { DestroyWindow(h); hwnd = IntPtr.Zero; PostQuitMessage(0); return IntPtr.Zero; }
            if (msg == WM_INPUT) OnInput(lParam);
            else if (msg == WM_USER) { Register(); Enumerate(); }
            else if (msg == WM_INPUT_DEVICE_CHANGE && wParam.ToInt32() == 1 /* GIDC_ARRIVAL */) { if (arrived.TryAdd(lParam, true)) { Pads.InvalidatePathCache(); Log?.Invoke("RAW a game controller arrived"); } }
            else if (msg == WM_INPUT_DEVICE_CHANGE && wParam.ToInt32() == 2 /* GIDC_REMOVAL */)
            {
                arrived.TryRemove(lParam, out _);
                Pads.InvalidatePathCache();
                if (devices.TryRemove(lParam, out var d) && d != null) { state.TryRemove(d.Address, out _); Marshal.FreeHGlobal(d.Preparsed); }
            }
        }
        catch (Exception e) { Log?.Invoke("RAW error: " + e.Message); }
        return DefWindowProcW(h, msg, wParam, lParam);
    }

    Dev? Lookup(IntPtr dev)
    {
        if (devices.TryGetValue(dev, out var known)) return known;
        Dev? d = null; bool notPad = false;
        uint n = 0;
        GetRawInputDeviceInfoW(dev, RIDI_DEVICENAME, IntPtr.Zero, ref n);
        if (n > 0)
        {
            var buf = Marshal.AllocHGlobal((int)n * 2 + 2);
            try
            {
                GetRawInputDeviceInfoW(dev, RIDI_DEVICENAME, buf, ref n);
                var path = Marshal.PtrToStringUni(buf) ?? "";
                var addr = AddressOf(path); notPad = addr == null;
                uint psize = 0;
                GetRawInputDeviceInfoW(dev, RIDI_PREPARSEDDATA, IntPtr.Zero, ref psize);
                if (addr != null && psize > 0)
                {
                    var pre = Marshal.AllocHGlobal((int)psize);
                    GetRawInputDeviceInfoW(dev, RIDI_PREPARSEDDATA, pre, ref psize);
                    d = new Dev(addr, pre);
                    Log?.Invoke($"RAW device {addr} {path}");
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        else return null;  // lookup failed (device hidden or gone): try again next time, never remember a failure
        if (d != null) devices[dev] = d;
        else if (notPad) devices[dev] = null;  // a device that is simply not a Bluetooth pad; anything else may succeed later
        return d;
    }

    // Same rule as Pads.AddressOf for Bluetooth pads: "...&001122334401&ig_00#..." -> 00:11:22:33:44:01. Other devices are ignored.
    static string? AddressOf(string path)
    {
        var m = Regex.Match(path, @"&([0-9a-f]{12})&ig_", RegexOptions.IgnoreCase);
        return m.Success ? string.Join(":", Enumerable.Range(0, 6).Select(i => m.Groups[1].Value.Substring(i * 2, 2))).ToUpperInvariant() : null;
    }

    byte[] reportBuf = Array.Empty<byte>();
    readonly ushort[] usageBuf = new ushort[32];

    void OnInput(IntPtr hRaw)
    {
        uint size = 0, hdr = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, hdr);
        if (size == 0) return;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRaw, RID_INPUT, buf, ref size, hdr) == uint.MaxValue) return;
            var h = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (h.dwType != 2) return;  // RIM_TYPEHID
            var d = Lookup(h.hDevice);
            if (d == null) return;
            uint sizeHid = (uint)Marshal.ReadInt32(buf, (int)hdr), count = (uint)Marshal.ReadInt32(buf, (int)hdr + 4);
            for (uint i = 0; i < count; i++)
            {
                // Pads report 100+ times a second: the buffer is reused (window thread only) and the text dump is built only for diagnostics.
                if (reportBuf.Length != sizeHid) reportBuf = new byte[sizeHid];
                var report = reportBuf;
                Marshal.Copy(buf + (int)hdr + 8 + (int)(i * sizeHid), report, 0, (int)sizeHid);
                var s = Decode(d.Preparsed, report, Trace != null || Verbose, out var dump);
                state[d.Address] = s;
                lastTick[d.Address] = Environment.TickCount64;
                Trace?.Invoke(d.Address, dump + " -> " + s);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static uint? Val(IntPtr pre, byte[] r, ushort page, ushort usage) =>
        HidP_GetUsageValue(0, page, 0, usage, out var v, pre, r, (uint)r.Length) == HIDP_STATUS_SUCCESS ? v : null;

    static readonly (int Button, ushort Bit)[] ButtonMap =
    {
        (1, Xb.A), (2, Xb.B), (3, Xb.X), (4, Xb.Y), (5, Xb.LB), (6, Xb.RB), (7, Xb.Back), (8, Xb.Start), (9, Xb.LeftThumb), (10, Xb.RightThumb),  // order checked against XInput 2026-10-06
    };

    static short Stick(uint? v) => v is null ? (short)0 : (short)Math.Clamp((int)v.Value - 32768, -32767, 32767);
    static short StickUp(uint? v) => v is null ? (short)0 : (short)Math.Clamp(32767 - (int)v.Value, -32767, 32767);  // HID Y: up = 0
    static byte Trigger(uint? v) => v is null ? (byte)0 : (byte)Math.Clamp((int)Math.Round(v.Value * 255.0 / 1023.0), 0, 255);

    /// Diagnostics only (probe): list every value usage the descriptor answers for, to find which one is the right stick Y and the triggers.
    public static bool Verbose;

    PadState Decode(IntPtr pre, byte[] r, bool wantDump, out string dump)
    {
        var x = Val(pre, r, 1, 0x30); var y = Val(pre, r, 1, 0x31); var z = Val(pre, r, 1, 0x32); var rz = Val(pre, r, 1, 0x34);
        var rx = Val(pre, r, 1, 0x33); var hat = Val(pre, r, 1, 0x39);
        ushort b = 0;
        var list = usageBuf; uint len = (uint)list.Length;
        List<int>? pressed = wantDump ? new List<int>() : null;
        if (HidP_GetUsages(0, 9, 0, list, ref len, pre, r, (uint)r.Length) == HIDP_STATUS_SUCCESS)
            for (int i = 0; i < len; i++) { pressed?.Add(list[i]); foreach (var (btn, bit) in ButtonMap) if (btn == list[i]) b |= bit; }
        if (hat is >= 1 and <= 8)
        {
            int a = (int)hat.Value - 1;  // 0 = up, clockwise in 45 degree steps
            if (a is 7 or 0 or 1) b |= Xb.Up;
            if (a is 1 or 2 or 3) b |= Xb.Right;
            if (a is 3 or 4 or 5) b |= Xb.Down;
            if (a is 5 or 6 or 7) b |= Xb.Left;
        }
        dump = wantDump ? $"X={x} Y={y} Z={z} Rx={rx} Ry={rz} Hat={hat} Btn=[{string.Join(",", pressed!)}]" : "";
        if (Verbose)
        {
            var all = new List<string>();
            foreach (var (pg, from, to) in new[] { ((ushort)1, 0x30, 0x3F), ((ushort)2, 0xB0, 0xD0), ((ushort)0xD, 0x30, 0x40), ((ushort)0xFF00, 0, 0x40) })
                for (int u = from; u <= to; u++) if (Val(pre, r, pg, (ushort)u) is uint v) all.Add($"{pg:X}:{u:X}={v}");
            dump += "  ALL[" + string.Join(" ", all) + $"] len={r.Length} bytes={BitConverter.ToString(r)}";
        }
        // Z carries both triggers on one axis (LT pushes it up from 32768, RT pushes it down), as DirectInput showed. Verified against XInput.
        int zz = (int)(z ?? 32768);
        byte lt = (byte)Math.Clamp((int)Math.Round((zz - 32768) / 128.0), 0, 255), rt = (byte)Math.Clamp((int)Math.Round((32768 - zz) / 128.0), 0, 255);
        return new PadState(b, lt, rt, Stick(x), StickUp(y), Stick(rx), StickUp(rz));
    }

    public void Dispose()
    {
        if (hwnd != IntPtr.Zero) PostMessageW(hwnd, WM_USER + 1, IntPtr.Zero, IntPtr.Zero);
        if (!thread.Join(3000)) throw new TimeoutException("Controller reader is still stopping; its resources were kept safe.");
    }
}
