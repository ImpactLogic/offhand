namespace PadLauncher;

public static class AppIcon
{
    static Icon? cached;

    public static Icon Make() =>
        cached ??= System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
}
