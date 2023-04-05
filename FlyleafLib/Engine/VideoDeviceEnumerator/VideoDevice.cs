using System;
using System.Collections.Generic;
using System.Linq;

namespace FlyleafLib.VideoDeviceEnumerator;

public class VideoDevice : IVideoDevice
{
    protected VideoDevice(string friendlyName, string symbolicLink, IEnumerable<IVideoFormat> formats)
    {
        FriendlyName = friendlyName;
        SymbolicLink = symbolicLink;
        Formats = formats;
    }

    public VideoDevice(string friendlyName, string symbolicLink) : this(
        friendlyName, symbolicLink,
        VideoFormatsViaMediaSource.GetVideoFormatsForVideoDevice(friendlyName, symbolicLink))
    {
    }

    private IVideoFormat DefaultVideoFormat => Formats.Where(f => f.SubType.Contains("MJPG") && f.FrameRate >= 30)
        .OrderByDescending(f => f.FrameSizeHeight).FirstOrDefault();

    private IVideoFormat DefaultSnapshotFormat =>
        Formats.Where(f => f.SubType.Contains("YUY")).OrderByDescending(f => f.FrameSizeHeight)
            .FirstOrDefault();

    public string FriendlyName { get; set; }
    public string SymbolicLink { get; }
    public Guid SourceType { get; set; }
    public IEnumerable<IVideoFormat> Formats { get; set; }

    public string DefaultVideoFormatUri => DefaultVideoFormat?.Uri;

    public string DefaultSnapshotFormatUri => DefaultSnapshotFormat?.Uri;

    public override string ToString()
    {
        return FriendlyName;
    }
}