namespace PadLauncher;

/// A connected game controller. Address is the stable identity (Bluetooth address or USB serial part of
/// the HID path); Guid is DirectInput's instance id, which is what FFXI stores and can change between sessions.
public record Pad(Guid Guid, string Name, string Address, string Path)
{
    public string Label => $"{Name}  [{Address}]";
    public override string ToString() => Label;
}

