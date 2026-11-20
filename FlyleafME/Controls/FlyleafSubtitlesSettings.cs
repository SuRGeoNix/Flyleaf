using static Flyleaf.Controls.WPF.FlyleafPanelUI;
using FlyleafLib.MediaPlayer;
using Flyleaf.FFmpeg;

namespace Flyleaf.Controls.WPF;

/// <summary>Subtitle selection/search and decoder preferences, without current stream state.</summary>
public sealed class FlyleafSubtitlesSettings : FlyleafSettingsEditor
{
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config.Subtitles;

    protected override void Build()
    {
        BeginPage("Subtitles");
        if (config is not Config.SubtitlesConfig c) return;
        var defaults = new Config.SubtitlesConfig();

        var appearance = new FlyleafSubtitleAppearanceEditor { ShowResetButton = false };
        var textAppearance = Group("Text appearance", appearance.Reset);
        appearance.SetBinding(FlyleafSubtitleAppearanceEditor.OptionsProperty, new System.Windows.Data.Binding("Context.UIConfig.Subtitles") { Source = this });
        textAppearance.Children.Add(appearance);

        var languages = Group("Preferred languages", () => c.Languages = defaults.Languages.ToList());
        languages.Children.Add(Text("First choice at the top. Used by subsequent subtitle selection and searches; does not switch the current track. Keep at least one preference.", true));
        Languages(languages, () => c.Languages, v => c.Languages = v);

        var search = Group("Subtitle search", () =>
        {
            c.SearchLocalOnInputType = defaults.SearchLocalOnInputType.ToList();
            c.SearchOnlineOnInputType = defaults.SearchOnlineOnInputType.ToList();
            if (c.SearchLocal != defaults.SearchLocal) c.SearchLocal = defaults.SearchLocal;
            if (c.SearchOnline != defaults.SearchOnline) c.SearchOnline = defaults.SearchOnline;
        });
        Toggle(search, "Search local subtitles", "Used by subsequent local searches through installed plugins. Enabling it does not run a search for the current video.", () => c.SearchLocal, v => c.SearchLocal = v);
        InputTypes(search, "Local search input types", () => c.SearchLocalOnInputType, v => c.SearchLocalOnInputType = v);
        Toggle(search, "Search online subtitles", "Enabling can immediately search for the current video through installed plugins. Provider availability and credentials belong to Plugins.", () => c.SearchOnline, v => c.SearchOnline = v);
        InputTypes(search, "Online search input types", () => c.SearchOnlineOnInputType, v => c.SearchOnlineOnInputType = v);
        search.Children.Add(Text("Choose All types or restrict the input types. Disable the corresponding search switch to prevent searching; at least one type remains selected in a restricted list.", true));

        var synchronization = Group("Synchronization steps", () => { c.DelayOffset = defaults.DelayOffset; c.DelayOffset2 = defaults.DelayOffset2; });
        DelayStep(synchronization, "Delay step", () => c.DelayOffset, v => c.DelayOffset = v);
        DelayStep(synchronization, "Delay step 2", () => c.DelayOffset2, v => c.DelayOffset2 = v);
        synchronization.Children.Add(Text("Used by subsequent delay-step actions. Current subtitle delay, enable/disable and tracks stay in the Subtitles panel.", true));

        var advanced = Advanced();
        if (player?.Config.Video is { } video)
        {
            Observe(video);
            var bitmap = Group("Bitmap subtitle scaling", () => video.BitmapSubsScaleQuality = SwsFlags.Bilinear | SwsFlags.Bitexact, advanced);
            Choice(bitmap, "Scaling quality", () => video.BitmapSubsScaleQuality, v => video.BitmapSubsScaleQuality = v,
                [(SwsFlags.FastBilinear | SwsFlags.Bitexact, "Fast bilinear"), (SwsFlags.Bilinear | SwsFlags.Bitexact, "Bilinear"),
                 (SwsFlags.Bicubic | SwsFlags.Bitexact, "Bicubic"), (SwsFlags.Lanczos | SwsFlags.Bitexact, "Lanczos")]);
            bitmap.Children.Add(Text("Used when the renderer next builds a bitmap subtitle scaler. This does not change text-subtitle styling or force recreation of the current bitmap.", true));
        }
        var decoder = Group("Decoder", () =>
        {
            c.MaxFrames = defaults.MaxFrames; c.MaxErrors = defaults.MaxErrors;
            c.Codec = defaults.Codec; c.CodecOpt = [];
        }, advanced);
        Integer(decoder, "Maximum decoded frames", "frames", () => c.MaxFrames, v => c.MaxFrames = v, 1, 1000);
        Integer(decoder, "Maximum decoding errors", "errors", () => c.MaxErrors, v => c.MaxErrors = v, 1, 100000);
        decoder.Children.Add(Text("Frame limit affects the active queue. Error limit is read when the decoder starts its next run. Decoder name and options apply on the next subtitle decoder open; this page does not reopen media.", true));
        TextEntry(decoder, "Decoder override · blank selects automatically", () => c.Codec ?? "", v => c.Codec = v,
            v => v.Any(char.IsWhiteSpace) ? "Use a decoder name without spaces, or leave blank for automatic selection." : null);
        Options(decoder, () => c.CodecOpt, v => c.CodecOpt = v);
        decoder.Children.Add(Text("Advanced codec names and option values are passed to the decoder. Unsupported names or values may prevent the next stream from opening.", true));
        Refresh();
    }
}
