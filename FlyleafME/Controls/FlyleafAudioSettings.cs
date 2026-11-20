using static Flyleaf.Controls.WPF.FlyleafPanelUI;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Audio preferences, separate from current tracks, volume, mute and synchronization.</summary>
public sealed class FlyleafAudioSettings : FlyleafSettingsEditor
{
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config.Audio;

    protected override void Build()
    {
        BeginPage("Audio");
        if (config is not Config.AudioConfig c || player is not { } target) return;
        var defaults = new Config.AudioConfig();

        var languages = Group("Preferred languages", () => c.Languages = defaults.Languages.ToList());
        languages.Children.Add(Text("First choice at the top. Used when selecting audio for the next input; does not switch the current track. Keep at least one preference.", true));
        Languages(languages, () => c.Languages, v => c.Languages = v);

        var volume = Group("Volume behavior", () => { c.VolumeOffset = defaults.VolumeOffset; c.VolumeMax = defaults.VolumeMax; });
        Integer(volume, "Volume step", "%", () => c.VolumeOffset, v => c.VolumeOffset = v, 1, 100);
        Integer(volume, "Maximum volume", "%", () => c.VolumeMax, v => c.VolumeMax = v, 1, 1000);
        volume.Children.Add(Text("Maximum volume changes the current output gain immediately, including on reset. It also sets the range for subsequent volume adjustments. Current volume and mute stay in the Audio panel.", true));

        var synchronization = Group("Synchronization steps", () => { c.DelayOffset = defaults.DelayOffset; c.DelayOffset2 = defaults.DelayOffset2; });
        DelayStep(synchronization, "Delay step", () => c.DelayOffset, v => c.DelayOffset = v);
        DelayStep(synchronization, "Delay step 2", () => c.DelayOffset2, v => c.DelayOffset2 = v);
        synchronization.Children.Add(Text("Used by subsequent delay-step actions. Changing these does not change the current audio delay.", true));

        var advanced = Advanced();
        FlyleafAudioFilterList? filterEditor = null;
        var filters = Group("Audio filters · experimental", () => { c.Filters = null; filterEditor?.RefreshValues(true); }, advanced);
        filters.Children.Add(Text("An ordered FFmpeg filter chain. Changes are used on the next filter-graph build, including stream open or playback operations that rebuild it; editing does not explicitly reload the current graph. Invalid names or arguments are reported by the decoder.", true));
        filterEditor = new FlyleafAudioFilterList(() => c.Filters, v => c.Filters = v, action => Run(action));
        filters.Children.Add(filterEditor); RefreshWith(() => filterEditor.RefreshValues());
        var queue = Group("Decoder frame queue", () => c.MaxFrames = defaults.MaxFrames, advanced, () => !target.Audio.IsOpened);
        Integer(queue, "Maximum decoded frames", "frames", () => c.MaxFrames, v => c.MaxFrames = v, 1, 64, () => !target.Audio.IsOpened);
        queue.Children.Add(Text("Change while audio is closed. The decoder allocates audio buffers from this size; the value is locked while an audio stream is open.", true));

        var decoder = Group("Decoder", () => { c.MaxErrors = defaults.MaxErrors; c.Codec = defaults.Codec; c.CodecOpt = []; }, advanced);
        Integer(decoder, "Maximum decoding errors", "errors", () => c.MaxErrors, v => c.MaxErrors = v, 1, 100000);
        decoder.Children.Add(Text("Error limit is read when the decoder starts its next run. Decoder name and options apply on the next audio decoder open; this page does not reopen media.", true));
        TextEntry(decoder, "Decoder override · blank selects automatically", () => c.Codec ?? "", v => c.Codec = v,
            v => v.Any(char.IsWhiteSpace) ? "Use a decoder name without spaces, or leave blank for automatic selection." : null);
        Options(decoder, () => c.CodecOpt, v => c.CodecOpt = v);
        decoder.Children.Add(Text("Advanced codec names and option values are passed to the decoder. Unsupported names or values may prevent the next stream from opening.", true));
        Refresh();
    }
}
