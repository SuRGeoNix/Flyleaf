using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

public partial class MainWindow
{
    bool downloadClosePending;

    void InitializeDownloads()
    {
        MediaElement.RegisterPanel(new FlyleafPanelDefinition("downloads", "Downloads", c => new FlyleafDownloadsPanel(App.Instance.Downloads, c))
        {
            HandlesScrolling = true, MinimumWidth = 300, PreferredWidth = 410, PreferredFloatingHeight = 650,
            DefaultMode = FlyleafPanelMode.Docked
        });
        Closing += DownloadsClosing;
    }

    void AddDownloadsMenu(ContextMenu menu)
    {
        var downloads = new MenuItem { Header = "Downloads" };
        downloads.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuItemStyle");
        var add = new MenuItem { Header = "New download…" };
        add.Click += (_, _) =>
        {
            if (MediaElement.ShowPanel("downloads").Editor is FlyleafDownloadsPanel panel) panel.AddDownload();
        };
        var current = new MenuItem { Header = "Download current item…", IsEnabled = Player.Playlist.Selected != null && !MediaElement.Queue.IsOpening && !string.IsNullOrWhiteSpace(Player.Playlist.Url) && App.Instance.Downloads.CanAdd };
        current.Click += (_, _) =>
        {
            if (MediaElement.ShowPanel("downloads").Editor is FlyleafDownloadsPanel panel) panel.AddCurrentDownload();
        };
        var open = new MenuItem { Header = "Open panel…" };
        open.Click += (_, _) => MediaElement.ShowPanel("downloads");
        downloads.Items.Add(current); downloads.Items.Add(add); downloads.Items.Add(open); menu.Items.Add(downloads);
    }

    internal void ShowDownloads()
    {
        if (playlistDisposed) return;
        // Loaded can precede startup layout restoration in Window_FirstContentRendered.
        if (panelLayoutBaseline != null) MediaElement.ShowPanel("downloads");
        else
        {
            ContentRendered -= ShowDownloadsAfterLayout;
            ContentRendered += ShowDownloadsAfterLayout;
        }
    }

    void ShowDownloadsAfterLayout(object? sender, EventArgs e)
    {
        ContentRendered -= ShowDownloadsAfterLayout;
        if (!playlistDisposed) MediaElement.ShowPanel("downloads");
    }

    // With the tray disabled, the last window also owns the process lifetime.
    void DownloadsClosing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || App.Instance.IsExiting || App.Instance.Config.Startup.KeepInTray ||
            App.Instance.Windows.OfType<MainWindow>().Count() != 1 || !App.Instance.Downloads.HasPending) return;
        e.Cancel = true;
        if (downloadClosePending) return;
        downloadClosePending = true;
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                if (playlistDisposed) return;
                if (await App.Instance.ConfirmStopDownloadsAsync(this)) Close();
                else allowPlaylistClose = false;
            }
            finally { downloadClosePending = false; }
        }));
    }
}
