using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyleaf.Controls.WPF;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Flyleaf;

internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    Drawing.Icon? image;
    FlyleafTheme theme;
    FlyleafAccent accent;
    string? customAccentColor;

    public TrayIcon(Action open, Action closeAll, Action exit, Action? downloads = null)
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = true,
            ShowCheckMargin = false,
            MinimumSize = new Drawing.Size(180, 0),
            Padding = new Forms.Padding(3, 4, 3, 4)
        };
        void Add(string label, TrayMenuGlyph glyph, Action invoke)
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = glyph, Padding = new Forms.Padding(0, 4, 8, 4) };
            item.Click += (_, _) => invoke();
            menu.Items.Add(item);
        }
        Add("&Open Flyleaf", TrayMenuGlyph.Open, open);
        if (downloads != null) Add("&Downloads…", TrayMenuGlyph.Downloads, downloads);
        Add("&Close all windows", TrayMenuGlyph.CloseAll, closeAll);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("E&xit", TrayMenuGlyph.Exit, exit);
        icon = new Forms.NotifyIcon { Text = "Flyleaf", ContextMenuStrip = menu };
        icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void ApplyAppearance(FlyleafAppearanceOptions appearance)
    {
        var actualTheme = FlyleafThemeManager.Resolve(appearance.Theme);
        var selectedCustomColor = appearance.Accent == FlyleafAccent.Custom ? appearance.CustomAccentColor : null;
        if (image != null && theme == actualTheme && accent == appearance.Accent && customAccentColor == selectedCustomColor) return;

        var resources = new ResourceDictionary();
        FlyleafThemeManager.Apply(resources, actualTheme, appearance.Accent, appearance.CustomAccentColor);
        Brush Brush(string name) => (Brush)resources[$"FlyleafME.Logo{name}Brush"];
        var logo = FlyleafLogo.CreateImageSource(Brush("Mark"), Brush("Fold"), Brush("Play"), Brush("Outline"));
        var replacement = Rasterize(logo);
        icon.ContextMenuStrip!.Renderer = new TrayMenuRenderer(resources);
        icon.Icon = replacement;
        icon.Visible = true;
        image?.Dispose();
        image = replacement;
        theme = actualTheme;
        accent = appearance.Accent;
        customAccentColor = selectedCustomColor;
    }

    static Drawing.Icon Rasterize(ImageSource logo)
    {
        var size = Forms.SystemInformation.SmallIconSize;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(logo, new Rect(0, 0, size.Width, size.Height));
        var pixels = new RenderTargetBitmap(size.Width, size.Height, 96, 96, PixelFormats.Pbgra32);
        pixels.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pixels));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var bitmap = new Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        icon.Visible = false;
        var menu = icon.ContextMenuStrip;
        icon.Dispose();
        menu?.Dispose();
        image?.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}
