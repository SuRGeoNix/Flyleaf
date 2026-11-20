using System.Windows.Controls;

namespace Flyleaf.Controls.WPF;

/// <summary>Allows the application to contribute menu groups before the Settings entry.</summary>
public sealed class FlyleafMenuBuildingEventArgs(ContextMenu menu) : EventArgs
{
    public ContextMenu Menu { get; } = menu;
}

/// <summary>Application opening policy for file/URL/paste actions initiated by the composed control.</summary>
public sealed class FlyleafOpenInputEventArgs(string input) : EventArgs
{
    public string Input { get; } = input;
    public bool Handled { get; set; }
}
