using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Flyleaf.Controls.WPF;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Flyleaf;

internal sealed class TrayIcon : IDisposable
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly Forms.NotifyIcon icon;
    Drawing.Icon? image;
    FlyleafAppearanceOptions? appearance;
    (FlyleafTheme Theme, FlyleafAccent Accent, string? CustomColor)? iconPalette, menuPalette;
    bool disposed;

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
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
    }

    public void ApplyAppearance(FlyleafAppearanceOptions appearance)
    {
        if (disposed) return;
        this.appearance = appearance;
        var menuTheme = FlyleafThemeManager.Resolve(appearance.Theme);
        var taskbarTheme = ReadTaskbarTheme();
        var selectedCustomColor = appearance.Accent == FlyleafAccent.Custom ? appearance.CustomAccentColor : null;
        var menuKey = (menuTheme, appearance.Accent, selectedCustomColor);
        var iconKey = (taskbarTheme, appearance.Accent, selectedCustomColor);

        ResourceDictionary? menuResources = null;
        if (menuPalette != menuKey)
        {
            menuResources = CreatePalette(menuTheme, appearance);
            icon.ContextMenuStrip!.Renderer = new TrayMenuRenderer(menuResources);
            menuPalette = menuKey;
        }
        if (iconPalette != iconKey)
        {
            var resources = (taskbarTheme == menuTheme ? menuResources : null) ?? CreatePalette(taskbarTheme, appearance);
            var replacement = Rasterize(CreateLogo(resources));
            icon.Icon = replacement;
            icon.Visible = true;
            image?.Dispose();
            image = replacement;
            iconPalette = iconKey;
        }
    }

    static ResourceDictionary CreatePalette(FlyleafTheme theme, FlyleafAppearanceOptions appearance)
    {
        var resources = new ResourceDictionary();
        FlyleafThemeManager.Apply(resources, theme, appearance.Accent, appearance.CustomAccentColor);
        return resources;
    }

    static DrawingImage CreateLogo(ResourceDictionary resources)
    {
        Brush Brush(string name) => (Brush)resources[$"FlyleafME.Logo{name}Brush"];
        return FlyleafLogo.CreateImageSource(Brush("Mark"), Brush("Fold"), Brush("Play"), Brush("Outline"));
    }

    static FlyleafTheme ReadTaskbarTheme()
    {
        try
        {
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            if (Registry.GetValue(key, "SystemUsesLightTheme", null) is int value)
                return value != 0 ? FlyleafTheme.Light : FlyleafTheme.Dark;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return FlyleafThemeManager.Resolve(FlyleafTheme.System);
    }

    void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color) ||
            dispatcher.HasShutdownStarted)
            return;

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (appearance is { } current) ApplyAppearance(current);
        }));
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
        if (disposed) return;
        disposed = true;
        SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        appearance = null;
        icon.Visible = false;
        var menu = icon.ContextMenuStrip;
        icon.Dispose();
        menu?.Dispose();
        image?.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}
