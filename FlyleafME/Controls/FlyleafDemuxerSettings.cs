using FlyleafLib.MediaPlayer;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

public sealed class FlyleafDemuxerSettings : FlyleafSettingsEditor
{
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config.Demuxer;
    protected override void Build()
    {
        BeginPage("Demuxer");
        if (config is not Config.DemuxerConfig c) return;
        Config.DemuxerConfig Defaults() => FlyleafSettingsReset.GetDefaults(this).Demuxer;
        var formats = Group("Input format options", () => c.FormatOpt = new(Defaults().FormatOpt));
        formats.Children.Add(Text("Main input / video demuxer. Applied when the next format context opens. Names and values follow FFmpeg; unknown options are preserved.", true));
        Options(formats, () => c.FormatOpt, v => c.FormatOpt = v, "Main input · name / value");
        var audio = Group("External audio format options", () => c.AudioFormatOpt = new(Defaults().AudioFormatOpt));
        Options(audio, () => c.AudioFormatOpt, v => c.AudioFormatOpt = v, "Audio input · name / value");
        var subtitles = Group("External subtitle format options", () => c.SubtitlesFormatOpt = new(Defaults().SubtitlesFormatOpt));
        Options(subtitles, () => c.SubtitlesFormatOpt, v => c.SubtitlesFormatOpt = v, "Subtitle input · name / value");
        var advanced = Advanced();
        var packets = Group("Packet limits", () => { var defaults = Defaults(); c.BufferPackets = defaults.BufferPackets; c.MaxAudioPackets = defaults.MaxAudioPackets; }, advanced);
        Number(packets, "Buffered packet limit", "0 = no limit", () => c.BufferPackets, v => c.BufferPackets = (long)v, 0, int.MaxValue, Whole);
        Number(packets, "Audio packet limit", "0 = no limit", () => c.MaxAudioPackets, v => c.MaxAudioPackets = (long)v, 0, int.MaxValue, Whole);
        var opening = Group("Input discovery & overrides", () =>
        {
            var defaults = Defaults();
            c.AllowFindStreamInfo = defaults.AllowFindStreamInfo; c.ForceFormat = defaults.ForceFormat;
            c.ForceFPS = defaults.ForceFPS; c.IOStreamBufferSize = defaults.IOStreamBufferSize; c.FormatFlags = defaults.FormatFlags;
        }, advanced);
        Toggle(opening, "Discover stream information", "On the next input open. Turning this off can open faster but may leave essential stream information unavailable.", () => c.AllowFindStreamInfo, v => c.AllowFindStreamInfo = v);
        TextEntry(opening, "Forced format · blank selects automatically", () => c.ForceFormat ?? "", v => c.ForceFormat = v);
        Number(opening, "Forced frame rate", "fps · 0 = automatic", () => c.ForceFPS, v => c.ForceFPS = v, 0, 1000);
        Integer(opening, "Custom stream I/O buffer", "bytes", () => c.IOStreamBufferSize, v => c.IOStreamBufferSize = v, 4096, 67108864);
        Flags(opening, "Format flags · next input open", () => c.FormatFlags, v => c.FormatFlags = v);
        var timeouts = Group("Interrupts & timeouts", () =>
        {
            var defaults = Defaults();
            c.AllowInterrupts = defaults.AllowInterrupts; c.AllowReadInterrupts = defaults.AllowReadInterrupts;
            c.AllowTimeouts = defaults.AllowTimeouts; c.ExcludeInterruptFmts = defaults.ExcludeInterruptFmts.ToList();
            c.OpenTimeout = defaults.OpenTimeout; c.CloseTimeout = defaults.CloseTimeout;
            c.ReadTimeout = defaults.ReadTimeout; c.ReadLiveTimeout = defaults.ReadLiveTimeout; c.MaxErrors = defaults.MaxErrors;
            c.SeekTimeout = defaults.SeekTimeout;
        }, advanced);
        Toggle(timeouts, "Allow interrupts", "Custom interrupt callback is selected when the next input opens.", () => c.AllowInterrupts, v => c.AllowInterrupts = v);
        Toggle(timeouts, "Interrupt read operations", "Allows read operations to be interrupted where the protocol supports it.", () => c.AllowReadInterrupts, v => c.AllowReadInterrupts = v);
        Toggle(timeouts, "Check timeouts", "Timeouts require interrupts and protocol support. A timeout cannot forcibly cancel every protocol operation.", () => c.AllowTimeouts, v => c.AllowTimeouts = v);
        TextEntry(timeouts, "Formats excluded from interrupts · comma separated", () => string.Join(", ", c.ExcludeInterruptFmts ?? []),
            v => c.ExcludeInterruptFmts = v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        Duration(timeouts, "Open timeout", "seconds", () => c.OpenTimeout, v => c.OpenTimeout = v, positive: true);
        Duration(timeouts, "Close timeout", "seconds", () => c.CloseTimeout, v => c.CloseTimeout = v, positive: true);
        Duration(timeouts, "Read timeout", "seconds", () => c.ReadTimeout, v => c.ReadTimeout = v, positive: true);
        Duration(timeouts, "Live read timeout", "seconds", () => c.ReadLiveTimeout, v => c.ReadLiveTimeout = v, positive: true);
        Duration(timeouts, "Seek timeout", "seconds", () => c.SeekTimeout, v => c.SeekTimeout = v, positive: true);
        Integer(timeouts, "Maximum demuxing errors", "errors", () => c.MaxErrors, v => c.MaxErrors = v, 1, 100000);
        var nested = Group("Nested inputs", () =>
        {
            var defaults = Defaults();
            c.FormatOptToUnderlying = defaults.FormatOptToUnderlying; c.DefaultHTTPQueryToUnderlying = defaults.DefaultHTTPQueryToUnderlying;
            c.ExtraHTTPQueryParamsToUnderlying = new(defaults.ExtraHTTPQueryParamsToUnderlying);
        }, advanced);
        nested.Children.Add(Text("For formats that open underlying inputs. Used when those contexts open; editing does not reopen the current input.", true));
        Toggle(nested, "Forward format options", "Pass format options to underlying format contexts.", () => c.FormatOptToUnderlying, v => c.FormatOptToUnderlying = v);
        Toggle(nested, "Forward original HTTP query", "Pass the original URL query parameters to underlying inputs.", () => c.DefaultHTTPQueryToUnderlying, v => c.DefaultHTTPQueryToUnderlying = v);
        Options(nested, () => c.ExtraHTTPQueryParamsToUnderlying, v => c.ExtraHTTPQueryParamsToUnderlying = v, "Additional HTTP query parameters · name / value");
        Refresh();
    }
    static string? Whole(double value) => value == Math.Truncate(value) ? null : "Enter a whole number.";
}
