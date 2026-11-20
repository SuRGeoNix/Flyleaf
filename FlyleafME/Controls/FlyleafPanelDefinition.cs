using System.Windows;

namespace Flyleaf.Controls.WPF;

[Flags]
public enum FlyleafPanelModes { Overlay = 1, Docked = 2, Floating = 4, All = Overlay | Docked | Floating }
[Flags]
public enum FlyleafPanelSides { Left = 1, Right = 2, Both = Left | Right }

/// <summary>Immutable registration metadata. Content is created lazily, once per media element.</summary>
public sealed class FlyleafPanelDefinition
{
    public string Id { get; }
    public string Title { get; }
    internal Func<FlyleafPanelContext, FrameworkElement> CreateContent { get; }
    public FlyleafPanelModes AllowedModes { get; init; } = FlyleafPanelModes.All;
    public FlyleafPanelSides AllowedSides { get; init; } = FlyleafPanelSides.Both;
    public FlyleafPanelMode DefaultMode { get; init; } = FlyleafPanelMode.Overlay;
    public FlyleafPanelSide DefaultSide { get; init; } = FlyleafPanelSide.Right;
    /// <summary>Use the playback area below its window header instead of a side overlay.</summary>
    public bool FillOverlayArea { get; init; }
    /// <summary>The editor provides its own scrolling (for example settings with a fixed category rail).</summary>
    public bool HandlesScrolling { get; init; }
    public double MinimumWidth { get; init; } = 240;
    public double MaximumWidth { get; init; } = 600;
    public double PreferredWidth { get; init; } = 300;
    public double MinimumFloatingHeight { get; init; } = 220;
    public double PreferredFloatingHeight { get; init; } = 470;

    public FlyleafPanelDefinition(string id, string title, Func<FlyleafPanelContext, FrameworkElement> createContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(createContent);
        Id = id; Title = title; CreateContent = createContent;
    }

    public bool Supports(FlyleafPanelMode mode, FlyleafPanelSide side)
    {
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(side)) return false;
        var flag = mode switch
        {
            FlyleafPanelMode.Overlay => FlyleafPanelModes.Overlay,
            FlyleafPanelMode.Docked => FlyleafPanelModes.Docked,
            _ => FlyleafPanelModes.Floating
        };
        return (AllowedModes & flag) != 0 && (mode == FlyleafPanelMode.Floating ||
            (AllowedSides & (side == FlyleafPanelSide.Left ? FlyleafPanelSides.Left : FlyleafPanelSides.Right)) != 0);
    }

    internal void Validate()
    {
        if ((AllowedModes & ~FlyleafPanelModes.All) != 0 || AllowedModes == 0 ||
            (AllowedSides & ~FlyleafPanelSides.Both) != 0 || !Supports(DefaultMode, DefaultSide))
            throw new ArgumentException("Panel defaults must be permitted by its capabilities.");
        if (!double.IsFinite(MinimumWidth) || MinimumWidth <= 0 ||
            !double.IsFinite(MaximumWidth) || MaximumWidth < MinimumWidth ||
            !double.IsFinite(PreferredWidth) || PreferredWidth < MinimumWidth || PreferredWidth > MaximumWidth ||
            !double.IsFinite(MinimumFloatingHeight) || MinimumFloatingHeight <= 0 ||
            !double.IsFinite(PreferredFloatingHeight) || PreferredFloatingHeight < MinimumFloatingHeight)
            throw new ArgumentException("Panel size limits and preferred sizes must be finite and consistent.");
    }
}
