using System;

namespace FlyleafLib.MediaFramework.MediaInput
{
    public class VideoInput : InputBase
    {
        public double       Fps         { get; set; }
        public int          Height      { get; set; }
        public int          Width       { get; set; }

        public bool         HasAudio    { get; set; }
        public bool         SearchedForSubtitles 
                                        { get; set;}
    }
}
