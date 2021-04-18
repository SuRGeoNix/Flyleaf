namespace FlyleafLib.Plugins.MediaStream
{
    public class StreamBase
    {
        /// <summary>
        /// The valid types that decoder's accepts and will be used to handle the stream
        /// </summary>
        public DecoderInput DecoderInput{ get; set; } = new DecoderInput();
        public Language     Language    { get; set; }
        public string       CodecName   { get; set; }
        public long         BitRate     { get; set; }

        /// <summary>
        /// Whether the current stream is enabled
        /// </summary>
        public bool         InUse       { get => _InUse; set { _InUse = value; if (value) Used++; } }
        bool _InUse;

        /// <summary>
        /// How many times has been used
        /// </summary>
        public int          Used        { get; set; }

        /// <summary>
        /// Tag/Opaque for plugins (mainly to match streams with their own streams)
        /// </summary>
        public object       Tag         { get; set; }
    }
}