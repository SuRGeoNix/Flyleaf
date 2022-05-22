using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public class ExternalVideoStream : ExternalStream
    {
        public double   FPS             { get; set; }
        public int      Height          { get; set; }
        public int      Width           { get; set; }

        public bool     HasAudio        { get; set; }
    }
}
