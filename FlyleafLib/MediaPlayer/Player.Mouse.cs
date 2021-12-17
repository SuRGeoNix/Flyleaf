using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FlyleafLib.MediaPlayer
{
    partial class Player
    {
        /* Player Mouse Bindings
         * 
         * Config.Player.MouseBindigns.Enabled
         * Config.Player.MouseBindigns.OpenOnDragAndDrop
         * Config.Player.MouseBindigns.PanMoveOnDragAndCtrl
         * Config.Player.MouseBindigns.PanZoomOnWheelAndCtrl
         * Config.Player.MouseBindigns.ToggleFullScreenOnDoubleClick
         * 
         * ActivityRefresh          (Control_MouseDown, Control_MouseMove)
         * Drag & Drop              (Control_DragDrop,  Control_DragEnter)
         * Pan Zoom In / Zoom Out   (Control_MouseWheel)
         * Pan Move                 (Control_MouseDown, Control_MouseUp)
         * ToggleFullScreen         (Control_DoubleClick)
         */

        int panClickX = -1, panClickY = -1, panPrevX = -1, panPrevY = -1;

        private void Control_DragDrop(object sender, System.Windows.Forms.DragEventArgs e)
        {
            if (!Config.Player.MouseBindigns.OpenOnDragAndDrop)
                return;

            if (e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop))
            {
                string filename = ((string[])e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop, false))[0];
                OpenAsync(filename);
            }
            else if (e.Data.GetDataPresent(System.Windows.Forms.DataFormats.Text))
            {
                string text = e.Data.GetData(System.Windows.Forms.DataFormats.Text, false).ToString();
                if (text.Length > 0) OpenAsync(text);
            }
        }
        private void Control_DragEnter(object sender, System.Windows.Forms.DragEventArgs e)
        {
            if (!Config.Player.MouseBindigns.OpenOnDragAndDrop)
                return;

            e.Effect = System.Windows.Forms.DragDropEffects.All;
        }
        private void Control_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (Config.Player.ActivityMode)
                Activity.MouseTimestamp = DateTime.UtcNow.Ticks;

            if (!Config.Player.MouseBindigns.PanMoveOnDragAndCtrl || !CanPlay || e.Button != System.Windows.Forms.MouseButtons.Left)
                return;

            if (panClickX == -1)
            {
                panClickX = e.X;
                panClickY = e.Y;
                panPrevX = PanXOffset;
                panPrevY = PanYOffset;
            }
        }
        private void Control_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            panClickX = -1; panClickY = -1;
        }
        private void Control_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (Config.Player.ActivityMode)
                Activity.MouseTimestamp = DateTime.UtcNow.Ticks;

            if (Config.Player.MouseBindigns.PanMoveOnDragAndCtrl && panClickX != -1 && e.Button == System.Windows.Forms.MouseButtons.Left && 
                (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                PanXOffset = panPrevX + e.X - panClickX;
                PanYOffset = panPrevY + e.Y - panClickY;
            }
        }
        private void Control_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (!Config.Player.MouseBindigns.PanZoomOnWheelAndCtrl || e.Delta == 0 || (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)))
                return;

            Zoom += e.Delta > 0 ? Config.Player.ZoomOffset : -Config.Player.ZoomOffset;
        }
        private void Control_DoubleClick(object sender, EventArgs e)
        {
            if (!Config.Player.MouseBindigns.ToggleFullScreenOnDoubleClick)
                return;

            ToggleFullScreen();
        }
    }

    public class MouseConfig : NotifyPropertyChanged
    {
        public bool Enabled
        {
            get => OpenOnDragAndDrop || HideCursorOnFullScreenIdle || PanMoveOnDragAndCtrl || PanZoomOnWheelAndCtrl || ToggleFullScreenOnDoubleClick; 
            set
            {
                OpenOnDragAndDrop = value;
                HideCursorOnFullScreenIdle = value;
                PanMoveOnDragAndCtrl = value;
                PanZoomOnWheelAndCtrl = value;
                ToggleFullScreenOnDoubleClick = value;

                Raise();
            }
        }

        public bool OpenOnDragAndDrop               { get => _OpenOnDragAndDrop;            set => Set(ref _OpenOnDragAndDrop, value); }
        bool _OpenOnDragAndDrop = true;
        public bool HideCursorOnFullScreenIdle      { get => _HideCursorOnFullScreenIdle;   set => Set(ref _HideCursorOnFullScreenIdle, value); }
        bool _HideCursorOnFullScreenIdle = true;
        public bool PanMoveOnDragAndCtrl            { get => _PanMoveOnDragAndCtrl;         set => Set(ref _PanMoveOnDragAndCtrl, value); }
        bool _PanMoveOnDragAndCtrl = true;
        public bool PanZoomOnWheelAndCtrl           { get => _PanZoomOnWheelAndCtrl;        set => Set(ref _PanZoomOnWheelAndCtrl, value); }
        bool _PanZoomOnWheelAndCtrl = true;
        public bool ToggleFullScreenOnDoubleClick   { get => _ToggleFullScreenOnDoubleClick;set => Set(ref _ToggleFullScreenOnDoubleClick, value); }
        bool _ToggleFullScreenOnDoubleClick = true;
    }
}
