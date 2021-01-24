using System.Collections.Generic;
using System.Windows.Forms;

using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.Mathematics.Interop;

using static SuRGeoNix.Flyleaf.OSDMessage;

using WColor    = System.Drawing.Color;
using WPoint    = System.Drawing.Point;
using WFont     = System.Drawing.Font;
using WFontStyle= System.Drawing.FontStyle;

namespace SuRGeoNix.Flyleaf
{
    public class OSDSurface
    {
        MediaRenderer           renderer;
        public string           name;
        public string           text;
        string                  lastText;
        
        public TextFormat       format;
        public TextLayout       layout;

        List<SolidColorBrush>   brushes         = new List<SolidColorBrush>();
        public List<OSDMessage> msgs            = new List<OSDMessage>();

        public bool             requiresUpdate;
        public bool             hookViewport    = true;
        public bool             rectEnabled;
        public Alignment        align;
        public RawVector2       pos;
        public RectangleF       rect;
        public Padding          rectPadding;
        public Color            color;
        public Color            rectColor;
        public enum Alignment
        {
            TOPLEFT,
            TOPCENTER,
            TOPRIGHT,
            CENTERCENTER,
            BOTTOMRIGHT,
            BOTTOMCENTER,
            BOTTOMLEFT
        }

        public WPoint   Position    { get { return new WPoint((int)pos.X, (int)pos.Y); } set { pos.X = value.X; pos.Y = value.Y; } }
        public bool     OnViewPort  { get { return hookViewport;    } set { hookViewport = value; requiresUpdate = true; } }
        public WColor   BackColor   { get { return WColor.FromArgb(rectColor.A, rectColor.R, rectColor.G, rectColor.B); } set { rectColor.R = value.R; rectColor.G = value.G; rectColor.B = value.B; rectColor.A = value.A; } }
        public WColor   ForeColor   { get { return WColor.FromArgb(color.A, color.R, color.G, color.B);                 } set { color.R = value.R; color.G = value.G; color.B = value.B; color.A = value.A; } }
        public float    FontSize    { get { return format.FontSize; } set { format = new TextFormat(renderer.factoryWrite, format.FontFamilyName, format.FontWeight, format.FontStyle, value); SetAlignments(align); requiresUpdate = true; } }
        public WFont    Font { 
            get
            {
                WFontStyle style;

                if (format.FontWeight == FontWeight.Normal)
                {
                    style = format.FontStyle == FontStyle.Italic ? WFontStyle.Italic : WFontStyle.Regular;
                }
                else
                {
                    style = format.FontStyle == FontStyle.Italic ? WFontStyle.Italic | WFontStyle.Bold : WFontStyle.Bold;
                }

                return new WFont(format.FontFamilyName, format.FontSize, style);
            } 

            set
            {
                FontWeight weight;
                FontStyle style;

                if (value.Style == (WFontStyle.Bold | WFontStyle.Italic))
                {
                    weight  = FontWeight.Bold;
                    style   = FontStyle.Italic;
                }
                else
                {
                    style   = value.Style == WFontStyle.Italic? FontStyle.Italic : FontStyle.Normal;
                    weight  = value.Style == WFontStyle.Bold  ? FontWeight.Bold  : FontWeight.Normal;
                }
                
                format  = new TextFormat(renderer.factoryWrite, value.Name, weight, style, value.Size); 
                SetAlignments(align);
                requiresUpdate = true;
            }
        }

