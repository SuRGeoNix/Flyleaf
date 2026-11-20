using System.Linq;

namespace FlyleafLib;

/// <summary>Known filename extensions for media discovery and file filters, without a leading dot.</summary>
public static class MediaExtensions
{
    // VLC : https://github.com/videolan/vlc/blob/master/modules/gui/qt/dialogs/preferences/simple_preferences.cpp
    // Kodi: https://github.com/xbmc/xbmc/blob/master/xbmc/settings/AdvancedSettings.cpp

    internal static readonly List<string> AudioList =
    [
        // VLC
          "3ga" , "669" , "a52" , "aac" , "ac3"
        , "adt" , "adts", "aif" , "aifc", "aiff"
        , "au"  , "amr" , "aob" , "ape" , "caf"
        , "cda" , "dts" , "flac", "it"  , "m4a"
        , "m4p" , "mid" , "mka" , "mlp" , "mod"
        , "mp1" , "mp2" , "mp3" , "mpc" , "mpga"
        , "oga" , "ogg" , "oma" , "opus", "qcp"
        , "ra"  , "rmi" , "snd" , "s3m" , "spx"
        , "tta" , "voc" , "vqf" , "w64" , "wav"
        , "wma" , "wv"  , "xa"  , "xm"
    ];

    internal static readonly List<string> ImageList =
    [
        "apng", "avif", "bmp", "dpx", "exr", "gif", "ico", "jfif", "jp2",
        "jpeg", "jpg", "png", "ras", "svg", "tga", "tif", "tiff", "webp"
    ];

    internal static readonly List<string> SubtitlesTextList =
    [
        "ass", "ssa", "srt", "text", "vtt"
    ];

    internal static readonly List<string> SubtitlesBitmapList =
    [
        "sub", "sup"
    ];

    internal static readonly List<string> SubtitlesList = [..SubtitlesTextList, ..SubtitlesBitmapList];

    internal static readonly List<string> VideoList =
    [
        // VLC
          "3g2" , "3gp" , "3gp2", "3gpp", "amrec"
        , "amv" , "asf" , "avi" , "bik" , "divx"
        , "drc" , "dv"  , "f4v" , "flv" , "gvi"
        , "gxf" , "m1v" , "m2t" , "m2v" , "m2ts"
        , "m4v" , "mkv" , "mov" , "mp2v", "mp4"
        , "mp4v", "mpa" , "mpe" , "mpeg", "mpeg1"
        , "mpeg2","mpeg4","mpg" , "mpv2", "mts"
        , "mtv" , "mxf" , "nsv" , "nuv" , "ogg"
        , "ogm" , "ogx" , "ogv" , "rec" , "rm"
        , "rmvb", "rpl" , "thp" , "tod" , "ts"
        , "tts" , "vob" , "vro" , "webm", "wmv"
        , "xesc"

        // Additional
        , "dav"
    ];

    // Ogg can carry audio or video. Extension-based pickers can give audio priority.
    public static IReadOnlyList<string> Audio { get; } = AudioList.AsReadOnly();
    public static IReadOnlyList<string> Video { get; } = VideoList.AsReadOnly();
    public static IReadOnlyList<string> Images { get; } = ImageList.AsReadOnly();
    public static IReadOnlyList<string> Subtitles { get; } = SubtitlesList.AsReadOnly();
    public static IReadOnlyList<string> TextSubtitles { get; } = SubtitlesTextList.AsReadOnly();
    public static IReadOnlyList<string> BitmapSubtitles { get; } = SubtitlesBitmapList.AsReadOnly();

    public static bool IsImagePath(string path) =>
        Path.GetExtension(path) is { Length: > 1 } extension &&
        Images.Contains(extension[1..], StringComparer.OrdinalIgnoreCase);
}
