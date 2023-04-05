namespace FlyleafLib.VideoDeviceEnumerator;

public interface IVideoFormat
{
    string DeviceFriendlyName { get; }
    string MajorType { get; }
    string SubType { get; }
    int FrameSizeWidth { get; }
    int FrameSizeHeight { get; }
    int FrameRate { get; }
    string Uri { get; }
    string ToString();
}