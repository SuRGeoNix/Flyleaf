using System;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;

using static SuRGeoNix.Flyleaf.MediaRouter;

namespace SuRGeoNix.Flyleaf
{
    [Serializable]
    [Category("Flyleaf UI")]
    [TypeConverter(typeof(FlyleafTypeConverter))]
    public class Settings
    {
        MediaRouter         player;
        public Action       PropertyChanged;

        public Bar          bar         = new Bar();
        public Keys         keys        = new Keys();
        public HookForm     hookForm    = new HookForm();

        public Audio        audio;
        public Subtitles    subtitles;
        public Video        video;
            
        public Surfaces[]   surfaces;
        public SurfacesAll  surfacesAll;
        public Messages[]   messages;

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

        public Settings(MediaRouter player)
        {
            this.player = player;
            var t1 = Enum.GetValues(typeof(OSDSurfaces));
            surfaces = new Surfaces[t1.Length];
            for (int i=0; i<t1.Length; i++)
            {
                surfaces[i] = new Surfaces(player.renderer, (OSDSurfaces)t1.GetValue(i));
            }

            t1 = Enum.GetValues(typeof(OSDMessage.Type));
            messages = new Messages[t1.Length];
            for (int i=0; i<t1.Length; i++)
            {
                messages[i] = new Messages(player.renderer, (OSDMessage.Type)t1.GetValue(i));
            }

            surfacesAll = new SurfacesAll(player.renderer, surfaces);

            audio       = new Audio(player);
            subtitles   = new Subtitles(player);
            video       = new Video(player);

            bar. PropertyChanged = () => { PropertyChanged?.Invoke(); }; 
        }

        ActivityMode previewMode = ActivityMode.FullActive;
        public ActivityMode _PreviewMode        { get { return previewMode; } set { previewMode = value; PropertyChanged?.Invoke(); } }

        Bitmap _bitmap;
        public Bitmap       SampleFrame         { get { return _bitmap;     } set { _bitmap     = value; player.renderer.SetSampleFrame(value); } }

        bool _allowDrop = true;
        public bool         AllowDrop           { get { return _allowDrop;  } set { _allowDrop  = value; PropertyChanged?.Invoke(); } }

