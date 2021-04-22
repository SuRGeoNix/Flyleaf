using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FlyleafLib
{
    public enum Status
    {
        None,

        Opening,
        OpenFailed,
        Opened,

        Playing,
        Seeking,
        Stopping,

        Paused,
        Stopped,
        Ended,
        Failed
    }
    public enum MediaType
    {
        Audio,
        Video,
        Subs
    }
    public enum UrlType
    {
        File,
        Torrent,
        Stream,
        Web,
        Other
    }

    public enum VolumeHandler
    {
        Session,
        Master,
        //Both // TODO: When you move out of Session upper limits volume up on master
    }

    public class DecoderInput
    {
        /// <summary>
        /// Any FFmpeg valid video url
        /// </summary>
        public string   Url         { get; set; }

        /// <summary>
        /// Any main Demuxer's existing stream index (demuxer.streams[])
        /// </summary>
        public int      StreamIndex { get; set; } = -1;

        /// <summary>
        /// Any valid custom Stream type (CanRead/CanSeek)
        /// </summary>
        public Stream   Stream      { get; set; }
    }
    public class Movie
    {
        public string   Url         { get; set; }
        public UrlType  UrlType     { get; set; }
        public string   Folder      { get; set; }
        public long     FileSize    { get; set; }

        public string   Title       { get; set; }
        public long     Duration    { get; set; }
        public int      Season      { get; set; }
        public int      Episode     { get; set; }
    }
    public struct AspectRatio
    {
        public static readonly AspectRatio Keep     = new AspectRatio(-1, 1);
        public static readonly AspectRatio Fill     = new AspectRatio(-2, 1);
        public static readonly AspectRatio Custom   = new AspectRatio(-3, 1);
        public static readonly AspectRatio Invalid  = new AspectRatio(-999, 1);

        public static readonly List<AspectRatio> AspectRatios = new List<AspectRatio>()
        {
            Keep,
            Fill,
            Custom,
            new AspectRatio(1, 1),
            new AspectRatio(4, 3),
            new AspectRatio(16, 9),
            new AspectRatio(16, 10),
            new AspectRatio(2.35f, 1),
        };

        public static implicit operator AspectRatio(string value) { return (new AspectRatio(value)); }

        public float Num { get; set; }
        public float Den { get; set; }

        public float Value
        {
            get => Num / Den;
            set  { Num = value; Den = 1; }
        }

        public string ValueStr
        {
            get => ToString();
            set => FromString(value);
        }

        public AspectRatio(float value) : this(value, 1) { }
        public AspectRatio(float num, float den) { Num = num; Den = den; }
        public AspectRatio(string value) { Num = Invalid.Num; Den = Invalid.Den; FromString(value); }

        public override bool Equals(object obj)
        {
            if ((obj == null) || ! GetType().Equals(obj.GetType()))
                return false;
            else
                return Num == ((AspectRatio)obj).Num && Den == ((AspectRatio)obj).Den;
        }
        public static bool operator ==(AspectRatio a, AspectRatio b) => a.Equals(b);
        public static bool operator !=(AspectRatio a, AspectRatio b) => !(a == b);

        public override int GetHashCode() { return (int) (Value * 1000); }

        public void FromString(string value)
        {
            if (value == "Keep")
                { Num = Keep.Num; Den = Keep.Den; return; }
            else if (value == "Fill")
                { Num = Fill.Num; Den = Fill.Den; return; }
            else if (value == "Custom")
                { Num = Custom.Num; Den = Custom.Den; return; }
            else if (value == "Invalid")
                { Num = Invalid.Num; Den = Invalid.Den; return; }

            string newvalue = value.ToString().Replace(',', '.');

            if (System.Text.RegularExpressions.Regex.IsMatch(newvalue.ToString(), @"^\s*[0-9\.]+\s*[:/]\s*[0-9\.]+\s*$"))
            {
                string[] values = newvalue.ToString().Split(':');
                if (values.Length < 2)
                            values = newvalue.ToString().Split('/');

                Num = float.Parse(values[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
                Den = float.Parse(values[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            }

            else if (float.TryParse(newvalue.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float result))
                { Num = result; Den = 1; }

            else
                { Num = Invalid.Num; Den = Invalid.Den; }
        }
        public override string ToString() { return this == Keep ? "Keep" : (this == Fill ? "Fill" : (this == Custom ? "Custom" : (this == Invalid ? "Invalid" : $"{Num}:{Den}"))); }
    }
    public class NotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
        {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        protected void Raise([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        //public void RaiseReset() { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
}