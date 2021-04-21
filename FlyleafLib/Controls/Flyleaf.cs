using System;
using System.Windows.Forms;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls
{
    public partial class Flyleaf : UserControl
    {
        public Player Player { get; internal set; }
        public Flyleaf() { BackColor = System.Drawing.Color.Black; }
        protected override void OnPaintBackground(PaintEventArgs pe) { Player?.renderer?.PresentFrame(); }
        protected override void OnPaint(PaintEventArgs pe) { Player?.renderer?.PresentFrame(); } // renderer?.PresentFrame(); }
    }
}