        public bool         AllowFullScreen     { get; set; } = true;
        public bool         AllowTorrents       { get; set; } = true;
        public int          IdleTimeout         { get; set; } = 7000;
        public bool         EmbeddedList        { get; set; } = true;
        public bool         HideCursor          { get; set; } = true;
        public Color        ClearBackColor      { get { return player.renderer.ClearColor; } set { player.renderer.ClearColor = value; player.renderer.PresentFrame(null); } }
        public int          MessagesDuration    { get { return OSDMessage.DefaultDuration; } set { OSDMessage.DefaultDuration = value; } }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Bar
        {
            public Action PropertyChanged;

            VisibilityMode visibility  = VisibilityMode.OnFullActive;
            [RefreshProperties(RefreshProperties.All)]
            public VisibilityMode _Visibility { get { return visibility; } set { visibility = value; PropertyChanged?.Invoke(); } } 

            bool play = true;
            public bool Play         { get { return play; } set { play = value; PropertyChanged?.Invoke(); } }

            bool playlist = true;
            public bool Playlist    { get { return playlist; } set { playlist = value; PropertyChanged?.Invoke(); } } 

            bool mute = true;
            public bool Mute         { get { return mute; } set { mute = value; PropertyChanged?.Invoke(); } } 

            bool volume = true;
            public bool Volume       { get { return volume; } set { volume = value; PropertyChanged?.Invoke(); } } 

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
            public bool AllowResize      { get; set; } = true;
            public bool AutoResize      { get; set; } = true;
                
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Audio
        {
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
            MediaRenderer renderer;
            MediaRouter player;

            public Subtitles(MediaRouter player) { this.player = player; this.renderer = player.renderer;}

            [RefreshProperties(RefreshProperties.All)]
            public bool     _Enabled    { get { return player.doSubs; } set { player.doSubs = value; } }
            public Font     Font        { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Font;          } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Font          = value; renderer.PresentFrame(null); } }
            public Color    Color       { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].ForeColor;     } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].ForeColor     = value; renderer.PresentFrame(null); } }
            public Point    Position    { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Position;      } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].Position      = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
            public bool     OnViewPort  { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].hookViewport;  } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].hookViewport  = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
            public bool     RectEnabled { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectEnabled;   } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectEnabled   = value; renderer.HookResized (null, null); } }
            public Color    RectColor   { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].BackColor;     } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].BackColor     = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
            public Padding  RectPadding { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectPadding;   } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].rectPadding   = value; renderer.PresentFrame(null); renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].requiresUpdate = true; renderer.PresentFrame(null); } }
        }

        [Serializable]
        [Category("Flyleaf UI")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Video
        {
            MediaRouter player;
            public Video(MediaRouter player) { this.player = player;}
                
            public bool         HardwareAcceleration{ get { return player.HWAccel;          } set { player.HWAccel          = value; player.renderer.PresentFrame(null);} }
            public ViewPorts    AspectRatio         { get { return player.ViewPort;         } set { player.ViewPort         = value; player.renderer.HookResized(null,null);} }
            public float        CustomRatio         { get { return player.CustomRatio;      } set { player.CustomRatio      = value; player.renderer.HookResized(null,null);} }
            public int          DecoderThreads      { get { return player.decoder.Threads;  } set { player.decoder.Threads  = value; } }
            public bool         VSync               { get { return player.renderer.VSync;   } set { player.renderer.VSync   = value; } }
        }

        [Serializable]
        [Category("Flyleaf Messages")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Messages
        {
            MediaRenderer     renderer;
            OSDMessage.Type _Selected;
            public Messages(MediaRenderer renderer, OSDMessage.Type selected) { this.renderer = renderer; _Selected = selected; }
                
            public VisibilityMode Visibility { get { return renderer.msgToVis[_Selected]; } set { renderer.msgToVis[_Selected] = value;  renderer.PresentFrame(null); } }
            public OSDSurfaces Surface { get { return StringToEnum(renderer.msgToSurf[_Selected]); }  set { renderer.msgToSurf[_Selected] = EnumToString(value); renderer.PresentFrame(null); } }
        }

        [Serializable]
        [Category("Flyleaf Surfaces")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class SurfacesAll
        {
            MediaRenderer renderer;
            Surfaces[]  surfaces;
            public SurfacesAll(MediaRenderer renderer, Surfaces[] surfaces) { this.renderer = renderer; this.surfaces = surfaces; }

            public Font     Font        { get { return surfaces[0].Font;        } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.Font        = value; renderer.HookResized(null,null); } } }
            public Color    Color       { get { return surfaces[0].Color;       } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.ForeColor   = value; renderer.HookResized(null,null); } } }
            public bool     OnViewPort  { get { return surfaces[0].OnViewPort;  } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.hookViewport= value; renderer.HookResized(null,null);  } } }
            public bool     RectEnabled { get { return surfaces[0].RectEnabled; } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.rectEnabled = value; renderer.HookResized(null,null); } } }
            public Color    RectColor   { get { return surfaces[0].RectColor;   } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.BackColor   = value; renderer.HookResized(null,null);  } } }
            public Padding  RectPadding { get { return surfaces[0].RectPadding; } set { foreach (var osdsurf in renderer.osd) if (osdsurf.Key != "bc")  { osdsurf.Value.rectPadding = value; renderer.HookResized(null,null); } } }
                
        }

        [Serializable]
        [Category("Flyleaf Surfaces")]
        [TypeConverter(typeof(FlyleafTypeConverter))]
        public class Surfaces
        {
            MediaRenderer renderer;
            OSDSurfaces _Selected;
            public Surfaces(MediaRenderer renderer, OSDSurfaces selected) { this.renderer = renderer; _Selected = selected; }

            public Font     Font        { get { return renderer.osd[EnumToString(_Selected)].Font;          } set { renderer.osd[EnumToString(_Selected)].Font          = value; renderer.PresentFrame(null); } }
            public Color    Color       { get { return renderer.osd[EnumToString(_Selected)].ForeColor;     } set { renderer.osd[EnumToString(_Selected)].ForeColor     = value; renderer.PresentFrame(null); } }
            public Point    Position    { get { return renderer.osd[EnumToString(_Selected)].Position;      } set { renderer.osd[EnumToString(_Selected)].Position      = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
            public bool     OnViewPort  { get { return renderer.osd[EnumToString(_Selected)].hookViewport;  } set { renderer.osd[EnumToString(_Selected)].hookViewport  = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
            public bool     RectEnabled { get { return renderer.osd[EnumToString(_Selected)].rectEnabled;   } set { renderer.osd[EnumToString(_Selected)].rectEnabled   = value; renderer.HookResized (null, null); } }
            public Color    RectColor   { get { return renderer.osd[EnumToString(_Selected)].BackColor;     } set { renderer.osd[EnumToString(_Selected)].BackColor     = value; renderer.PresentFrame(null); renderer.osd[EnumToString(_Selected)].requiresUpdate = true; renderer.PresentFrame(null); } }
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
                    return "-= " + ((Settings)value)._PreviewMode.ToString() + " =-";

                return "";
            }
        }

    }
}