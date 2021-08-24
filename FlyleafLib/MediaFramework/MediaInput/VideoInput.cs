using FlyleafLib.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlyleafLib.MediaFramework.MediaInput
{
    public class VideoInput : InputBase
    {
        //public IPluginInput
        //                    Plugin      { get; set; }

        public double       Fps         { get; set; }
        public int          Height      { get; set; }
        public int          Width       { get; set; }

        public bool         SearchedForSubtitles { get; set;}
    }
}
