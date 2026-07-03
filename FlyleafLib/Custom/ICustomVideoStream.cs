namespace FlyleafLib.Custom;
#nullable enable
public interface ICustomVideoStream
{
    event Action<long>? StartTimeChanged;
    event Action<long>? CurrentTimeChanged;
    /// <summary>
    /// The real start date of the video archive. The default is minus 30 days before the current date.
    /// </summary>
    DateTime StartRealTime { get; }
    /// <summary>
    /// Target timestamp in milliseconds for video frame timestamp search operations
    /// </summary>
    long TargetTimestamp { get; }
    /// <summary>
    /// The initial timestamp of the video archive (in milliseconds). The default is minus 30 days before the current date.
    /// </summary>
    long StartTimestamp { get; }
    /// <summary>
    /// The first timestamp of the video response (in milliseconds).
    /// </summary>
    long FirstTimestampInGoP { get; }
    /// <summary>
    ///
    /// </summary>
    long CurrentTimestamp { get; }
    long LastTimestamp { get; }
    Double FrameDuration { get; }
    int FramesPerSecond { get; }
    long PictureGroupTimeStamp { get; }
    double PictureGroupFrameDuration { get; }
    int PictureGroupFrameIndex { get; set; }
    bool IsLive { get; }
    int ExpectedFrameIndex { get; }
    long FrameCount { get; set; }
    bool IsPlayStopMode { get; }
    bool SearchCompleted { get; set; }
    bool IsBufferReady { get; }
    int Mode { get; set; }
    double SpoolSpeed { get; set; }
    void Play(long timestamp, int playMode, double spoolSpeed);
    void ErrorByStreamingDetected(StreamingErrorCode errorCode); 
}
#nullable disable
