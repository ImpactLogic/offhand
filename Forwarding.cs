namespace PadLauncher;

/// One Xbox 360 style report: what a physical pad is doing, or what a virtual pad is told to do.
public readonly record struct PadState(ushort Buttons, byte LT, byte RT, short LX, short LY, short RX, short RY)
{
    public static readonly PadState Neutral = default;  // nothing held, sticks centred
    public bool IsNeutral => this == Neutral;
}

/// XInput button bits (same values as XINPUT_GAMEPAD_*), shared by the mapping and the tests.
public static class Xb
{
    public const ushort Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020,
        LeftThumb = 0x0040, RightThumb = 0x0080, LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
}

/// Reads a physical pad by its stable address. Null = the pad is not connected right now.
public interface IPadSource { PadState? Read(string address); }

/// A virtual pad a game client sees. Submit always sends a full state, so a neutral one releases everything.
public interface IVirtualPad { void Submit(PadState s); }

/// Which physical pad feeds which virtual pad (= which game client). Rules: a physical pad feeds at most one client;
/// when a pad drops, is swapped or moved, the client's virtual pad is sent one neutral report (no stuck buttons).
public class Router
{
    public class Slot
    {
        public string Name = "";
        public IVirtualPad Pad = null!;
        public string? Address;      // physical pad currently feeding this slot
        internal bool Dirty = true;  // last thing sent is unknown or stale: send before forwarding
        internal PadState Last;
    }

    public List<Slot> Slots { get; } = new();
    readonly IPadSource source;
    public Router(IPadSource source) { this.source = source; }

    public Slot AddSlot(string name, IVirtualPad pad, string? address = null)
    {
        var s = new Slot { Name = name, Pad = pad };
        Slots.Add(s);
        if (address != null) Assign(s, address);
        return s;
    }

    /// Give a physical pad to a slot. If another slot had it, that slot is released (neutral) first: never two clients.
    public void Assign(Slot slot, string? address)
    {
        if (address != null)
            foreach (var o in Slots.Where(o => o != slot && o.Address == address)) Release(o);
        if (slot.Address == address) return;
        slot.Address = address;
        slot.Dirty = true;  // the old pad's held buttons must not survive the change
    }

    public void Release(Slot slot) => Assign(slot, null);

    /// Swap the pads of two slots, e.g. one pad's battery died and the spare takes over.
    public void Swap(Slot a, Slot b)
    {
        (a.Address, b.Address) = (b.Address, a.Address);
        a.Dirty = b.Dirty = true;
    }

    /// One forwarding pass. Call at a steady rate (e.g. 1 ms to 4 ms).
    public void Tick()
    {
        foreach (var s in Slots)
        {
            var st = s.Address == null ? null : source.Read(s.Address);
            if (st == null)
            {
                // No pad, or it dropped: one neutral report, then stay quiet until it is back.
                if (s.Dirty || !s.Last.IsNeutral) { s.Pad.Submit(PadState.Neutral); s.Last = PadState.Neutral; s.Dirty = false; }
                continue;
            }
            // On (re)bind the new pad's state replaces whatever was there; send only when it changed.
            if (s.Dirty || st.Value != s.Last) { s.Pad.Submit(st.Value); s.Last = st.Value; s.Dirty = false; }
        }
    }
}

/// DirectInput view of an Xbox pad (what pol.exe sees, and what ViGEm's virtual X360 pad shows) -> XInput-style state.
/// Layout confirmed on the virtual pad 2026-10-05: buttons 0..9 = A B X Y LB RB Back Start LS RS, POV hat in
/// hundredths of a degree (-1 = centred), X/Y = left stick (Y up = 0), RotationX/Y = right stick, Z = both triggers
/// on one axis (LT pushes it up from 32767, RT pushes it down), so LT+RT held together reads as centred.
public static class DiMap
{
    static readonly ushort[] ButtonBits = { Xb.A, Xb.B, Xb.X, Xb.Y, Xb.LB, Xb.RB, Xb.Back, Xb.Start, Xb.LeftThumb, Xb.RightThumb };

    static short Axis(int v) => (short)Math.Clamp(v - 32767, -32767, 32767);
    static short AxisUp(int v) => (short)Math.Clamp(32767 - v, -32767, 32767);  // DirectInput Y: up = 0

    public static PadState FromDirectInput(int x, int y, int z, int rx, int ry, int pov, IReadOnlyList<bool> buttons)
    {
        ushort b = 0;
        for (int i = 0; i < ButtonBits.Length && i < buttons.Count; i++) if (buttons[i]) b |= ButtonBits[i];
        if (pov >= 0 && pov < 36000)
        {
            int a = pov;
            if (a >= 31500 || a <= 4500) b |= Xb.Up;
            if (a >= 4500 && a <= 13500) b |= Xb.Right;
            if (a >= 13500 && a <= 22500) b |= Xb.Down;
            if (a >= 22500 && a <= 31500) b |= Xb.Left;
        }
        byte lt = (byte)Math.Clamp((int)Math.Round((z - 32768) / 128.0), 0, 255);
        byte rt = (byte)Math.Clamp((int)Math.Round((32768 - z) / 128.0), 0, 255);
        return new PadState(b, lt, rt, Axis(x), AxisUp(y), Axis(rx), AxisUp(ry));
    }
}
