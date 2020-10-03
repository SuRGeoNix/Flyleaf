using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace SuRGeoNix.Flyleaf.Controls
{
    public partial class FlyleafBar : UserControl
    {
        class Col { public Col(SizeType s1, float w1) {s = s1; w = w1; } public SizeType s; public float w;}

        List<Col> save = new List<Col>();
        Pen barBorderColor = new Pen(Color.FromArgb(25, 30, 44), 2);

        public FlyleafBar()
        {
            InitializeComponent();

            foreach (ColumnStyle cs in tblBar.ColumnStyles)
                save.Add(new Col(cs.SizeType, cs.Width));
        }

        public void DisableColumn(int c)    { if (tblBar.ColumnStyles[c].Width == 0) return; tblBar.ColumnStyles[c].SizeType = SizeType.Absolute; tblBar.ColumnStyles[c].Width = 0; }
        public void EnableColumn(int c)     { if (tblBar.ColumnStyles[c].Width == save[c].w) return; tblBar.ColumnStyles[c].SizeType = save[c].s; tblBar.ColumnStyles[c].Width = save[c].w; }
        public bool IsEnabled(int c)        { if (tblBar.ColumnStyles[c].Width == 0) return false; else return true; }

        private void tblBar_CellPaint(object sender, TableLayoutCellPaintEventArgs e)
        {
            if (e.Column == 1)
            {
                e.Graphics.DrawRectangle(barBorderColor, new Rectangle(e.CellBounds.Left + seekBar.Margin.Left - 10, 1 + e.CellBounds.Top + btnPlay.Margin.Top, e.CellBounds.Width - (seekBar.Margin.Left + seekBar.Margin.Right) + 20, e.CellBounds.Height - btnPlay.Margin.Top - btnPlay.Margin.Bottom - 2));
            }
            else if (e.Column == tblBar.ColumnStyles.Count - 1)
            {
                var cell2 = tblBar.ColumnStyles[tblBar.ColumnStyles.Count - 2];
                e.Graphics.DrawRectangle(barBorderColor, new Rectangle(e.CellBounds.Left - (int)cell2.Width + 3, 1 + e.CellBounds.Top + btnPlay.Margin.Top, e.CellBounds.Width + (int)cell2.Width - 7, e.CellBounds.Height - btnPlay.Margin.Top - btnPlay.Margin.Bottom - 2));
            } 
        }
    }
}
