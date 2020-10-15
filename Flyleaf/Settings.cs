using System;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;

using static SuRGeoNix.Flyleaf.MediaRouter;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Linq;
using static SuRGeoNix.Flyleaf.Settings;
using Keys = SuRGeoNix.Flyleaf.Settings.Keys;
using System.IO;

namespace SuRGeoNix.Flyleaf
{
    [Serializable]
    [XmlRoot("Settings")]
    public class SettingsLoad
    {
        public Main         main        = new Main();
        public Bar          bar         = new Bar();
        public Keys         keys        = new Keys();
        public HookForm     hookForm    = new HookForm();
        public Subtitles    subtitles   = new Subtitles();
        public Audio        audio       = new Audio();
        public Video        video       = new Video();

        public Surface[]    surfaces;
        public Message[]    messages;


        public class Surface
        {
            [XmlElement("Font")]
            public string   Font2       { get; set; }
            [XmlElement("Color")]
            public string   Color2      { get; set; }
            public Point    Position    { get; set; }
            public bool     OnViewPort  { get; set; }
            public bool     RectEnabled { get; set; }
            [XmlElement("RectColor")]
            public string   RectColor2  { get; set; }
            public Padding  RectPadding { get; set; }
        }

        public class Message
        {
            public VisibilityMode Visibility{ get; set; }
            public OSDSurfaces Surface      { get; set; }
        }

        public class Main
        {
            public ActivityMode _PreviewMode        { get; set; }
            public bool         AllowDrop           { get; set; }
            public bool         AllowFullScreen     { get; set; }
            public bool         AllowTorrents       { get; set; }
            public int          IdleTimeout         { get; set; }
            public bool         EmbeddedList        { get; set; }
            public bool         HideCursor          { get; set; }
            [XmlElement("ClearBackColor")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string       ClearBackColor2     { get { return (new ColorConverter()).ConvertToString(ClearBackColor); } set { ClearBackColor = (Color) (new ColorConverter()).ConvertFromString(value); } }
            [XmlIgnore]
            public Color        ClearBackColor      { get; set; }
            public int          MessagesDuration    { get; set; }
        }

        public class Subtitles
        {
            public bool     _Enabled                { get; set; }
            public string[] Languages               { get; set; }
            public DownloadSubsMode DownloadSubs    { get; set; }
        }

        public class Audio
        {
            public bool     _Enabled    { get ; set; }
        }

        public class Video
        {       
            public bool         HardwareAcceleration{ get ; set; }
            public ViewPorts    AspectRatio         { get ; set; }
            public float        CustomRatio         { get ; set; }
            public int          DecoderThreads      { get ; set; }
            public int          QueueMinSize        { get ; set; }
            public int          QueueMaxSize        { get ; set; }
            public bool         VSync               { get ; set; }
        }
    }


    [Serializable]
    [Category("Flyleaf UI")]
    [TypeConverter(typeof(FlyleafTypeConverter))]
    public class Settings
    {
        //MediaRouter         player;

        
        internal Action       PropertyChanged;

        public Main         main        = new Main();
        public Bar          bar         = new Bar();
        public Keys         keys        = new Keys();
        public HookForm     hookForm    = new HookForm();

        public Audio        audio;
        public Subtitles    subtitles;
        public Video        video;
            
        public Surface[]    surfaces;
        public SurfacesAll  surfacesAll;
        public Message[]   messages;

        public enum OSDSurfaces
        {
            TopLeft,
            TopRight,
            TopLeft2,
            TopRight2,
            BottomLeft,
            BottomRight,
            BottomCenter
        }
        public static string EnumToString(OSDSurfaces surface)
        {
            switch (surface)
            {
                case OSDSurfaces.TopLeft:
                    return "tl";
                case OSDSurfaces.TopRight:
                    return "tr";
                case OSDSurfaces.TopLeft2:
                    return "tl2";
                case OSDSurfaces.TopRight2:
                    return "tr2";
                case OSDSurfaces.BottomLeft:
                    return "bl";
                case OSDSurfaces.BottomRight:
                    return "br";
                case OSDSurfaces.BottomCenter:
                    return "bc";
            }

            return "";
        }
        public static OSDSurfaces StringToEnum(string surface)
            {
                switch (surface)
                {
                    case "tl":
	                    return OSDSurfaces.TopLeft;
                    case "tr":
	                    return OSDSurfaces.TopRight;
                    case "tl2":
	                    return OSDSurfaces.TopLeft2;
                    case "tr2":
	                    return OSDSurfaces.TopRight2;
                    case "bl":
	                    return OSDSurfaces.BottomLeft;
                    case "br":
	                    return OSDSurfaces.BottomRight;
                    case "bc":
	                    return OSDSurfaces.BottomCenter;
                }

                return OSDSurfaces.TopLeft;
            }

