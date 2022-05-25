using System;
using System.Collections.Generic;
using System.IO;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public class DemuxerInput : NotifyPropertyChanged
    {
        /// <summary>
        /// Url provided as a demuxer input
        /// </summary>
        public string   Url             { get; set; }

        /// <summary>
        /// Fallback url provided as a demuxer input
        /// </summary>
        public string   UrlFallback     { get; set; }

        /// <summary>
        /// IOStream provided as a demuxer input
        /// </summary>
        public Stream   IOStream        { get; set; }

        public Dictionary<string, string>
                        HTTPHeaders     { get; set; }

        public string   UserAgent       { get; set; }

        public string   Referrer        { get; set; }
    }
}
