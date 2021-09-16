using System;
using System.ComponentModel;
using System.Windows.Forms;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls
{
    public partial class Flyleaf : UserControl
    {
        public Player Player { get; internal set; }

        //bool isDesignMode;

        public Flyleaf()
        {
            BackColor   = System.Drawing.Color.Black;
            //isDesignMode= LicenseManager.UsageMode == LicenseUsageMode.Designtime;
        }

        protected override void OnPaintBackground(PaintEventArgs pe) { if (Player != null && Player.renderer != null) Player?.renderer?.Present(); else base.OnPaintBackground(pe); }
        protected override void OnPaint(PaintEventArgs pe) { Player?.renderer?.Present(); }
    }
}
