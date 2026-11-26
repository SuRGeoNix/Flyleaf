using System.Windows;
using System.Windows.Media;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Flyleaf;

internal enum TrayMenuGlyph { Open, CloseAll, Exit, Downloads }

/// <summary>Paints the native notification menu with the active Flyleaf palette.</summary>
internal sealed class TrayMenuRenderer(ResourceDictionary palette) : Forms.ToolStripRenderer
{
    readonly Drawing.Color surface = Color(palette, "Surface");
    readonly Drawing.Color border = Color(palette, "Border");
    readonly Drawing.Color text = Color(palette, "Text");
    readonly Drawing.Color secondary = Color(palette, "SecondaryText");
    readonly Drawing.Color accent = Color(palette, "AccentIcon");
    readonly Drawing.Color selection = Color(palette, "Selection");

    static Drawing.Color Color(ResourceDictionary palette, string name)
    {
        var color = ((SolidColorBrush)palette[$"FlyleafME.{name}Brush"]).Color;
        return Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new Drawing.SolidBrush(surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        var bounds = e.ToolStrip.ClientRectangle;
        using var pen = new Drawing.Pen(border);
        e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;
        float scale = e.Graphics.DpiX / 96;
        float radius = 4 * scale;
        var width = e.Item.Width - 8 * scale;
        var height = e.Item.Height - 2 * scale;
        if (width < 2 * radius || height < 2 * radius) return;

        using var path = new Drawing2D.GraphicsPath();
        path.AddRoundedRectangle(new Drawing.RectangleF(4 * scale, scale, width, height), new Drawing.SizeF(radius, radius));
        using var brush = new Drawing.SolidBrush(selection);
        var mode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
        e.Graphics.SmoothingMode = mode;
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        int inset = (int)Math.Round(9 * e.Graphics.DpiX / 96);
        int y = e.Item.Height / 2;
        using var pen = new Drawing.Pen(border);
        e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? text : secondary;
        base.OnRenderItemText(e);
        if (e.Item.Tag is not TrayMenuGlyph glyph) return;

        var state = e.Graphics.Save();
        try
        {
            float scaleX = e.Graphics.DpiX / 96;
            float scaleY = e.Graphics.DpiY / 96;
            e.Graphics.TranslateTransform(e.TextRectangle.Left - 22 * scaleX, (e.Item.Height - 16 * scaleY) / 2);
            e.Graphics.ScaleTransform(scaleX, scaleY);
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Drawing.Pen(e.Item.Selected && e.Item.Enabled ? accent : secondary, 1.2f)
            {
                StartCap = Drawing2D.LineCap.Round,
                EndCap = Drawing2D.LineCap.Round,
                LineJoin = Drawing2D.LineJoin.Round
            };

            switch (glyph)
            {
                case TrayMenuGlyph.Open:
                    e.Graphics.DrawRectangle(pen, 2, 3, 12, 10);
                    e.Graphics.DrawPolygon(pen, [new(7, 6), new(10, 8), new(7, 10)]);
                    break;
                case TrayMenuGlyph.Downloads:
                    e.Graphics.DrawLine(pen, 8, 2, 8, 10);
                    e.Graphics.DrawLines(pen, [new(4, 6), new(8, 10), new(12, 6)]);
                    e.Graphics.DrawLines(pen, [new(2, 10), new(2, 14), new(14, 14), new(14, 10)]);
                    break;
                case TrayMenuGlyph.CloseAll:
                    e.Graphics.DrawLines(pen, [new(5, 2), new(14, 2), new(14, 11)]);
                    e.Graphics.DrawRectangle(pen, 2, 5, 9, 9);
                    e.Graphics.DrawLine(pen, 5, 8, 8, 11);
                    e.Graphics.DrawLine(pen, 8, 8, 5, 11);
                    break;
                case TrayMenuGlyph.Exit:
                    e.Graphics.DrawLines(pen, [new(8, 2), new(2, 2), new(2, 14), new(8, 14)]);
                    e.Graphics.DrawLine(pen, 6, 8, 14, 8);
                    e.Graphics.DrawLines(pen, [new(11, 5), new(14, 8), new(11, 11)]);
                    break;
            }
        }
        finally { e.Graphics.Restore(state); }
    }
}
