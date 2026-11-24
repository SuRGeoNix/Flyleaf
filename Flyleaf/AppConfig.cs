using System.Text.Json;
using System.Text.Json.Serialization;

using FlyleafLib;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

public enum ConfigProfileSource
{
    Defaults,
    CurrentPlayer
}

public enum StartupPanelLayoutMode
{
    Default,
    LastUsed,
    Saved
}

public class WindowOptions : NotifyPropertyChanged
{
    bool keepWindowRatioOnResize = true;
    bool restoreLegacyLayout;
    StartupPanelLayoutMode? startupPanelLayout;
    double width = 900, height = 560;
    double? left, top;
    public double Width
    {
        get => width;
        set { if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref width, value); }
    }
    public double Height
    {
        get => height;
        set { if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref height, value); }
    }

    public double? Left { get => left; set => Set(ref left, ValidateCoordinate(value)); }
    public double? Top { get => top; set => Set(ref top, ValidateCoordinate(value)); }

    public StartupPanelLayoutMode StartupPanelLayout
    {
        get => startupPanelLayout ?? (restoreLegacyLayout ? StartupPanelLayoutMode.LastUsed : StartupPanelLayoutMode.Default);
        set => Set(ref startupPanelLayout, value);
    }

    [JsonInclude, JsonPropertyName("RestorePanelLayout"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal bool? LegacyRestorePanelLayout
    {
        get => null;
        set => restoreLegacyLayout = value == true;
    }

    public FlyleafPanelPlacement[] PanelLayout { get; set; } = [];
    public FlyleafPanelPlacement[]? SavedPanelLayout { get; set; }

    internal FlyleafPanelPlacement[]? GetStartupPanelLayout() => StartupPanelLayout switch
    {
        StartupPanelLayoutMode.LastUsed => PanelLayout,
        StartupPanelLayoutMode.Saved => SavedPanelLayout,
        _ => null
    };

    internal void SaveCurrentPanelLayout(FlyleafPanelPlacement[] placements)
    {
        // Settings is open to issue the command. Remember its placement without reopening it at startup.
        SavedPanelLayout = placements.Select(panel => panel.Id == "settings" ? panel with { IsOpen = false } : panel).ToArray();
        StartupPanelLayout = StartupPanelLayoutMode.Saved;
    }

    internal void MergePanelLayout(FlyleafPanelPlacement[] baseline, FlyleafPanelPlacement[] snapshot, bool saveOpenState = false)
    {
        var prior = baseline.ToDictionary(panel => panel.Id, StringComparer.Ordinal);
        var saved = new Dictionary<string, FlyleafPanelPlacement>(StringComparer.Ordinal);
        foreach (var panel in PanelLayout ?? [])
            if (panel != null && !string.IsNullOrWhiteSpace(panel.Id)) saved.TryAdd(panel.Id, panel);

        bool changed = false;
        foreach (var panel in snapshot)
        {
            prior.TryGetValue(panel.Id, out var before);
            if (before != null && panel with { IsOpen = before.IsOpen } == before) continue;

            var shared = saved.GetValueOrDefault(panel.Id) ?? panel with { IsOpen = false };
            bool moved = before == null || (panel.Mode, panel.Side) != (before.Mode, before.Side);
            saved[panel.Id] = shared with
            {
                Mode = moved ? panel.Mode : shared.Mode,
                Side = moved ? panel.Side : shared.Side,
                Width = before == null || panel.Width != before.Width ? panel.Width : shared.Width,
                FloatingBounds = before == null || panel.FloatingBounds != before.FloatingBounds ? panel.FloatingBounds : shared.FloatingBounds
            };
            changed = true;
        }

        if (saveOpenState)
        {
            // The last closing player window supplies visibility, including
            // panels that stayed closed there throughout the session.
            foreach (var panel in snapshot)
            {
                if (saved.TryGetValue(panel.Id, out var placement))
                {
                    if (placement.IsOpen == panel.IsOpen) continue;
                    saved[panel.Id] = placement with { IsOpen = panel.IsOpen };
                }
                else if (panel.IsOpen) saved.Add(panel.Id, panel);
                else continue;

                changed = true;
            }
        }

        if (changed) PanelLayout = saved.Values.ToArray();
    }

    static double? ValidateCoordinate(double? value)
    {
        if (value.HasValue && !double.IsFinite(value.Value)) throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }

    // Constrains normal-window interactive resizing while video is open.
    public bool KeepWindowRatioOnResize
    {
        get => keepWindowRatioOnResize;
        set => Set(ref keepWindowRatioOnResize, value);
    }
}

public class NewWindowOptions : NotifyPropertyChanged
{
    ConfigProfileSource playerConfigSource, uiConfigSource;
    bool copyMediaSession, shareUIAppearance = true, syncPlayerSettings;

    // Initialization and continuing synchronization are independent policies.
    // Reserved for configurable window creation. Ctrl+N currently uses the source window's profile.
    public ConfigProfileSource PlaybackConfigSource { get => playerConfigSource; set => Set(ref playerConfigSource, value); }
    public ConfigProfileSource UIConfigSource { get => uiConfigSource; set => Set(ref uiConfigSource, value); }
    public bool CopyMediaSession { get => copyMediaSession; set => Set(ref copyMediaSession, value); }
    public bool ShareUIAppearance { get => shareUIAppearance; set => Set(ref shareUIAppearance, value); }
    public bool SyncPlayerSettings { get => syncPlayerSettings; set => Set(ref syncPlayerSettings, value); }
}

public sealed class StartupOptions : NotifyPropertyChanged
{
    bool singleInstance = true, keepInTray = true, allowTransparency;
    public bool SingleInstance { get => singleInstance; set => Set(ref singleInstance, value); }
    public bool KeepInTray { get => keepInTray; set => Set(ref keepInTray, value); }
    public bool AllowTransparency { get => allowTransparency; set => Set(ref allowTransparency, value); }
}

public class AppConfig : NotifyPropertyChanged
{
    WindowOptions window = new();
    NewWindowOptions newWindow = new();
    StartupOptions startup = new();
    bool confirmUnsavedQueueChanges = true;

    public string[] CustomFileExtensions { get; set; } = [];
    public bool ConfirmUnsavedQueueChanges { get => confirmUnsavedQueueChanges; set => Set(ref confirmUnsavedQueueChanges, value); }

    public StartupOptions Startup
    {
        get => startup;
        set { ArgumentNullException.ThrowIfNull(value); Set(ref startup, value); }
    }

    public WindowOptions Window
    {
        get => window;
        set { ArgumentNullException.ThrowIfNull(value); Set(ref window, value); }
    }

    public NewWindowOptions NewWindow
    {
        get => newWindow;
        set { ArgumentNullException.ThrowIfNull(value); Set(ref newWindow, value); }
    }

    static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public string Serialize() => JsonSerializer.Serialize(new Document { SchemaVersion = 1, AppConfig = this }, options);

    public static AppConfig Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, options)
            ?? throw new JsonException("Application configuration must be an object.");
        if (document.SchemaVersion != 1)
            throw new JsonException($"Unsupported application configuration version: {document.SchemaVersion}.");
        return document.AppConfig ?? throw new JsonException("AppConfig is required.");
    }

    public sealed class Document
    {
        public int SchemaVersion { get; set; }
        public AppConfig? AppConfig { get; set; }
    }
}
