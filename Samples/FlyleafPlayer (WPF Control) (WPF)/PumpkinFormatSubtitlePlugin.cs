using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.Plugins;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlyleafPlayer
{
    public enum SubtitleDirection
    {
        Top,
        Bottom,
    }
    public class SubtitleInfo
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; } = string.Empty;

        [JsonPropertyName("direction")]
        public string? DirectionStr { get; set; }
        public SubtitleDirection Direction
        {
            get
            {
                if (Enum.TryParse<SubtitleDirection>(DirectionStr, out var type))
                {
                    return type;
                }
                return SubtitleDirection.Bottom;
            }
        }

        [JsonPropertyName("fontColor")]
        public string? FontColor { get; set; } = string.Empty;

        [JsonPropertyName("sizeScale")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int SizeScale { get; set; } = 1;

        [JsonPropertyName("changeText")]
        public string? ChangeText { get; set; } = string.Empty;

        [JsonPropertyName("asideText")]
        public string? AsideText { get; set; } = string.Empty;
    }
    internal class PumpkinFormatSubtitlePlugin : PluginBase, IFormatSubtitle
    {
        bool IFormatSubtitle.FormatSubtitle(ref SubtitlesFrame sframe)
        {
            try
            {
                var s = sframe.OriginalText;
                var originText = s = s.LastIndexOf(",,") == -1 ? s : s.Substring(s.LastIndexOf(",,") + 2).Replace("\\N", "\n").Trim();
                var subtitleInfo = JsonSerializer.Deserialize<SubtitleInfo>(originText);
                sframe.text = subtitleInfo.Content;
                return true;
            }
            catch { return false; }
        }
    }
}
