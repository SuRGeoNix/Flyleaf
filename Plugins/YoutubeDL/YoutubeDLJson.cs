using System.Collections.Generic;

namespace FlyleafLib.Plugins;

// Only deserialize fields used by the Flyleaf yt-dlp integration.
// yt-dlp adds fields regularly; unknown fields are ignored by System.Text.Json.
public class YoutubeDLJson : Format
{
    public string id               { get; set; }
    public string ie_key           { get; set; }
    public string extractor_key    { get; set; }
    public string extractor        { get; set; }
    public string original_url     { get; set; }
    public string title            { get; set; }
    public string webpage_url      { get; set; }
    public string _type            { get; set; }
    public int?   playlist_count   { get; set; }
    public int?   playlist_index   { get; set; }
    public string playlist         { get; set; }
    public string playlist_title   { get; set; }
    public bool?  is_live          { get; set; }

    public List<Format> formats { get; set; }

    // Manual captions take precedence; automatic captions are the fallback.
    public Dictionary<string, List<SubtitlesFormat>> subtitles          { get; set; }
    public Dictionary<string, List<SubtitlesFormat>> automatic_captions { get; set; }
}

public class SubtitlesFormat
{
    public string ext      { get; set; }
    public string url      { get; set; }
    public string name     { get; set; }
    public string protocol { get; set; }
    public Dictionary<string, string> http_headers { get; set; }
    public string cookies { get; set; }
}

public class FormatFragment
{
    public string url  { get; set; }
    public string path { get; set; }
}

public class Format
{
    public string format_id           { get; set; }
    public string format              { get; set; }
    public string ext                 { get; set; }
    public string url                 { get; set; }
    public string manifest_url        { get; set; }
    public string protocol            { get; set; }
    public string dynamic_range       { get; set; }
    public string language            { get; set; }
    public int?   language_preference { get; set; }
    public string vcodec              { get; set; }
    public string acodec              { get; set; }
    // yt-dlp's classification hints (audio_ext is NOT a muxed-audio flag).
    public string video_ext           { get; set; }
    public string audio_ext           { get; set; }
    // Unix timestamp. yt-dlp defers downloading a format until this time.
    public long?  available_at        { get; set; }

    // yt-dlp bitrate fields are in kbit/s.
    public double? tbr    { get; set; }
    public double? vbr    { get; set; }
    public double? abr    { get; set; }
    public double? fps    { get; set; }
    public double? asr    { get; set; }
    public double? height { get; set; }
    public double? width  { get; set; }
    public int?   audio_channels { get; set; }

    public Dictionary<string, string> http_headers { get; set; }
    public string cookies { get; set; }

    // Presence of fragments matters: a DASH fragment/base URL is not
    // interchangeable with a directly playable media URL.
    public List<FormatFragment> fragments { get; set; }
}
