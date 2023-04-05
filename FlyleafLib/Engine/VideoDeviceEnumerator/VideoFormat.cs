using System;
using System.Reflection;
using FFmpeg.AutoGen;
using Vortice.MediaFoundation;

namespace FlyleafLib.VideoDeviceEnumerator;

public class VideoFormat : IVideoFormat
{
    private static readonly Guid MfMediaTypeVideo =
        new(0x73646976, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);

    public VideoFormat(string videoDeviceName, Guid majorType, Guid subType, int frameSizeWidth,
        int frameSizeHeight, int frameRate,
        int frameRateDenominator)
    {
        DeviceFriendlyName = videoDeviceName;
        MajorType = MfMediaTypeVideo == majorType ? "Video" : "Unknown";
        SubType = GetPropertyName(subType);
        FrameSizeWidth = frameSizeWidth;
        FrameSizeHeight = frameSizeHeight;
        FrameRate = frameRate / frameRateDenominator;
        FFmpegFormat = GetFFmpegFormat(SubType);
        Uri =
            $"device://dshow?video={DeviceFriendlyName}&video_size={FrameSizeWidth}x{FrameSizeHeight}&framerate={FrameRate}&{FFmpegFormat}";
    }

    public VideoFormat(string videoDeviceName, string majorType, string subType, int frameSizeWidth,
        int frameSizeHeight, int frameRate,
        int frameRateDenominator)
    {
        DeviceFriendlyName = videoDeviceName;
        MajorType = majorType;
        SubType = subType;
        FrameSizeWidth = frameSizeWidth;
        FrameSizeHeight = frameSizeHeight;
        FrameRate = frameRate / frameRateDenominator;
        FFmpegFormat = GetFFmpegFormat(SubType);
        Uri =
            $"device://dshow?video={DeviceFriendlyName}&video_size={FrameSizeWidth}x{FrameSizeHeight}&framerate={FrameRate}&{FFmpegFormat}";
    }

    public string DeviceFriendlyName { get; }
    public string MajorType { get; }
    public string SubType { get; }
    public int FrameSizeWidth { get; }
    public int FrameSizeHeight { get; }
    public int FrameRate { get; }
    private string FFmpegFormat { get; }

    public string Uri { get; }

    public override string ToString()
    {
        return $"{SubType}, {FrameSizeWidth}x{FrameSizeHeight}, {FrameRate}FPS";
    }

    private static string GetPropertyName(Guid guid)
    {
        var type = typeof(VideoFormatGuids);
        foreach (var property in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            if (property.FieldType == typeof(Guid))
            {
                var temp = property.GetValue(null);
                if (temp is Guid value)
                    if (value == guid)
                        return property.Name.ToUpper();
            }

        return null; // not found
    }

    private static unsafe string GetFFmpegFormat(string subType)
    {
        switch (subType)
        {
            case "MJPG":
                var descriptorPtr = ffmpeg.avcodec_descriptor_get(AVCodecID.AV_CODEC_ID_MJPEG);
                return $"vcodec={Utils.BytePtrToStringUTF8(descriptorPtr->name)}";
            case "YUY2":
                return $"pixel_format={ffmpeg.av_get_pix_fmt_name(AVPixelFormat.AV_PIX_FMT_YUYV422)}";
            case "NV12":
                return $"pixel_format={ffmpeg.av_get_pix_fmt_name(AVPixelFormat.AV_PIX_FMT_NV12)}";
            default:
                return "";
        }
    }
}