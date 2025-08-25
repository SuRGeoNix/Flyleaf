namespace FlyleafLib.MediaFramework.MediaDemuxer;

public class DemuxerInput : NotifyPropertyChanged
{
    /// <summary>
    /// Url provided as a demuxer input
    /// </summary>
    public string   Url             { get => _Url; set => _Url = FixFileUrl(value); }
    string _Url;

    /// <summary>
    /// Fallback url provided as a demuxer input
    /// </summary>
    public string   UrlFallback     { get => _UrlFallback; set => _UrlFallback = FixFileUrl(value); }
    string _UrlFallback;

    /// <summary>
    /// IOStream provided as a demuxer input
    /// </summary>
    public Stream   IOStream        { get; set; }

    public Dictionary<string, string>
                    HTTPHeaders     { get; set; }

    public string   UserAgent       { get; set; }

    public string   Referrer        { get; set; }
}
