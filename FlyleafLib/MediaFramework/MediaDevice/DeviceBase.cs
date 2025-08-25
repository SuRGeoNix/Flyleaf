using System.Linq;

namespace FlyleafLib.MediaFramework.MediaDevice;

public class AudioDevice : DeviceBase<AudioDeviceStream>
{
    public AudioDevice(string friendlyName, string symbolicLink) : base(friendlyName, symbolicLink)
        => Url = $"fmt://dshow?audio={FriendlyName}";
}

public class VideoDevice : DeviceBase<VideoDeviceStream>
{
    public VideoDevice(string friendlyName, string symbolicLink) : base(friendlyName, symbolicLink)
    {
        Streams = VideoDeviceStream.GetVideoFormatsForVideoDevice(friendlyName, symbolicLink);
        Url = Streams.Where(f => f.SubType.Contains("MJPG") && f.FrameRate >= 30).OrderByDescending(f => f.FrameSizeHeight).FirstOrDefault()?.Url;
    }

}

public class DeviceBase :DeviceBase<DeviceStreamBase>
{
    public DeviceBase(string friendlyName, string symbolicLink) : base(friendlyName, symbolicLink)
    {
    }
}

public class DeviceBase<T>
    where T: DeviceStreamBase
{
    public string   FriendlyName            { get; }
    public string   SymbolicLink            { get; }
    public IList<T>
                    Streams                 { get; protected set; }
    public string   Url                     { get; protected set; } // default Url

    public DeviceBase(string friendlyName, string symbolicLink)
    {
        FriendlyName = friendlyName;
        SymbolicLink = symbolicLink;

        Engine.Log.Debug($"[{(this is AudioDevice ? "Audio" : "Video")}Device] {friendlyName}");
    }

    public override string ToString() => FriendlyName;
}
