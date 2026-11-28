using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FlyleafLib.Plugins.LibAss;

public sealed class LibAss : PluginBase, ISubtitlesRenderer
{
    public override Dictionary<string, string> GetDefaultOptions() => new() { ["Enabled"] = bool.TrueString };

    public ISubtitlesRenderSession Open(SubtitlesRenderTrack track)
    {
        if (track.Codec is not ("subrip" or "ass" or "ssa") || track.Header.IsEmpty ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return null;

        if (Config.Plugins.TryGetValue(Name, out var options) &&
            options.TryGetValue("Enabled", out string value) && bool.TryParse(value, out bool enabled) && !enabled)
            return null;

        return new LibAssSession(track);
    }
}
