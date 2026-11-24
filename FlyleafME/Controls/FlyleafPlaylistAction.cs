namespace Flyleaf.Controls.WPF;

public enum FlyleafPlaylistAction { Add, AddUrl, AddInputs, Save, SaveAs, Export, New, AddFolder, Open, OpenUrl, OpenFolder, OpenInputs }
public sealed class FlyleafPlaylistActionEventArgs(FlyleafPlaylistAction action, IReadOnlyList<string>? inputs = null) : EventArgs
{
    public FlyleafPlaylistAction Action { get; } = action;
    public IReadOnlyList<string> Inputs { get; } = inputs ?? [];
}