using System.Windows;

namespace Flyleaf.Controls.WPF;

public sealed record FlyleafFloatingPanelBounds(double Left, double Top, double Width, double Height);

/// <summary>Presentation and geometry for a registered panel. The host owns storage.</summary>
public sealed record FlyleafPanelPlacement(string Id, FlyleafPanelMode Mode, FlyleafPanelSide Side, double Width,
    bool IsOpen, FlyleafFloatingPanelBounds? FloatingBounds = null)
{
    public bool PinnedInFullscreen { get; init; } = true;
}

public partial class FlyleafME
{
    readonly Dictionary<string, FlyleafPanelPlacement> restoredPanelPlacements = new(StringComparer.Ordinal);

    /// <summary>Captures open and closed panels, including temporarily hidden docks.</summary>
    public FlyleafPanelPlacement[] CapturePanelLayout()
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        var result = new List<FlyleafPanelPlacement>(restoredPanelPlacements.Values);
        foreach (var panel in panels.Values)
        {
            if (panel.FloatingWindow is { } window) RememberFloatingBounds(panel, window);
            double width = panel is { IsOpen: true, Mode: FlyleafPanelMode.Docked }
                ? RequestedDockWidthAt(panel.Side == FlyleafPanelSide.Left ? 0 : 4) : panel.PreferredWidth;
            var bounds = panel.FloatingBounds is { } rect
                ? new FlyleafFloatingPanelBounds(rect.Left, rect.Top, rect.Width, rect.Height) : null;
            result.Add(new(panel.Id, panel.Mode, panel.Side, width, panel.IsOpen, bounds)
            {
                PinnedInFullscreen = panel.PinnedInFullscreen
            });
        }
        return result.ToArray();
    }

    /// <summary>Restores registered panel placement after the control is loaded.</summary>
    public void RestorePanelLayout(IEnumerable<FlyleafPanelPlacement> placements)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(placements);
        if (!IsLoaded) throw new InvalidOperationException("Restore panels after the media element is loaded.");

        var occupied = new HashSet<(FlyleafPanelMode, FlyleafPanelSide)>();
        foreach (var saved in placements)
        {
            if (saved == null || string.IsNullOrWhiteSpace(saved.Id) ||
                !panelDefinitions.TryGetValue(saved.Id, out var definition) ||
                !definition.Supports(saved.Mode, saved.Side) || panels.ContainsKey(saved.Id) ||
                restoredPanelPlacements.ContainsKey(saved.Id)) continue;
            var placement = saved with
            {
                Width = double.IsFinite(saved.Width)
                    ? Math.Clamp(saved.Width, definition.MinimumWidth, definition.MaximumWidth) : definition.PreferredWidth
            };
            if (placement.IsOpen && placement.Mode != FlyleafPanelMode.Floating &&
                !occupied.Add((placement.Mode, placement.Mode == FlyleafPanelMode.Docked ? placement.Side : FlyleafPanelSide.Right)))
                placement = placement with { IsOpen = false };
            restoredPanelPlacements.TryAdd(placement.Id, placement);
        }

        foreach (var placement in restoredPanelPlacements.Values.Where(p => p.IsOpen)
            .OrderBy(p => p.Mode == FlyleafPanelMode.Docked ? 0 : 1).ToArray())
            ShowPanel(placement.Id);
    }

    /// <summary>Closes panels and restores their registered placement defaults.</summary>
    public void ResetPanelLayout()
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        CloseAllPanels();
        restoredPanelPlacements.Clear();
        foreach (var panel in panels.Values)
        {
            panel.Mode = panel.Definition.DefaultMode;
            panel.Side = panel.Definition.DefaultSide;
            panel.PreferredWidth = panel.Definition.PreferredWidth;
            panel.FloatingBounds = null;
            panel.PinnedInFullscreen = true;
            panel.RefreshPresentation();
        }
    }

    /// <summary>Requested dock space including splitters, even while an overlay hides the docks.</summary>
    public (double Left, double Right) GetRequestedDockInsets() =>
        (leftDock.Content == null ? 0 : RequestedDockWidthAt(0) + leftSplitter.Width,
         rightDock.Content == null ? 0 : RequestedDockWidthAt(4) + rightSplitter.Width);

    /// <summary>Dock space that fits the given composed width if all open docks are visible.</summary>
    public (double Left, double Right) GetDockInsets(double composedWidth)
    {
        var left = leftDock.Content as FlyleafPanel;
        var right = rightDock.Content as FlyleafPanel;
        var leftSplitterWidth = left == null ? 0 : leftSplitter.Width;
        var rightSplitterWidth = right == null ? 0 : rightSplitter.Width;
        var fitted = FlyleafDockWidths.Fit(composedWidth - 400 - leftSplitterWidth - rightSplitterWidth,
            RequestedDockWidthAt(0), left?.Definition.MinimumWidth ?? 0,
            RequestedDockWidthAt(4), right?.Definition.MinimumWidth ?? 0);
        return (fitted.Left + leftSplitterWidth, fitted.Right + rightSplitterWidth);
    }
}
