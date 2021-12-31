namespace FlyleafAudioPlayer__Custom___WinForms_
{
    partial class FrmMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lstPlaylist = new System.Windows.Forms.ListBox();
            this.sliderCurTime = new FlyleafAudioPlayer__Custom___WinForms_.TrackBarWithoutFocus();
            this.lblCurTime = new System.Windows.Forms.Label();
            this.lblDuration = new System.Windows.Forms.Label();
            this.btnPlayPause = new System.Windows.Forms.Button();
            this.btnForward = new System.Windows.Forms.Button();
            this.btnForward2 = new System.Windows.Forms.Button();
            this.btnBackward = new System.Windows.Forms.Button();
            this.btnBackward2 = new System.Windows.Forms.Button();
            this.sliderVolume = new FlyleafAudioPlayer__Custom___WinForms_.TrackBarWithoutFocus();
            this.lblVolume = new System.Windows.Forms.Label();
            this.chkRepeat = new System.Windows.Forms.CheckBox();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnMute = new System.Windows.Forms.Button();
            this.lblMsgs = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.sliderCurTime)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.sliderVolume)).BeginInit();
            this.SuspendLayout();
            // 
            // lstPlaylist
            // 
            this.lstPlaylist.FormattingEnabled = true;
            this.lstPlaylist.ItemHeight = 15;
            this.lstPlaylist.Location = new System.Drawing.Point(5, 10);
            this.lstPlaylist.Name = "lstPlaylist";
            this.lstPlaylist.Size = new System.Drawing.Size(495, 94);
            this.lstPlaylist.TabIndex = 0;
            // 
            // sliderCurTime
            // 
            this.sliderCurTime.AutoSize = false;
            this.sliderCurTime.Location = new System.Drawing.Point(60, 124);
            this.sliderCurTime.Maximum = 0;
            this.sliderCurTime.Name = "sliderCurTime";
            this.sliderCurTime.Size = new System.Drawing.Size(426, 26);
            this.sliderCurTime.TabIndex = 1;
            this.sliderCurTime.TickStyle = System.Windows.Forms.TickStyle.None;
            this.sliderCurTime.ValueChanged += new System.EventHandler(this.sliderCurTime_ValueChanged);
            this.sliderCurTime.MouseDown += new System.Windows.Forms.MouseEventHandler(this.sliderCurTime_MouseDown);
            this.sliderCurTime.MouseMove += new System.Windows.Forms.MouseEventHandler(this.sliderCurTime_MouseMove);
            // 
            // lblCurTime
            // 
            this.lblCurTime.AutoSize = true;
            this.lblCurTime.Location = new System.Drawing.Point(5, 128);
            this.lblCurTime.Name = "lblCurTime";
            this.lblCurTime.Size = new System.Drawing.Size(49, 15);
            this.lblCurTime.TabIndex = 2;
            this.lblCurTime.Text = "00:00:00";
            // 
            // lblDuration
            // 
            this.lblDuration.AutoSize = true;
            this.lblDuration.Location = new System.Drawing.Point(492, 128);
            this.lblDuration.Name = "lblDuration";
            this.lblDuration.Size = new System.Drawing.Size(49, 15);
            this.lblDuration.TabIndex = 3;
            this.lblDuration.Text = "00:00:00";
            // 
            // btnPlayPause
            // 
            this.btnPlayPause.Location = new System.Drawing.Point(183, 151);
            this.btnPlayPause.Name = "btnPlayPause";
            this.btnPlayPause.Size = new System.Drawing.Size(75, 23);
            this.btnPlayPause.TabIndex = 4;
            this.btnPlayPause.Text = "Play";
            this.btnPlayPause.UseVisualStyleBackColor = true;
            // 
            // btnForward
            // 
            this.btnForward.Location = new System.Drawing.Point(345, 151);
            this.btnForward.Name = "btnForward";
            this.btnForward.Size = new System.Drawing.Size(33, 23);
            this.btnForward.TabIndex = 5;
            this.btnForward.Text = ">";
            this.btnForward.UseVisualStyleBackColor = true;
            // 
            // btnForward2
            // 
            this.btnForward2.Location = new System.Drawing.Point(384, 151);
            this.btnForward2.Name = "btnForward2";
            this.btnForward2.Size = new System.Drawing.Size(33, 23);
            this.btnForward2.TabIndex = 6;
            this.btnForward2.Text = ">>";
            this.btnForward2.UseVisualStyleBackColor = true;
            // 
            // btnBackward
            // 
            this.btnBackward.Location = new System.Drawing.Point(144, 151);
            this.btnBackward.Name = "btnBackward";
            this.btnBackward.Size = new System.Drawing.Size(33, 23);
            this.btnBackward.TabIndex = 7;
            this.btnBackward.Text = "<";
            this.btnBackward.UseVisualStyleBackColor = true;
            // 
            // btnBackward2
            // 
            this.btnBackward2.Location = new System.Drawing.Point(105, 151);
            this.btnBackward2.Name = "btnBackward2";
            this.btnBackward2.Size = new System.Drawing.Size(33, 23);
            this.btnBackward2.TabIndex = 8;
            this.btnBackward2.Text = "<<";
            this.btnBackward2.UseVisualStyleBackColor = true;
            // 
            // sliderVolume
            // 
            this.sliderVolume.AutoSize = false;
            this.sliderVolume.Location = new System.Drawing.Point(511, 10);
            this.sliderVolume.Maximum = 0;
            this.sliderVolume.Name = "sliderVolume";
            this.sliderVolume.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.sliderVolume.Size = new System.Drawing.Size(26, 78);
            this.sliderVolume.TabIndex = 9;
            this.sliderVolume.TickStyle = System.Windows.Forms.TickStyle.None;
            this.sliderVolume.ValueChanged += new System.EventHandler(this.sliderVolume_ValueChanged);
            this.sliderVolume.MouseDown += new System.Windows.Forms.MouseEventHandler(this.sliderVolume_MouseDown);
            this.sliderVolume.MouseMove += new System.Windows.Forms.MouseEventHandler(this.sliderVolume_MouseMove);
            // 
            // lblVolume
            // 
            this.lblVolume.AutoSize = true;
            this.lblVolume.Location = new System.Drawing.Point(506, 91);
            this.lblVolume.Name = "lblVolume";
            this.lblVolume.Size = new System.Drawing.Size(35, 15);
            this.lblVolume.TabIndex = 10;
            this.lblVolume.Text = "200%";
            // 
            // chkRepeat
            // 
            this.chkRepeat.AutoSize = true;
            this.chkRepeat.Checked = true;
            this.chkRepeat.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkRepeat.Location = new System.Drawing.Point(5, 155);
            this.chkRepeat.Name = "chkRepeat";
            this.chkRepeat.Size = new System.Drawing.Size(62, 19);
            this.chkRepeat.TabIndex = 11;
            this.chkRepeat.Text = "Repeat";
            this.chkRepeat.UseVisualStyleBackColor = true;
            // 
            // btnStop
            // 
            this.btnStop.Location = new System.Drawing.Point(264, 151);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(75, 23);
            this.btnStop.TabIndex = 12;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            // 
            // btnMute
            // 
            this.btnMute.Location = new System.Drawing.Point(483, 151);
            this.btnMute.Name = "btnMute";
            this.btnMute.Size = new System.Drawing.Size(58, 23);
            this.btnMute.TabIndex = 13;
            this.btnMute.Text = "Mute";
            this.btnMute.UseVisualStyleBackColor = true;
            // 
            // lblOpenMsg
            // 
            this.lblMsgs.AutoSize = true;
            this.lblMsgs.Location = new System.Drawing.Point(5, 107);
            this.lblMsgs.Name = "lblOpenMsg";
            this.lblMsgs.Size = new System.Drawing.Size(65, 15);
            this.lblMsgs.TabIndex = 14;
            this.lblMsgs.Text = "Opening ...";
            // 
            // FrmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(544, 182);
            this.Controls.Add(this.lblMsgs);
            this.Controls.Add(this.btnMute);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.chkRepeat);
            this.Controls.Add(this.lblVolume);
            this.Controls.Add(this.sliderVolume);
            this.Controls.Add(this.btnBackward2);
            this.Controls.Add(this.btnBackward);
            this.Controls.Add(this.btnForward2);
            this.Controls.Add(this.btnForward);
            this.Controls.Add(this.btnPlayPause);
            this.Controls.Add(this.lblDuration);
            this.Controls.Add(this.lblCurTime);
            this.Controls.Add(this.sliderCurTime);
            this.Controls.Add(this.lstPlaylist);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "FrmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Flyleaf Audio Player";
            ((System.ComponentModel.ISupportInitialize)(this.sliderCurTime)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.sliderVolume)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private ListBox lstPlaylist;
        private Label lblCurTime;
        private Label lblDuration;
        private Button btnPlayPause;
        private Button btnForward;
        private Button btnForward2;
        private Button btnBackward;
        private Button btnBackward2;
        private TrackBarWithoutFocus sliderCurTime;
        private TrackBarWithoutFocus sliderVolume;
        private Label lblVolume;
        private CheckBox chkRepeat;
        private Button btnStop;
        private Button btnMute;
        private Label lblMsgs;
    }
}