        private Settings() { }
        public Settings(MediaRouter player)
        {
            //this.player = player;
            var t1 = Enum.GetValues(typeof(OSDSurfaces));
            surfaces = new Surface[t1.Length];
            for (int i=0; i<t1.Length; i++)
            {
                surfaces[i] = new Surface(player.renderer, (OSDSurfaces)t1.GetValue(i));
            }

            t1 = Enum.GetValues(typeof(OSDMessage.Type));
            messages = new Message[t1.Length];
            for (int i=0; i<t1.Length; i++)
            {
                messages[i] = new Message(player.renderer, (OSDMessage.Type)t1.GetValue(i));
            }

            surfacesAll = new SurfacesAll(player.renderer, surfaces);

            audio       = new Audio(player);
            subtitles   = new Subtitles(player);
            video       = new Video(player);

            bar. PropertyChanged = () => { PropertyChanged?.Invoke(); }; 
            main.PropertyChanged = () => { PropertyChanged?.Invoke(); }; 

            main.player = player;
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Main
        {
            internal Action PropertyChanged;
            internal MediaRouter player;

            ActivityMode previewMode = ActivityMode.FullActive;
            public ActivityMode _PreviewMode        { get { return previewMode; } set { previewMode = value; PropertyChanged?.Invoke(); } }

            Bitmap _bitmap;
            [XmlIgnore]
            public Bitmap       SampleFrame         { get { return _bitmap;     } set { _bitmap     = value; player.renderer.SetSampleFrame(value); } }

            bool _allowDrop = true;
            public bool         AllowDrop           { get { return _allowDrop;  } set { _allowDrop  = value; PropertyChanged?.Invoke(); } }

            public bool         AllowFullScreen     { get; set; } = true;
            public bool         AllowTorrents       { get; set; } = true;
            public int          IdleTimeout         { get; set; } = 7000;
            public bool         EmbeddedList        { get; set; } = true;
            public bool         HideCursor          { get; set; } = true;
            [XmlIgnore]
            public Color        ClearBackColor      { get { return player.renderer.ClearColor; } set { player.renderer.ClearColor = value; player.renderer.PresentFrame(null); } }
            [XmlElement("ClearBackColor")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string       ClearBackColor2     { get { return (new ColorConverter()).ConvertToString(ClearBackColor); } set { ClearBackColor = (Color) (new ColorConverter()).ConvertFromString(value); } }
            public int          MessagesDuration    { get { return OSDMessage.DefaultDuration; } set { OSDMessage.DefaultDuration = value; } }
        }
        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Bar
        {
            internal Action PropertyChanged;

            VisibilityMode visibility  = VisibilityMode.OnFullActive;
            [RefreshProperties(RefreshProperties.All)]
            public VisibilityMode _Visibility { get { return visibility; } set { visibility = value; PropertyChanged?.Invoke(); } } 

            bool play = true;
            public bool Play         { get { return play;   } set { play        = value; PropertyChanged?.Invoke(); } }

            bool playlist = true;
            public bool Playlist    { get { return playlist;} set { playlist    = value; PropertyChanged?.Invoke(); } } 

            bool subs = true;
            public bool Subs        { get { return subs;    } set { subs        = value; PropertyChanged?.Invoke(); } } 

            bool mute = true;
            public bool Mute        { get { return mute;    } set { mute        = value; PropertyChanged?.Invoke(); } } 

            bool volume = true;
            public bool Volume      { get { return volume;  } set { volume      = value; PropertyChanged?.Invoke(); } } 

            public bool SeekOnSlide { get; set; } = true;
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
	    public class Keys
        {
            [RefreshProperties(RefreshProperties.All)]
            public bool _Enabled        { get; set; } = true;

		    public int SeekStepFirst    { get; set; } = 5000;
            public int SeekStep         { get; set; } =  500;
            public int SubsDelayStep    { get; set; } =  100;
            public int SubsDelayStep2   { get; set; } = 5000;
            public int SubsPosYStep     { get; set; } =    5;
            public int SubsFontSizeStep { get; set; } =    2;
            public int AudioDelayStep   { get; set; } =   20;
            public int AudioDelayStep2  { get; set; } = 5000;
            public int VolStep          { get; set; } =    5;
	    }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class HookForm
        {
            [RefreshProperties(RefreshProperties.All)]
            public bool _Enabled        { get; set; } = true;

            public bool HookHandle      { get; set; } = false;
            public bool HookKeys        { get; set; } = true;
            public bool AllowResize     { get; set; } = true;
            public bool AutoResize      { get; set; } = true;
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Audio
        {
            private Audio() { }
            MediaRouter player;

            public Audio(MediaRouter player) { this.player = player; }

            [RefreshProperties(RefreshProperties.All)]
            public bool     _Enabled    { get { return player.doAudio; } set { player.doAudio = value; } }
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Subtitles
        {
            private Subtitles() { }
            MediaRenderer renderer;
            MediaRouter player;

            public Subtitles(MediaRouter player) { this.player = player; this.renderer = player.renderer;}

            [RefreshProperties(RefreshProperties.All)]
            public bool     _Enabled    { get { return player.doSubs; } set { player.doSubs = value; } }
            public string[] Languages   { get { string[] ret = new string[player.Languages.Count]; for (int i=0; i<player.Languages.Count; i++) ret[i] = player.Languages[i].LanguageName; return ret; } set { player.Languages.Clear(); foreach (string lang in value.ToList()) player.Languages.Add(Language.Get(lang)); } }

            public DownloadSubsMode DownloadSubs { get { return player.DownloadSubs; } set { player.DownloadSubs = value; } }

            [XmlIgnore]
            public Font     Font        { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Font;          } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Font          = value; renderer.PresentFrame(null); } }
            [XmlIgnore]
            [XmlElement("Font")]
            [Browsable(false)]
            public string   Font2       { get { return (new FontConverter()).ConvertToString(Font); } set { Font = (Font) (new FontConverter()).ConvertFromString(value); } }
            [XmlIgnore]
            public Color    Color       { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].ForeColor;     } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].ForeColor     = value; renderer.PresentFrame(null); } }
            [XmlIgnore]
            [XmlElement("Color")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string   Color2      { get { return (new ColorConverter()).ConvertToString(Color); } set { Color = (Color) (new ColorConverter()).ConvertFromString(value); } }
            [XmlIgnore]
            public Point    Position    { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Position;      } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Position      = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
            [XmlIgnore]
            public bool     OnViewPort  { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].hookViewport;  } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].hookViewport  = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
            [XmlIgnore]
            public bool     RectEnabled { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectEnabled;   } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectEnabled   = value; renderer.HookResized (null, null); } }
            [XmlIgnore]
            public Color    RectColor   { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].BackColor;     } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].BackColor     = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
            [XmlIgnore]
            [XmlElement("RectColor")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string   RectColor2  { get { return (new ColorConverter()).ConvertToString(RectColor);   } set { Color = (Color) (new ColorConverter()).ConvertFromString(value); } }
            [XmlIgnore]
            public Padding  RectPadding { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectPadding;   } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectPadding   = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Video
        {
            private Video() { }
            MediaRouter player;
            public Video(MediaRouter player) { this.player = player;}
                
            public bool         HardwareAcceleration{ get { return player.HWAccel;              } set { player.HWAccel          = value; player.renderer.PresentFrame(null);} }
            public ViewPorts    AspectRatio         { get { return player.ViewPort;             } set { player.ViewPort         = value; player.renderer.HookResized(null,null);} }
            public float        CustomRatio         { get { return player.CustomRatio;          } set { player.CustomRatio      = value; player.renderer.HookResized(null,null);} }
            public int          DecoderThreads      { get { return player.decoder.Threads;      } set { player.decoder.Threads  = value; } }
            public int          QueueMinSize        { get { return player.VIDEO_MIX_QUEUE_SIZE; } set { player.VIDEO_MIX_QUEUE_SIZE = value; } }
            public int          QueueMaxSize        { get { return player.VIDEO_MAX_QUEUE_SIZE; } set { player.VIDEO_MAX_QUEUE_SIZE = value; } }
            public bool         VSync               { get { return player.renderer.VSync;       } set { player.renderer.VSync   = value; } }
        }

        [Serializable]
        [Category("Flyleaf Messages")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Message
        {
            private Message() { }
            MediaRenderer     renderer;

            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public OSDMessage.Type _Selected;
            public Message(MediaRenderer renderer, OSDMessage.Type selected) { this.renderer = renderer; _Selected = selected; }
            public VisibilityMode Visibility { get { return renderer.msgToVis[_Selected]; } set { renderer.msgToVis[_Selected] = value;  renderer.PresentFrame(null); } }
            public OSDSurfaces Surface { get { return StringToEnum(renderer.msgToSurf[_Selected]); }  set { renderer.msgToSurf[_Selected] = EnumToString(value); renderer.PresentFrame(null); } }
        }

        [Serializable]
        [Category("Flyleaf Surfaces")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class SurfacesAll
        {
            private SurfacesAll() { }
            MediaRenderer renderer;
            Surface[]  surfaces;
            public SurfacesAll(MediaRenderer renderer, Surface[] surfaces) { this.renderer = renderer; this.surfaces = surfaces; }
            [XmlIgnore]
            public Font     Font        { get { return surfaces[0].Font;        } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.Font        = value; renderer.HookResized(null,null); } } }
            [XmlIgnore]
            public Color    Color       { get { return surfaces[0].Color;       } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.ForeColor   = value; renderer.HookResized(null,null); } } }
            [XmlIgnore]
            public bool     OnViewPort  { get { return surfaces[0].OnViewPort;  } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.hookViewport= value; renderer.HookResized(null,null);  } } }
            [XmlIgnore]
            public bool     RectEnabled { get { return surfaces[0].RectEnabled; } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.rectEnabled = value; renderer.HookResized(null,null); } } }
            [XmlIgnore]
            public Color    RectColor   { get { return surfaces[0].RectColor;   } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.BackColor   = value; renderer.HookResized(null,null);  } } }
            [XmlIgnore]
            public Padding  RectPadding { get { return surfaces[0].RectPadding; } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.rectPadding = value; renderer.HookResized(null,null); } } }
                
        }

        [Serializable]
        [Category("Flyleaf Surfaces")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Surface
        {
            private Surface() { }
            MediaRenderer renderer;

            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public OSDSurfaces _Selected;
            public Surface(MediaRenderer renderer, OSDSurfaces selected) { this.renderer = renderer; _Selected = selected; }
            [XmlIgnore]
            public Font     Font        { get { return renderer.osd[EnumToString(_Selected)].Font;          } set { renderer.osd[EnumToString(_Selected)].Font          = value; renderer.PresentFrame(null); } }
            [XmlElement("Font")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string   Font2       { get { return (new FontConverter()).ConvertToString(Font); } set { Font = (Font) (new FontConverter()).ConvertFromString(value); } }
            [XmlIgnore]
            public Color    Color       { get { return renderer.osd[EnumToString(_Selected)].ForeColor;     } set { renderer.osd[EnumToString(_Selected)].ForeColor     = value; renderer.PresentFrame(null); } }
            [XmlElement("Color")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string   Color2      { get { return (new ColorConverter()).ConvertToString(Color); } set { Color = (Color) (new ColorConverter()).ConvertFromString(value); } }
            public Point    Position    { get { return renderer.osd[EnumToString(_Selected)].Position;      } set { renderer.osd[EnumToString(_Selected)].Position      = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
            public bool     OnViewPort  { get { return renderer.osd[EnumToString(_Selected)].hookViewport;  } set { renderer.osd[EnumToString(_Selected)].hookViewport  = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
            public bool     RectEnabled { get { return renderer.osd[EnumToString(_Selected)].rectEnabled;   } set { renderer.osd[EnumToString(_Selected)].rectEnabled   = value; renderer.HookResized (null, null); } }
            [XmlIgnore]
            public Color    RectColor   { get { return renderer.osd[EnumToString(_Selected)].BackColor;     } set { renderer.osd[EnumToString(_Selected)].BackColor     = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
            [XmlElement("RectColor")]
            [Browsable(false)]
            [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
            public string   RectColor2  { get { return (new ColorConverter()).ConvertToString(RectColor);   } set { RectColor = (Color) (new ColorConverter()).ConvertFromString(value); } }
            public Padding  RectPadding { get { return renderer.osd[EnumToString(_Selected)].rectPadding;   } set { renderer.osd[EnumToString(_Selected)].rectPadding   = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
        }

        public class FlyleafTypeConverter : ExpandableObjectConverter
        {
            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
            {
                string visOrEnable = "_Enabled";
                PropertyDescriptorCollection props = base.GetProperties(context, value, attributes);
                PropertyDescriptor pVis =  props.Find(visOrEnable, true);
                if ( pVis == null ) { visOrEnable = "_Visibility"; pVis = props.Find(visOrEnable, true); }

                if ( pVis != null )
                {
                    if ( (visOrEnable == "_Visibility" && (VisibilityMode)props[visOrEnable].GetValue(value) == VisibilityMode.Never) || (visOrEnable == "_Enabled"  && !(bool)props[visOrEnable].GetValue(value)) )
                    {
                        PropertyDescriptorCollection newProps = new PropertyDescriptorCollection(null);
                        newProps.Add(props[visOrEnable]);

                        return newProps;
                    }
                }

                return props;
            }

            public override bool    CanConvertFrom  (ITypeDescriptorContext context, Type sourceType)       { return base.CanConvertFrom(context, sourceType); }
            public override object  ConvertFrom     (ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value) { return base.ConvertFrom(context, culture, value); }
            public override bool    CanConvertTo    (ITypeDescriptorContext context, Type destinationType)  { return base.CanConvertTo(context, destinationType); }
            public override object  ConvertTo       (ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
            {
                if (value is Settings.Bar)
                    return ((Settings.Bar)value)._Visibility.ToString();
                else if (value is Settings.Keys)
                    return ((Settings.Keys)value)._Enabled.ToString();
                else if (value is Settings.HookForm)
                    return ((Settings.HookForm)value)._Enabled ? "-= Single =-" : "-= Multi =-";
                else if (value is Settings)
                    return "-= " + ((Settings.Main)value)._PreviewMode.ToString() + " =-";

                return "";
            }
        }

        public static void SaveSettings(Settings config, string filename = "SettingsUser.xml")
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));
                xmlSerializer.Serialize(fs, config);
            }
        }
        public static SettingsLoad LoadSettings(string filename = "SettingsUser.xml")
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(SettingsLoad));
                return (SettingsLoad) xmlSerializer.Deserialize(fs);
            }
        }

        public static void ParseSettings(SettingsLoad load, Settings config)
        {
            config.bar      = load.bar;
            config.keys     = load.keys;
            config.hookForm = load.hookForm;

            config.audio._Enabled           = load.audio._Enabled;

            config.video.AspectRatio        = load.video.AspectRatio;
            config.video.CustomRatio        = load.video.CustomRatio;
            config.video.HardwareAcceleration = load.video.HardwareAcceleration;
            config.video.VSync              = load.video.VSync;
            config.video.DecoderThreads     = load.video.DecoderThreads;
            config.video.QueueMinSize       = load.video.QueueMinSize;
            config.video.QueueMaxSize       = load.video.QueueMaxSize;

            config.main.AllowDrop           = load.main.AllowDrop;
            config.main.AllowFullScreen     = load.main.AllowFullScreen;
            config.main.AllowTorrents       = load.main.AllowTorrents;
            config.main.ClearBackColor      = load.main.ClearBackColor;
            config.main.EmbeddedList        = load.main.EmbeddedList;
            config.main.HideCursor          = load.main.HideCursor;
            config.main.IdleTimeout         = load.main.IdleTimeout;
            config.main.MessagesDuration    = load.main.MessagesDuration;

            config.subtitles._Enabled       = load.subtitles._Enabled;
            config.subtitles.Languages      = load.subtitles.Languages;
            config.subtitles.DownloadSubs   = load.subtitles.DownloadSubs;

            for (int i=0; i<load.messages.Length; i++)
            {
                config.messages[i].Visibility = load.messages[i].Visibility;
                config.messages[i].Surface    = load.messages[i].Surface;
            }
            for (int i=0; i<load.surfaces.Length; i++)
            {
                config.surfaces[i].Color2       = load.surfaces[i].Color2;
                config.surfaces[i].Font2        = load.surfaces[i].Font2;
                config.surfaces[i].OnViewPort   = load.surfaces[i].OnViewPort;
                config.surfaces[i].Position     = load.surfaces[i].Position;
                config.surfaces[i].RectColor2   = load.surfaces[i].RectColor2;
                config.surfaces[i].RectEnabled  = load.surfaces[i].RectEnabled;
                config.surfaces[i].RectPadding  = load.surfaces[i].RectPadding;
            }
                
        }
    }
}