using FlyleafLib.MediaPlayer;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;
public sealed class FlyleafVideoSettings : FlyleafSettingsEditor
{
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config.Video;
    protected override void Build()
    {
        BeginPage("Video");
        if (config is not Config.VideoConfig c) return;
        Config.VideoConfig Defaults() => FlyleafSettingsReset.GetDefaults(this).Video;
        var source = Group("Source selection", () => { var defaults = Defaults(); c.MaxVerticalResolutionCustom = defaults.MaxVerticalResolutionCustom; c.ZoomOffset = defaults.ZoomOffset; });
        Integer(source, "Preferred maximum height", "pixels · 0 = automatic", () => c.MaxVerticalResolutionCustom, v => c.MaxVerticalResolutionCustom = v, 0, 16384);
        source.Children.Add(Text("Used by source-selection plugins on subsequent selections. This does not resize or switch the current video stream.", true));
        Integer(source, "Zoom step", "%", () => c.ZoomOffset, v => c.ZoomOffset = v, 1, 100);
        var display = Group("Output", () =>
        {
            var defaults = Defaults();
            c.VSync = defaults.VSync; c.MaxOutputFps = defaults.MaxOutputFps; c.ClearScreen = defaults.ClearScreen;
            c.BackColor = defaults.BackColor; c.AllowHDRSwapchain = defaults.AllowHDRSwapchain; c.SuperResolution = defaults.SuperResolution; c.DoubleRate = defaults.DoubleRate;
        }, background: true);
        Toggle(display, "Vertical synchronization", "Used by subsequent frame presentation.", () => c.VSync != 0, v => c.VSync = v ? 1u : 0u);
        Number(display, "Maximum output frame rate", "fps", () => c.MaxOutputFps, v => c.MaxOutputFps = v, 1, 1000);
        display.Children.Add(Text("Limits rendered frames, particularly at increased speed. The decoder recalculates its frame-skipping policy as playback timing changes.", true));
        Toggle(display, "Clear video on open, stop and close", "Applies to subsequent clear operations.", () => c.ClearScreen, v => c.ClearScreen = v);
        ColorEntry(display, "Video background", () => c.BackColor, v => c.BackColor = v);
        Toggle(display, "Allow HDR output", "Requests swapchain reconfiguration now. HDR output also requires Windows HDR on the active display; SDR remains the fallback.", () => c.AllowHDRSwapchain, v => c.AllowHDRSwapchain = v, background: true);
        Toggle(display, "Super resolution", "Updates the current viewport when supported by the GPU and D3D11 video processor.", () => c.SuperResolution, v => c.SuperResolution = v, background: true);
        Toggle(display, "Present both deinterlaced fields", "Uses double-rate output for supported deinterlacing. Current deinterlace mode stays in Video adjustments.", () => c.DoubleRate, v => c.DoubleRate = v);
        var advanced = Advanced();
        var device = Group("Graphics device & surface", () => { var defaults = Defaults(); c.GPUAdapter = defaults.GPUAdapter; c.SwapChainFormat = defaults.SwapChainFormat; }, advanced);
        var adapters = new List<(string, string)> { ("", "Automatic adapter") };
        adapters.AddRange(Engine.Video.GPUAdapters.Values.Select(a => (a.Description, a.Description)));
        Choice(device, "Graphics adapter", () => c.GPUAdapter ?? "", v => c.GPUAdapter = v, adapters.Distinct().ToArray());
        device.Children.Add(Text("Adapter preference applies when the renderer is next created; it does not move current playback to another GPU.", true));
        Choice(device, "SDR swapchain format", () => c.SwapChainFormat, v => c.SwapChainFormat = v,
            Enum.GetValues<SwapChainFormat>().Select(v => (v, v.ToString())).ToArray());
        device.Children.Add(Text("Used on the next swapchain creation. Active HDR output can select its own floating-point format.", true));
        var decoder = Group("Decoder", () =>
        {
            var defaults = Defaults();
            c.HWAcceleration = defaults.HWAcceleration; c.DecoderThreads = defaults.DecoderThreads;
            c.AllowProfileMismatch = defaults.AllowProfileMismatch; c.ShowCorrupted = defaults.ShowCorrupted;
            c.LowDelay = defaults.LowDelay; c.AllowDropFrames = defaults.AllowDropFrames;
            c.MaxErrors = defaults.MaxErrors; c.Codec = defaults.Codec; c.CodecOpt = new(defaults.CodecOpt);
            c.MaxFrames = defaults.MaxFrames;
        }, advanced, background: true);
        decoder.Children.Add(Text("Decoder selection, threads and codec flags apply on the next video decoder open. Error limit is read when the decoder starts its next run. Editing does not reopen media.", true));
        Toggle(decoder, "Hardware decoding", "Allow hardware-accelerated decoding when supported by the codec and device.", () => c.HWAcceleration, v => c.HWAcceleration = v);
        Integer(decoder, "Decoded frame queue", "frames", () => c.MaxFrames, v => c.MaxFrames = v, 1, 64, background: true);
        decoder.Children.Add(Text("Changing queue size reallocates video frames and resynchronizes playback. GPU limits can reduce the effective size.", true));
        Integer(decoder, "Decoder threads", "0 = codec default", () => c.DecoderThreads, v => c.DecoderThreads = v, 0, 256);
        Toggle(decoder, "Allow hardware profile mismatch", "Allows acceleration even when the codec profile differs from the device's reported support.", () => c.AllowProfileMismatch, v => c.AllowProfileMismatch = v);
        Toggle(decoder, "Allow corrupted frames", "Requests output of frames marked as corrupt by the decoder.", () => c.ShowCorrupted, v => c.ShowCorrupted = v);
        Toggle(decoder, "Low-delay decoding", "Also changed by Playback's latency setters. This is the saved decoder preference, not a current stream latency measurement.", () => c.LowDelay, v => c.LowDelay = v);
        Toggle(decoder, "Allow decoder frame dropping", "Controls the decoder's skip-frame policy and its low-delay flags.", () => c.AllowDropFrames, v => c.AllowDropFrames = v);
        Integer(decoder, "Maximum decoding errors", "errors", () => c.MaxErrors, v => c.MaxErrors = v, 1, 100000);
        TextEntry(decoder, "Decoder override · blank selects automatically", () => c.Codec ?? "", v => c.Codec = v,
            v => v.Any(char.IsWhiteSpace) ? "Use a decoder name without spaces, or leave blank." : null);
        Options(decoder, () => c.CodecOpt, v => c.CodecOpt = v);
        Refresh();
    }
}