        public OSDSurface(MediaRenderer renderer, Alignment align, WPoint pos, string font, float size, WFontStyle style = WFontStyle.Regular, FontWeight weight = FontWeight.Normal, WColor? color = null, bool rectEnabled = true, WColor? backColor = null, Padding? padding = null) 
        {
            this.renderer   = renderer;
            
            format          = new TextFormat(renderer.factoryWrite, font, weight, style == WFontStyle.Italic ? FontStyle.Italic : FontStyle.Normal, size);
            this.pos        = new RawVector2(pos.X, pos.Y);
            SetAlignments(align);

            this.rectPadding= padding == null ? new Padding() : (Padding)padding;
            this.rectEnabled= rectEnabled;
            this.color      = color == null     ? Color.White : new Color(((WColor)color).R,      ((WColor)color).G,    ((WColor)color).B,    ((WColor)color).A);
            this.rectColor  = backColor == null ? Color.Black : new Color(((WColor)backColor).R,  ((WColor)backColor).G,((WColor)backColor).B,((WColor)backColor).A);
        }
        //public void Init() { renderer.HookControl.Resize += HookResized; }
        //private void HookResized(object sender, EventArgs e) { requiresUpdate = true; }
        public void Dispose()
        {
            if (brushes.Count > 0)
            {
                foreach (var brush in brushes)
                { 
                    SolidColorBrush tmp = brush;
                    Utilities.Dispose(ref tmp);
                }
                brushes.Clear();
            }

            Utilities.Dispose(ref layout);
            Utilities.Dispose(ref format);

        }

        private void SetAlignments(Alignment align)
        {
            this.align = align;

            switch (align)
            {
                case Alignment.TOPLEFT:
                    format.TextAlignment        = TextAlignment.Leading;
                    format.ParagraphAlignment   = ParagraphAlignment.Near;
                    break;

                case Alignment.TOPCENTER:
                    format.TextAlignment        = TextAlignment.Center;
                    format.ParagraphAlignment   = ParagraphAlignment.Near;
                    break;

                case Alignment.TOPRIGHT:
                    format.TextAlignment        = TextAlignment.Trailing;
                    format.ParagraphAlignment   = ParagraphAlignment.Near;
                    break;

                case Alignment.CENTERCENTER:
                    format.TextAlignment        = TextAlignment.Center;
                    format.ParagraphAlignment   = ParagraphAlignment.Center;
                    break;

                case Alignment.BOTTOMLEFT:
                    format.TextAlignment        = TextAlignment.Leading;
                    format.ParagraphAlignment   = ParagraphAlignment.Far;
                    break;

                case Alignment.BOTTOMCENTER:
                    format.TextAlignment        = TextAlignment.Center;
                    format.ParagraphAlignment   = ParagraphAlignment.Far;
                    break;

                case Alignment.BOTTOMRIGHT:
                    format.TextAlignment        = TextAlignment.Trailing;
                    format.ParagraphAlignment   = ParagraphAlignment.Far;
                    break;
            }
        }
        private void CreateRectangle()
        {
            if (rectEnabled)
            {
                var width   = hookViewport ? renderer.GetViewport.Width : renderer.HookControl.Width;
                var height  = hookViewport ? renderer.GetViewport.Height: renderer.HookControl.Height;

                if (align == Alignment.TOPLEFT)
                    rect = new RectangleF(pos.X, pos.Y, layout.Metrics.Width, layout.Metrics.Height);
                else if (align == Alignment.TOPRIGHT)
                    rect = new RectangleF((width - layout.Metrics.Width) + pos.X, pos.Y, layout.Metrics.Width, layout.Metrics.Height);
                else if (align == Alignment.BOTTOMLEFT)
                    rect = new RectangleF(pos.X, (height - layout.Metrics.Height) + pos.Y, layout.Metrics.Width, layout.Metrics.Height);
                else if (align == Alignment.BOTTOMRIGHT)
                    rect = new RectangleF((width - layout.Metrics.Width) + pos.X, (height - layout.Metrics.Height) + pos.Y, layout.Metrics.Width, layout.Metrics.Height);
                else if (align == Alignment.BOTTOMCENTER)
                    rect = new RectangleF((width / 2 - layout.Metrics.Width / 2) + pos.X, (height - layout.Metrics.Height) + pos.Y, layout.Metrics.Width, layout.Metrics.Height);
                // TODO BOTTOMLEFT

                rect.X      -= rectPadding.Left;
                rect.Y      -= rectPadding.Top;
                rect.Width  += rectPadding.Right    + rectPadding.Left;
                rect.Height += rectPadding.Top      + rectPadding.Bottom;

                rect.Y -= renderer.msgToSurf[OSDMessage.Type.Subtitles] == name ? renderer.SubsPosition : 0;

                if (hookViewport)
                {
                    rect.X += renderer.GetViewport.X;
                    rect.Y += renderer.GetViewport.Y;
                }

            }
        }

