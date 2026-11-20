namespace Flyleaf.Controls.WPF;

/// <summary>Requests a registered panel without coupling the playback bar to its host.</summary>
public sealed class FlyleafPanelRequestedEventArgs(string panelId) : EventArgs
{
    public string PanelId { get; } = panelId;
}
