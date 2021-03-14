using System;
using System.Drawing;
using System.Windows.Forms;

using SuRGeoNix.Flyleaf.Controls;

namespace SuRGeoNix.FlyleafMultiPlayer
{
    public partial class FrmMain : Form
    {
        public FrmMain()
        {
            InitializeComponent();

            flyLeaf1.MouseMove  += flyLeaf_GotFocus;
            flyLeaf2.MouseMove  += flyLeaf_GotFocus;
            flyLeaf3.MouseMove  += flyLeaf_GotFocus;
            flyLeaf4.MouseMove  += flyLeaf_GotFocus;

            flyLeaf1.GotFocus   += flyLeaf_GotFocus;
            flyLeaf2.GotFocus   += flyLeaf_GotFocus;
            flyLeaf3.GotFocus   += flyLeaf_GotFocus;
            flyLeaf4.GotFocus   += flyLeaf_GotFocus;

            flyLeaf1.LostFocus  += FlyLeaf_LostFocus;
            flyLeaf2.LostFocus  += FlyLeaf_LostFocus;
            flyLeaf3.LostFocus  += FlyLeaf_LostFocus;
            flyLeaf4.LostFocus  += FlyLeaf_LostFocus;

            flyLeaf2.MouseLeave += FlyLeaf_LostFocus;
            flyLeaf1.MouseLeave += FlyLeaf_LostFocus;
            flyLeaf3.MouseLeave += FlyLeaf_LostFocus;
            flyLeaf4.MouseLeave += FlyLeaf_LostFocus;
        }

        int activeRow = -1;
        int activeColumn = -1;
        Pen pen = new Pen(Color.FromArgb(144, 40, 231), 5);
        private void tableLayoutPanel1_CellPaint(object sender, TableLayoutCellPaintEventArgs e)
        {
            if (activeRow == e.Row && activeColumn == e.Column)
                e.Graphics.DrawRectangle(pen, e.CellBounds.X , e.CellBounds.Y , e.CellBounds.Width, e.CellBounds.Height);
        }

        private void flyLeaf_GotFocus(object sender, EventArgs e)
        {
            if (sender.GetType() == typeof(FlyleafPlayer))
            {
                FlyleafPlayer flyleaf = ((FlyleafPlayer)sender);
                
                switch (flyleaf.Name)
                {
                    case "flyLeaf1":
                        if (activeRow == 0 && activeColumn == 0) return;
                        activeRow = 0;
                        activeColumn = 0;
                        break;
                    case "flyLeaf2":
                        if (activeRow == 0 && activeColumn == 1) return;
                        activeRow = 0;
                        activeColumn = 1;
                        break;
                    case "flyLeaf3":
                        if (activeRow == 1 && activeColumn == 0) return;
                        activeRow = 1;
                        activeColumn = 0;
                        break;
                    case "flyLeaf4":
                        if (activeRow == 1 && activeColumn == 1) return;
                        activeRow = 1;
                        activeColumn = 1;
                        break;
                }

                tableLayoutPanel1.Refresh();
                flyleaf.Focus();
            }
        }
        private void FlyLeaf_LostFocus(object sender, EventArgs e)
        {
            activeRow = -1;
            activeColumn = -1;
            tableLayoutPanel1.Refresh();
        }
    }
}
