using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaperCare;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Point position, ref Size size, IntPtr src, ref Point source, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
}

internal sealed class Overlay : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80; return p; }
    }
    public Overlay(Rectangle bounds)
    {
        Text = "PaperCare 纸感覆盖层";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
    }
    public void Render(byte[] tile)
    {
        using var bitmap = TextureRenderer.Bitmap(Width, Height, tile);
        var screen = Native.GetDC(IntPtr.Zero);
        var dc = Native.CreateCompatibleDC(screen);
        IntPtr hbitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = Native.SelectObject(dc, hbitmap);
            var position = new Native.Point(Left, Top);
            var size = new Native.Size(Width, Height);
            var source = new Native.Point(0, 0);
            var blend = new Native.Blend { Alpha = 255, Format = 1 };
            if (!Native.UpdateLayeredWindow(Handle, screen, ref position, ref size, dc, ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (previous != IntPtr.Zero) Native.SelectObject(dc, previous);
            if (hbitmap != IntPtr.Zero) Native.DeleteObject(hbitmap);
            if (dc != IntPtr.Zero) Native.DeleteDC(dc);
            if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen);
        }
        Show();
        Raise();
    }
    public void Raise() => Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }
}
