using System.Runtime.InteropServices;

namespace PadLauncher;

// Read-only probes. Run on the watchdog's separate, single-flight evidence worker.
public static class ControllerEvidence
{
    const uint PresentOnly = 0x100;
    [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern uint CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    struct XInputState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint slot, out XInputState state);

    public static string[]? PresentDevices()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out var size, null, PresentOnly) != 0 || size == 0 || size > 1_048_576)
                return null;
            var buffer = new char[size];
            if (CM_Get_Device_ID_ListW(null, buffer, size, PresentOnly) == 0)
                return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return null;
    }

    public static string XInputSlots()
    {
        try
        {
            return string.Join(" ", Enumerable.Range(0, 4).Select(slot =>
            {
                uint result = XInputGetState((uint)slot, out _);
                return $"{slot}:" + (result == 0 ? "present" : result == 1167 ? "absent" : $"error({result})");
            }));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return "unavailable"; }
    }

    public static string BluetoothPresence(string[]? devices, string address)
    {
        var hex = address.Replace(":", "");
        if (devices == null || hex.Length != 12 || !hex.All(Uri.IsHexDigit)) return "unknown";
        bool parent = devices.Any(id => id.StartsWith("BTHLE\\DEV_" + hex + "\\", StringComparison.OrdinalIgnoreCase));
        bool hid = devices.Any(id => id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) &&
            id.Contains("&" + hex + "&IG_", StringComparison.OrdinalIgnoreCase));
        return $"parent={parent},hid={hid}";
    }
}

