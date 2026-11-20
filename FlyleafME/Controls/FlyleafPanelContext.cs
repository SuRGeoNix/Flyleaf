using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Stable binding source across overlay, dock, and floating presentations.</summary>
public sealed class FlyleafPanelContext : NotifyPropertyChanged
{
    public FlyleafME? MediaElement { get; private set; }
    public string PanelId { get; }
    public Player? Player => MediaElement?.Player;
    public FlyleafUIConfig? UIConfig => MediaElement?.UIConfig;

    internal FlyleafPanelContext(FlyleafME mediaElement, string panelId)
    {
        MediaElement = mediaElement; PanelId = panelId;
    }

    public void Close() => RequireOwner().ClosePanel(PanelId);
    public void Show(FlyleafPanelMode mode, FlyleafPanelSide? side = null) =>
        RequireOwner().ShowPanel(PanelId, mode, side);

    FlyleafME RequireOwner() => MediaElement ?? throw new ObjectDisposedException(nameof(FlyleafPanelContext));
    internal void Refresh() { Raise(nameof(Player)); Raise(nameof(UIConfig)); }
    internal void Detach() { MediaElement = null; Raise(nameof(MediaElement)); Refresh(); }
}
