using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WinForms
{
    public partial class FlyleafHost : UserControl, INotifyPropertyChanged
    {
        Player _Player;
        public Player       Player          {
            get => _Player; 
            set {
                if (_Player == value)
                    return; 

                Player oldPlayer = _Player; 
                _Player = value; 
                SetPlayer(oldPlayer); 
                Raise(nameof(Player));
                } 
            }

        bool _IsFullScreen;
        public bool         IsFullScreen    { 
            get => _IsFullScreen; 
            set
                {
                if (_IsFullScreen == value)
                    return;

                if (value)
                    FullScreen();
                else
                    NormalScreen();

                if (Player != null)
                    Player.IsFullScreen = _IsFullScreen;
                } 
            }

        bool _ToggleFullScreenOnDoubleClick = true;
        public bool         ToggleFullScreenOnDoubleClick 
                                            { get => _ToggleFullScreenOnDoubleClick; set => Set(ref _ToggleFullScreenOnDoubleClick, value); }

        public int          UniqueId        { get; private set; } = -1;

        bool _KeyBindings = true;
        public bool         KeyBindings     { get => _KeyBindings; set => Set(ref _KeyBindings, value); }

        bool _PanMove = true;
        public bool         PanMove         { get => _PanMove; set => Set(ref _PanMove, value); }

        bool _DragMove = true;
        public bool         DragMove        { get => _DragMove; set => Set(ref _DragMove, value); }

        int panPrevX, panPrevY;
        Point mouseLeftDownPoint = new Point(0, 0);
        bool designMode = (LicenseManager.UsageMode == LicenseUsageMode.Designtime);
        Point oldLocation = Point.Empty;
        Size oldSize = Size.Empty;
        FormBorderStyle oldStyle = FormBorderStyle.None;
        Control oldParent = null;
        LogHandler Log;

        public FlyleafHost()
        {
            AllowDrop = true;
            BackColor = Color.Black;

            if (designMode)
                return;

            Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost   ] ");

            KeyUp       += Host_KeyUp;
            KeyDown     += Host_KeyDown;
            DoubleClick += Host_DoubleClick;
            MouseDown   += Host_MouseDown;
            MouseMove   += Host_MouseMove;
            MouseWheel  += Host_MouseWheel;
            DragEnter   += Host_DragEnter;
            DragDrop    += Host_DragDrop;
        }

        private void Host_DragDrop(object sender, DragEventArgs e)
        {
            if (Player == null)
                return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string filename = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
                Player.OpenAsync(filename);
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string text = e.Data.GetData(DataFormats.Text, false).ToString();
                if (text.Length > 0)
                    Player.OpenAsync(text);
            }
        }
        private void Host_DragEnter(object sender, DragEventArgs e) { if (Player != null) e.Effect = DragDropEffects.All; }
        private void Host_MouseWheel(object sender, MouseEventArgs e)
        {
            if (PanMove && Player != null && e.Delta != 0 && ModifierKeys.HasFlag(Keys.Control))
            {
                Player.Zoom += e.Delta > 0 ? Player.Config.Player.ZoomOffset : -Player.Config.Player.ZoomOffset;
            }
        }
        private void Host_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Player == null)
                return;

            //if (Player.Config.Player.ActivityMode)
                //Player.Activity.RefreshFullActive();
            
            if (PanMove && ModifierKeys.HasFlag(Keys.Control))
            {
                Player.PanXOffset = panPrevX + e.X - mouseLeftDownPoint.X;
                Player.PanYOffset = panPrevY + e.Y - mouseLeftDownPoint.Y;
            }
            else if (DragMove && ParentForm != null)// && !mouseLeftDownDeactivated)
            {
                ParentForm.Location  = new Point(ParentForm.Location.X + e.X - mouseLeftDownPoint.X, ParentForm.Location.Y + e.Y - mouseLeftDownPoint.Y);
            }
        }
        private void Host_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            mouseLeftDownPoint = new Point(e.X, e.Y);
            if (Player != null)
            {
                panPrevX = Player.PanXOffset;
                panPrevY = Player.PanYOffset;
            }
        }
        private void Host_DoubleClick(object sender, EventArgs e) { if (!ToggleFullScreenOnDoubleClick) return; IsFullScreen = !IsFullScreen; }
        private void Host_KeyDown(object sender, KeyEventArgs e) { if (KeyBindings) Player.KeyDown(Player, e); }
        private void Host_KeyUp(object sender, KeyEventArgs e) { if (KeyBindings) Player.KeyUp(Player, e); }

        public void SetPlayer(Player oldPlayer)
        {
            // Note: We should never get old = new = null

            // De-assign old Player's Handle/FlyleafHost
            if (oldPlayer != null)
            {
                Log.Debug($"De-assign Player #{oldPlayer.PlayerId}");

                if (oldPlayer.renderer != null)
                    oldPlayer.renderer.SetControl(null);
                
                oldPlayer.WFHost = null;
                oldPlayer.IsFullScreen = false;
            }

            if (Player == null)
            {
                return;
            }

            // Set UniqueId (First Player's Id)
            if (UniqueId == -1)
            {
                UniqueId    = Player.PlayerId;
                Log.Prefix  = ("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost   ] ";
            }

            // De-assign new Player's Handle/FlyleafHost
            //Player.renderer.SetControl(null);
            if (Player.WFHost != null)
                Player.WFHost.Player = null;

            // Assign new Player's (Handle/FlyleafHost)
            Log.Debug($"Assign Player #{Player.PlayerId}");

            Player.WFHost = this;
            Player.IsFullScreen = IsFullScreen;
            Player.VideoDecoder.CreateRenderer(this);
        }

        public void FullScreen()
        {
            if (ParentForm == null)
                return;

            oldStyle = ParentForm.FormBorderStyle;
            oldLocation = Location;
            oldSize = Size;
            oldParent = Parent;

            ParentForm.FormBorderStyle = FormBorderStyle.None;
            ParentForm.WindowState = FormWindowState.Maximized;
            Parent = ParentForm;
            Location = new Point(0, 0);
            Size = ParentForm.ClientSize;

            BringToFront();
            Focus();

            _IsFullScreen = true;
            Raise(nameof(IsFullScreen));
        }
        public void NormalScreen()
        {
            if (ParentForm == null)
                return;

            ParentForm.FormBorderStyle = oldStyle;
            ParentForm.WindowState = FormWindowState.Normal;
            Parent = oldParent;

            Location = oldLocation;
            Size = oldSize;

            Focus();

            _IsFullScreen = false;
            Raise(nameof(IsFullScreen));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
        {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                return true;
            }

            return false;
        }
        protected void Raise([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected override bool IsInputKey(Keys keyData) { return Player != null && Player.WFHost != null; } // Required to allow keybindings such as arrows etc.
        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            if (Player != null && Player.renderer != null)
                Player?.renderer?.Present(); 
            else
                base.OnPaintBackground(pe);
        }            
        protected override void OnPaint(PaintEventArgs pe) { }
    }
}