using System;
using System.Collections.Generic;

using SharpDX;

namespace SuRGeoNix.Flyleaf
{
    public class OSDMessage
    {
        public static int       DefaultDuration { get; set; } = 3500;

        public Type             type;
        public string           msg;
        public long             startAt;
        public int              duration;
        public List<SubStyle>   styles;

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
        public enum Type
        { 
            Time,
            HardwareAcceleration,
            Volume,
            Mute,
            Open,
            Play,
            Paused,
            Buffering,
            Failed,
            AudioDelay,
            SubsDelay,
            SubsHeight,
            SubsFontSize,
            Subtitles,
            TorrentStats,
            TopLeft,
            TopLeft2,
            TopRight,
            TopRight2,
            BottomLeft,
            BottomRight
        }

        public OSDMessage(Type type, string msg = null, List<SubStyle> styles = null, int duration = -1)
        {
            this.type       = type;
            this.msg        = msg;
            this.duration   = duration == -1 ? DefaultDuration : duration;
            this.styles     = styles;
            this.startAt    = DateTime.UtcNow.Ticks;
        }
        public OSDMessage(Type type, string msg, SubStyle style, int duration = -1) : this(type, msg,new List<SubStyle>() { style }, duration) { }

        public void UpdateStyle(SubStyle style) { styles = new List<SubStyle>() { style }; }
    }
}