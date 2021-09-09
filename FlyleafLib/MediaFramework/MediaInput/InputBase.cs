using System;
using System.IO;

using FlyleafLib.Plugins;

namespace FlyleafLib.MediaFramework.MediaInput
{
    public abstract class InputBase
    {
        public IPlugin          Plugin      { get; set; }

        public string           Url         { get; set; }
        public string           Protocol    { get; set; }
        public Stream           IOStream    { get; set; }
        public object           Tag         { get; set; }

        public InputData        InputData   { get; set; } = new InputData();

        public long             BitRate     { get; set; }
        public string           Codec       { get; set; }
        public Language         Language    { get; set; }
        public bool             Enabled     { get; set; }
    }
}
