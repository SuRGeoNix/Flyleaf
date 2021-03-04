namespace SuRGeoNix.FlyleafPlayer
{
    partial class FrmMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmMain));
            this.flyleafPlayer1 = new SuRGeoNix.Flyleaf.Controls.FlyleafPlayer();
            this.SuspendLayout();
            // 
            // flyleafPlayer1
            // 
            this.flyleafPlayer1._audio._Enabled = true;
            this.flyleafPlayer1._bar._Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.OnFullActive;
            this.flyleafPlayer1._bar.Mute = true;
            this.flyleafPlayer1._bar.Play = true;
            this.flyleafPlayer1._bar.Playlist = true;
            this.flyleafPlayer1._bar.SeekOnSlide = true;
            this.flyleafPlayer1._bar.Subs = true;
            this.flyleafPlayer1._bar.Volume = true;
            this.flyleafPlayer1._form._Enabled = true;
            this.flyleafPlayer1._form.AllowResize = true;
            this.flyleafPlayer1._form.AutoResize = true;
            this.flyleafPlayer1._form.HookHandle = false;
            this.flyleafPlayer1._form.HookKeys = true;
            this.flyleafPlayer1._keys._Enabled = true;
            this.flyleafPlayer1._keys.AudioDelayStep = 20;
            this.flyleafPlayer1._keys.AudioDelayStep2 = 5000;
            this.flyleafPlayer1._keys.SeekStep = 500;
            this.flyleafPlayer1._keys.SeekStepFirst = 5000;
            this.flyleafPlayer1._keys.SubsDelayStep = 100;
            this.flyleafPlayer1._keys.SubsDelayStep2 = 5000;
            this.flyleafPlayer1._keys.SubsFontSizeStep = 2;
            this.flyleafPlayer1._keys.SubsPosYStep = 5;
            this.flyleafPlayer1._keys.VolStep = 3;
            this.flyleafPlayer1._main._PreviewMode = SuRGeoNix.Flyleaf.MediaRouter.ActivityMode.FullActive;
            this.flyleafPlayer1._main.AllowDrop = true;
            this.flyleafPlayer1._main.AllowFullScreen = true;
            this.flyleafPlayer1._main.ClearBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._main.EmbeddedList = true;
            this.flyleafPlayer1._main.HideCursor = true;
            this.flyleafPlayer1._main.HistoryEnabled = true;
            this.flyleafPlayer1._main.HistoryEntries = 30;
            this.flyleafPlayer1._main.IdleTimeout = 7000;
            this.flyleafPlayer1._main.MessagesDuration = 4000;
            this.flyleafPlayer1._main.SampleFrame = null;
            this.flyleafPlayer1._main.ShutdownAfterIdle = 300;
            this.flyleafPlayer1._main.ShutdownOnFinish = false;
            this.flyleafPlayer1._message0.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft;
            this.flyleafPlayer1._message0.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.OnActive;
            this.flyleafPlayer1._message1.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message1.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message10.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message10.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message11.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message11.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message12.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message12.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message13.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.BottomCenter;
            this.flyleafPlayer1._message13.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message14.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.BottomRight;
            this.flyleafPlayer1._message14.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.OnActive;
            this.flyleafPlayer1._message15.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft;
            this.flyleafPlayer1._message15.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message16.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message16.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message17.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message17.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message18.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight2;
            this.flyleafPlayer1._message18.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message19.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.BottomLeft;
            this.flyleafPlayer1._message19.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message2.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message2.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message20.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.BottomRight;
            this.flyleafPlayer1._message20.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message3.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message3.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message4.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message4.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message5.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message5.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message6.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message6.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message7.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message7.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message8.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopRight;
            this.flyleafPlayer1._message8.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._message9.Surface = SuRGeoNix.Flyleaf.Settings.OSDSurfaces.TopLeft2;
            this.flyleafPlayer1._message9.Visibility = SuRGeoNix.Flyleaf.MediaRouter.VisibilityMode.Always;
            this.flyleafPlayer1._subtitles._Enabled = true;
            this.flyleafPlayer1._subtitles.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
            this.flyleafPlayer1._subtitles.DownloadSubs = SuRGeoNix.Flyleaf.MediaRouter.DownloadSubsMode.FilesAndTorrents;
            this.flyleafPlayer1._subtitles.Font = new System.Drawing.Font("Arial", 51F, System.Drawing.FontStyle.Bold);
            this.flyleafPlayer1._subtitles.Languages = new string[] {
        "Spanish"};
            this.flyleafPlayer1._subtitles.OnViewPort = false;
            this.flyleafPlayer1._subtitles.Position = new System.Drawing.Point(0, -20);
            this.flyleafPlayer1._subtitles.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._subtitles.RectEnabled = false;
            this.flyleafPlayer1._subtitles.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface1.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._surface1.Font = new System.Drawing.Font("Perpetua", 26F);
            this.flyleafPlayer1._surface1.OnViewPort = true;
            this.flyleafPlayer1._surface1.Position = new System.Drawing.Point(12, 12);
            this.flyleafPlayer1._surface1.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface1.RectEnabled = true;
            this.flyleafPlayer1._surface1.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface2.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._surface2.Font = new System.Drawing.Font("Perpetua", 26F);
            this.flyleafPlayer1._surface2.OnViewPort = true;
            this.flyleafPlayer1._surface2.Position = new System.Drawing.Point(-12, 12);
            this.flyleafPlayer1._surface2.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface2.RectEnabled = true;
            this.flyleafPlayer1._surface2.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface3.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._surface3.Font = new System.Drawing.Font("Perpetua", 26F);
            this.flyleafPlayer1._surface3.OnViewPort = true;
            this.flyleafPlayer1._surface3.Position = new System.Drawing.Point(12, 60);
            this.flyleafPlayer1._surface3.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface3.RectEnabled = true;
            this.flyleafPlayer1._surface3.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface4.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._surface4.Font = new System.Drawing.Font("Perpetua", 26F);
            this.flyleafPlayer1._surface4.OnViewPort = true;
            this.flyleafPlayer1._surface4.Position = new System.Drawing.Point(-12, 60);
            this.flyleafPlayer1._surface4.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface4.RectEnabled = true;
            this.flyleafPlayer1._surface4.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface5.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._surface5.Font = new System.Drawing.Font("Perpetua", 26F);
            this.flyleafPlayer1._surface5.OnViewPort = true;
            this.flyleafPlayer1._surface5.Position = new System.Drawing.Point(12, -12);
            this.flyleafPlayer1._surface5.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface5.RectEnabled = true;
            this.flyleafPlayer1._surface5.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface6.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(0)))));
            this.flyleafPlayer1._surface6.Font = new System.Drawing.Font("Perpetua", 26F);
            this.flyleafPlayer1._surface6.OnViewPort = false;
            this.flyleafPlayer1._surface6.Position = new System.Drawing.Point(-12, -40);
            this.flyleafPlayer1._surface6.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface6.RectEnabled = true;
            this.flyleafPlayer1._surface6.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._surface7.Color = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
            this.flyleafPlayer1._surface7.Font = new System.Drawing.Font("Arial", 51F, System.Drawing.FontStyle.Bold);
            this.flyleafPlayer1._surface7.OnViewPort = false;
            this.flyleafPlayer1._surface7.Position = new System.Drawing.Point(0, -20);
            this.flyleafPlayer1._surface7.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(168)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.flyleafPlayer1._surface7.RectEnabled = false;
            this.flyleafPlayer1._surface7.RectPadding = new System.Windows.Forms.Padding(2, -2, 2, -2);
            this.flyleafPlayer1._video.AspectRatio = SuRGeoNix.Flyleaf.MediaRouter.ViewPorts.KEEP;
            this.flyleafPlayer1._video.CustomRatio = 1.777778F;
            this.flyleafPlayer1._video.DecoderThreads = 16;
            this.flyleafPlayer1._video.HardwareAcceleration = true;
            this.flyleafPlayer1._video.QueueMaxSize = 100;
            this.flyleafPlayer1._video.QueueMinSize = 10;
            this.flyleafPlayer1._video.VSync = false;
            this.flyleafPlayer1.AllowDrop = true;
            this.flyleafPlayer1.BackColor = System.Drawing.Color.Black;
            this.flyleafPlayer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flyleafPlayer1.Location = new System.Drawing.Point(0, 0);
            this.flyleafPlayer1.Name = "flyleafPlayer1";
            this.flyleafPlayer1.Size = new System.Drawing.Size(1310, 833);
            this.flyleafPlayer1.TabIndex = 0;
            this.flyleafPlayer1.TabStop = false;
            // 
            // FrmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoValidate = System.Windows.Forms.AutoValidate.Disable;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(1310, 833);
            this.Controls.Add(this.flyleafPlayer1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FrmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.ResumeLayout(false);

        }

        #endregion

        private Flyleaf.Controls.FlyleafPlayer flyleafPlayer1;
    }
}