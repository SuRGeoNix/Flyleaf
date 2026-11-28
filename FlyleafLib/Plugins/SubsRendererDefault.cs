using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.Plugins;

internal sealed class SubsRendererDefault : PluginBase, ISubtitlesRenderer
{
    internal const string DisplayName = "Basic text";
    internal static ISubtitlesTextRenderSession Session { get; } = new TextSession();

    public SubsRendererDefault() => Priority = int.MaxValue;

    public ISubtitlesRenderSession Open(SubtitlesRenderTrack track) => Session;

    sealed class TextSession : ISubtitlesTextRenderSession
    {
        public string ParseCue(string text, bool isAss, out List<SubStyle> styles)
        {
            if (isAss)
                return ParseSubtitles.SSAtoSubStyles(text, out styles);

            styles = null;
            return text;
        }

        public void Dispose() { }
    }
}
