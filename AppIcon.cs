using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PadLauncher;

/// The launcher's own icon, drawn at run time (a small gamepad on a dark rounded square), so there is no icon file to ship.
public static class AppIcon
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    static Icon? cached;

    public static Icon Make()
    {
        if (cached != null) return cached;
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = new GraphicsPath();
            back.AddArc(0, 0, 10, 10, 180, 90); back.AddArc(22, 0, 10, 10, 270, 90);
            back.AddArc(22, 22, 10, 10, 0, 90); back.AddArc(0, 22, 10, 10, 90, 90); back.CloseFigure();
            g.FillPath(new SolidBrush(Color.FromArgb(32, 40, 56)), back);
            var body = Color.FromArgb(230, 235, 245);
            using var pad = new GraphicsPath();
            pad.AddEllipse(3, 10, 12, 14); pad.AddEllipse(17, 10, 12, 14); pad.AddRectangle(new Rectangle(9, 10, 14, 11));
            g.FillPath(new SolidBrush(body), pad);
            var dark = new SolidBrush(Color.FromArgb(32, 40, 56));
            g.FillRectangle(dark, 8, 14, 6, 2); g.FillRectangle(dark, 10, 12, 2, 6);  // d-pad
            g.FillEllipse(new SolidBrush(Color.FromArgb(70, 160, 255)), 20, 12, 4, 4);   // buttons
            g.FillEllipse(new SolidBrush(Color.FromArgb(255, 170, 60)), 23, 16, 4, 4);
        }
        var h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        cached = (Icon)tmp.Clone();  // own copy, so the handle can be freed
        DestroyIcon(h);
        return cached;
    }
}