        public void DrawMessages()
        {
            if (msgs.Count == 0) return;

            lastText    = text;
            text        = "";

            foreach (OSDMessage msg in msgs)
                text += msg.msg + "\r\n";
            text = text.Trim();

            if (text == "") return;
            if (lastText == text && !requiresUpdate) { msgs.Clear(); Draw(); return; }

            Utilities.Dispose(ref layout);
            if (brushes.Count > 0)
            {
                foreach (var brush in brushes)
                { 
                    SolidColorBrush tmp = brush;
                    Utilities.Dispose(ref tmp);
                }
                brushes = new List<SolidColorBrush>();
            }

            layout = new TextLayout(renderer.factoryWrite, text, format, hookViewport ? renderer.GetViewport.Width : renderer.HookControl.Width, hookViewport ? renderer.GetViewport.Height : renderer.HookControl.Height);

            // Fix Styling
            int curPos = 0;
            foreach (OSDMessage msg in msgs)
            {
                if (msg.styles == null) { curPos += msg.msg.Length + 2; continue; }

                foreach (SubStyle style in msg.styles)
                {
                    switch (style.style)
                    {
                        case SubStyles.BOLD:
                            layout.SetFontWeight(FontWeight.Bold, new TextRange(curPos + style.from, style.len));
                            break;

                        case SubStyles.ITALIC:
                            layout.SetFontStyle(FontStyle.Italic, new TextRange(curPos + style.from, style.len));
                            break;

                        case SubStyles.UNDERLINE:
                            layout.SetUnderline(true, new TextRange(curPos + style.from, style.len));
                            break;

                        case SubStyles.STRIKEOUT:
                            layout.SetStrikethrough(true, new TextRange(curPos + style.from, style.len));
                            break;

                        case SubStyles.COLOR:
                            SolidColorBrush brush2dtmp = new SolidColorBrush(renderer.rtv2d, (Color) style.value);
                            brushes.Add(brush2dtmp);
                            layout.SetDrawingEffect(brush2dtmp.NativePointer, new TextRange(curPos + style.from, style.len));

                            break;
                    }
                }

                curPos += msg.msg.Length + 2;
            }

            CreateRectangle();

            msgs.Clear();
            Draw();

            requiresUpdate = false;
        }
        public void DrawText(string text)
        {
            if (lastText == text && !requiresUpdate) { Draw(); return; }
            
            lastText    = this.text;
            this.text   = text;

            Utilities.Dispose(ref layout);
            layout = new TextLayout(renderer.factoryWrite, text, format, hookViewport ? renderer.GetViewport.Width : renderer.HookControl.Width, hookViewport ? renderer.GetViewport.Height : renderer.HookControl.Height);

            CreateRectangle();
            
            requiresUpdate = false;
            Draw();
        }
        public void Draw()
        {
            if (rectEnabled)
            {
                renderer.brush2d.Color = rectColor;
                renderer.rtv2d.FillRectangle(rect, renderer.brush2d);
            }

            float x = hookViewport ? pos.X + renderer.GetViewport.X : pos.X;
            float y = hookViewport ? pos.Y + renderer.GetViewport.Y : pos.Y;
            renderer.brush2d.Color = color;

            // Outline: For better performance maybe should create new fonts with outline
            if (renderer.msgToSurf[OSDMessage.Type.Subtitles] == name)
            {
                y -= renderer.SubsPosition;
                renderer.rtv2d.DrawTextLayout(new RawVector2(x, y), layout, renderer.brush2d);
                layout.Draw(renderer.outlineRenderer, x, y);
            }
            else
                renderer.rtv2d.DrawTextLayout(new RawVector2(x, y), layout, renderer.brush2d);

                
        }
    }
}