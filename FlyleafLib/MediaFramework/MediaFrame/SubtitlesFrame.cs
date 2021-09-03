using System;
using System.Collections.Generic;
using System.Drawing;

namespace FlyleafLib.MediaFramework.MediaFrame
{
    public class SubtitlesFrame : FrameBase
    {
        public int          duration;
        public string       text;
        public List<SubStyle> subStyles;
    }
    public struct SubStyle
        {
            public SubStyles style;
            public Color value;

            public int from;
            public int len;

            public SubStyle(int from, int len, Color value) : this(SubStyles.COLOR, from, len, value) { }
            public SubStyle(SubStyles style, int from = -1, int len = -1, Color? value = null)
            {
                this.style  = style;
                this.value  = value == null ? Color.White : (Color)value;
                this.from   = from;
                this.len    = len;
            }
        }
        public enum SubStyles
        {
            BOLD,
            ITALIC,
            UNDERLINE,
            STRIKEOUT,
            FONTSIZE,
            FONTNAME,
            COLOR
        }
}
