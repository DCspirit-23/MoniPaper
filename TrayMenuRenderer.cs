using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace PaperCare;

internal sealed class MoniPaperMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private static readonly Color Surface = Color.White;
    private static readonly Color Text = Color.FromArgb(36, 42, 39);
    private static readonly Color Muted = Color.FromArgb(102, 112, 106);
    private static readonly Color Border = Color.FromArgb(230, 232, 227);
    private static readonly Color Green = Color.FromArgb(33, 79, 64);
    private static readonly Color PaleGreen = Color.FromArgb(237, 243, 236);

    internal MoniPaperMenuRenderer()
        : base(new MoniPaperColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Surface);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.ClientSize);
        bounds.Width = Math.Max(0, bounds.Width - 1);
        bounds.Height = Math.Max(0, bounds.Height - 1);
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;

        var bounds = new Rectangle(
            4,
            1,
            Math.Max(1, e.Item.Size.Width - 8),
            Math.Max(1, e.Item.Size.Height - 2));
        using var path = RoundedRectangle(bounds, 6);
        using var fill = new SolidBrush(PaleGreen);
        using var pen = new Pen(Color.FromArgb(191, 207, 196));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        using var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        using var brush = new SolidBrush(Green);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        e.Graphics.DrawString("✓", font, brush, e.ImageRectangle, format);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        var y = bounds.Top + bounds.Height / 2;
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, bounds.Left + 12, y, bounds.Right - 12, y);
    }

    internal static Forms.ContextMenuStrip CreateMenu(
        EventHandler open,
        EventHandler toggle,
        EventHandler pause,
        EventHandler resume,
        EventHandler exit,
        out Forms.ToolStripMenuItem openItem,
        out Forms.ToolStripMenuItem toggleItem,
        out Forms.ToolStripMenuItem pauseItem,
        out Forms.ToolStripMenuItem resumeItem)
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new MoniPaperMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            AutoSize = true,
            MinimumSize = new Size(232, 0),
            Padding = new Forms.Padding(8, 8, 8, 8),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular)
        };
        openItem = CreateTrayMenuItem("打开面板", open);
        toggleItem = CreateTrayMenuItem("总开关", toggle);
        pauseItem = CreateTrayMenuItem("暂停 10 分钟", pause);
        resumeItem = CreateTrayMenuItem("恢复覆盖", resume);
        var separator = new Forms.ToolStripSeparator { Margin = new Forms.Padding(10, 4, 10, 4) };
        var exitItem = CreateTrayMenuItem("退出", exit);
        menu.Items.AddRange(new Forms.ToolStripItem[] { openItem, toggleItem, pauseItem, resumeItem, separator, exitItem });

        // Keep each action row aligned with the menu surface. ToolStrip's
        // default auto-size leaves rows at their text width even when the
        // drop-down has a wider minimum size, which makes the hover chrome
        // look detached from the menu edges.
        var itemWidth = menu.MinimumSize.Width - menu.Padding.Horizontal - openItem.Margin.Horizontal;
        foreach (var item in new[] { openItem, toggleItem, pauseItem, resumeItem, exitItem })
        {
            item.AutoSize = false;
            item.Size = new Size(Math.Max(1, itemWidth), 34);
        }

        var menuItems = new[] { openItem, toggleItem, pauseItem, resumeItem, exitItem };
        var itemMargin = openItem.Margin.Horizontal;
        var updatingLayout = false;
        menu.Layout += (_, _) =>
        {
            if (updatingLayout) return;
            updatingLayout = true;
            try
            {
                var stretchedWidth = Math.Max(1, menu.DisplayRectangle.Right - itemMargin);
                foreach (var item in menuItems)
                    if (item.Width != stretchedWidth || item.Height != 34)
                        item.Size = new Size(stretchedWidth, 34);
            }
            finally
            {
                updatingLayout = false;
            }
        };
        return menu;
    }

    internal static void RenderPreview(string outputPath, bool enabled = true, bool paused = false)
    {
        using var menu = CreateMenu(
            (_, _) => { },
            (_, _) => { },
            (_, _) => { },
            (_, _) => { },
            (_, _) => { },
            out _, out var toggleItem, out var pauseItem, out var resumeItem);
        toggleItem.Checked = enabled;
        pauseItem.Enabled = enabled && !paused;
        resumeItem.Enabled = paused;

        // Select a non-first item so the image verifies the actual hover state
        // renderer rather than only the default menu background.
        toggleItem.Select();
        menu.CreateControl();
        menu.Show(new Point(-32000, -32000));
        menu.Update();
        var size = menu.Size;
        if (size.Width < 1 || size.Height < 1)
            throw new InvalidOperationException("托盘菜单无法测量离屏尺寸。");

        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        menu.Close(Forms.ToolStripDropDownCloseReason.CloseCalled);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static Forms.ToolStripMenuItem CreateTrayMenuItem(string text, EventHandler onClick) =>
        new(text, null, onClick)
        {
            AutoSize = true,
            Padding = new Forms.Padding(10, 7, 10, 7),
            Margin = new Forms.Padding(3, 2, 3, 2),
            DisplayStyle = Forms.ToolStripItemDisplayStyle.Text
        };

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class MoniPaperColorTable : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ToolStripBorder => Border;
        public override Color MenuBorder => Border;
        public override Color MenuItemSelected => PaleGreen;
        public override Color MenuItemSelectedGradientBegin => PaleGreen;
        public override Color MenuItemSelectedGradientEnd => PaleGreen;
        public override Color MenuItemBorder => Color.FromArgb(191, 207, 196);
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
    }
}
