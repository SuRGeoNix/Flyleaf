namespace SuRGeoNix.Flyleaf.Controls
{
    partial class FlyleafBar
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tblBar = new System.Windows.Forms.TableLayoutPanel();
            this.volBar = new ColorSlider.ColorSlider();
            this.btnPlaylist = new System.Windows.Forms.Button();
            this.btnPlay = new System.Windows.Forms.Button();
            this.seekBar = new ColorSlider.ColorSlider();
            this.btnMute = new System.Windows.Forms.Button();
            this.tblBar.SuspendLayout();
            this.SuspendLayout();
            // 
            // tblBar
            // 
            this.tblBar.BackColor = System.Drawing.Color.Black;
            this.tblBar.ColumnCount = 5;
            this.tblBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 45F));
            this.tblBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tblBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 45F));
            this.tblBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tblBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 85F));
            this.tblBar.Controls.Add(this.volBar, 4, 0);
            this.tblBar.Controls.Add(this.btnPlaylist, 2, 0);
            this.tblBar.Controls.Add(this.btnPlay, 0, 0);
            this.tblBar.Controls.Add(this.seekBar, 1, 0);
            this.tblBar.Controls.Add(this.btnMute, 3, 0);
            this.tblBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.tblBar.Location = new System.Drawing.Point(0, 0);
            this.tblBar.Margin = new System.Windows.Forms.Padding(0);
            this.tblBar.Name = "tblBar";
            this.tblBar.RowCount = 1;
            this.tblBar.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tblBar.Size = new System.Drawing.Size(1000, 44);
            this.tblBar.TabIndex = 25;
            this.tblBar.CellPaint += new System.Windows.Forms.TableLayoutCellPaintEventHandler(this.tblBar_CellPaint);
            // 
            // volBar
            // 
            this.volBar.BackColor = System.Drawing.Color.Transparent;
            this.volBar.BarPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(94)))), ((int)(((byte)(110)))));
            this.volBar.BarPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(60)))), ((int)(((byte)(74)))));
            this.volBar.BorderRoundRectSize = new System.Drawing.Size(1, 1);
            this.volBar.ColorSchema = ColorSlider.ColorSlider.ColorSchemas.GreenColors;
            this.volBar.ElapsedInnerColor = System.Drawing.Color.FromArgb(((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.volBar.ElapsedPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(228)))), ((int)(((byte)(150)))), ((int)(((byte)(255)))));
            this.volBar.ElapsedPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(0)))), ((int)(((byte)(51)))));
            this.volBar.Font = new System.Drawing.Font("Verdana", 7.25F);
            this.volBar.ForeColor = System.Drawing.Color.White;
            this.volBar.LargeChange = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.volBar.Location = new System.Drawing.Point(915, 2);
            this.volBar.Margin = new System.Windows.Forms.Padding(0, 2, 15, 2);
            this.volBar.Maximum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.volBar.Minimum = new decimal(new int[] {
            0,
            0,
            0,
            0});
            this.volBar.Name = "volBar";
            this.volBar.ScaleDivisions = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.volBar.ScaleSubDivisions = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.volBar.ShowDivisionsText = false;
            this.volBar.ShowSmallScale = false;
            this.volBar.Size = new System.Drawing.Size(70, 40);
            this.volBar.SmallChange = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.volBar.TabIndex = 17;
            this.volBar.TabStop = false;
            this.volBar.ThumbInnerColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(0)))), ((int)(((byte)(51)))));
            this.volBar.ThumbOuterColor = System.Drawing.Color.FromArgb(((int)(((byte)(228)))), ((int)(((byte)(150)))), ((int)(((byte)(255)))));
            this.volBar.ThumbPenColor = System.Drawing.Color.FromArgb(((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.volBar.ThumbRoundRectSize = new System.Drawing.Size(20, 20);
            this.volBar.ThumbSize = new System.Drawing.Size(20, 20);
            this.volBar.TickAdd = 0F;
            this.volBar.TickColor = System.Drawing.Color.White;
            this.volBar.TickDivide = 0F;
            this.volBar.TickStyle = System.Windows.Forms.TickStyle.None;
            this.volBar.Value = new decimal(new int[] {
            50,
            0,
            0,
            0});
            // 
            // btnPlaylist
            // 
            this.btnPlaylist.BackColor = System.Drawing.Color.Transparent;
            this.btnPlaylist.BackgroundImage = global::SuRGeoNix.Flyleaf.Properties.Resources.Playlist;
            this.btnPlaylist.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.btnPlaylist.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(60)))), ((int)(((byte)(74)))));
            this.btnPlaylist.FlatAppearance.BorderSize = 2;
            this.btnPlaylist.FlatAppearance.MouseDownBackColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnPlaylist.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(30)))), ((int)(((byte)(44)))));
            this.btnPlaylist.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPlaylist.ForeColor = System.Drawing.Color.Red;
            this.btnPlaylist.Location = new System.Drawing.Point(843, 2);
            this.btnPlaylist.Margin = new System.Windows.Forms.Padding(3, 2, 0, 2);
            this.btnPlaylist.Name = "btnPlaylist";
            this.btnPlaylist.Size = new System.Drawing.Size(40, 40);
            this.btnPlaylist.TabIndex = 26;
            this.btnPlaylist.TabStop = false;
            this.btnPlaylist.UseVisualStyleBackColor = false;
            // 
            // btnPlay
            // 
            this.btnPlay.BackColor = System.Drawing.Color.Transparent;
            this.btnPlay.BackgroundImage = global::SuRGeoNix.Flyleaf.Properties.Resources.Play;
            this.btnPlay.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.btnPlay.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(60)))), ((int)(((byte)(74)))));
            this.btnPlay.FlatAppearance.BorderSize = 2;
            this.btnPlay.FlatAppearance.MouseDownBackColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnPlay.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(30)))), ((int)(((byte)(44)))));
            this.btnPlay.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPlay.ForeColor = System.Drawing.Color.Red;
            this.btnPlay.Location = new System.Drawing.Point(3, 2);
            this.btnPlay.Margin = new System.Windows.Forms.Padding(3, 2, 0, 2);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(40, 40);
            this.btnPlay.TabIndex = 18;
            this.btnPlay.TabStop = false;
            this.btnPlay.UseVisualStyleBackColor = false;
            // 
            // seekBar
            // 
            this.seekBar.BackColor = System.Drawing.Color.Transparent;
            this.seekBar.BarPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(94)))), ((int)(((byte)(110)))));
            this.seekBar.BarPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(60)))), ((int)(((byte)(74)))));
            this.seekBar.BorderRoundRectSize = new System.Drawing.Size(25, 25);
            this.seekBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.seekBar.ElapsedInnerColor = System.Drawing.Color.FromArgb(((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.seekBar.ElapsedPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(228)))), ((int)(((byte)(150)))), ((int)(((byte)(255)))));
            this.seekBar.ElapsedPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(0)))), ((int)(((byte)(51)))));
            this.seekBar.Font = new System.Drawing.Font("Verdana", 7.25F);
            this.seekBar.ForeColor = System.Drawing.Color.White;
            this.seekBar.LargeChange = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seekBar.Location = new System.Drawing.Point(60, 2);
            this.seekBar.Margin = new System.Windows.Forms.Padding(15, 2, 14, 2);
            this.seekBar.Maximum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seekBar.Minimum = new decimal(new int[] {
            0,
            0,
            0,
            0});
            this.seekBar.Name = "seekBar";
            this.seekBar.ScaleDivisions = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seekBar.ScaleSubDivisions = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seekBar.ShowDivisionsText = false;
            this.seekBar.ShowSmallScale = false;
            this.seekBar.Size = new System.Drawing.Size(766, 40);
            this.seekBar.SmallChange = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seekBar.TabIndex = 14;
            this.seekBar.TabStop = false;
            this.seekBar.ThumbInnerColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(0)))), ((int)(((byte)(51)))));
            this.seekBar.ThumbOuterColor = System.Drawing.Color.FromArgb(((int)(((byte)(228)))), ((int)(((byte)(150)))), ((int)(((byte)(255)))));
            this.seekBar.ThumbPenColor = System.Drawing.Color.FromArgb(((int)(((byte)(78)))), ((int)(((byte)(0)))), ((int)(((byte)(131)))));
            this.seekBar.ThumbRoundRectSize = new System.Drawing.Size(5, 5);
            this.seekBar.ThumbSize = new System.Drawing.Size(20, 20);
            this.seekBar.TickAdd = 0F;
            this.seekBar.TickColor = System.Drawing.Color.White;
            this.seekBar.TickDivide = 0F;
            this.seekBar.TickStyle = System.Windows.Forms.TickStyle.None;
            this.seekBar.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // btnMute
            // 
            this.btnMute.BackColor = System.Drawing.Color.Transparent;
            this.btnMute.BackgroundImage = global::SuRGeoNix.Flyleaf.Properties.Resources.Speaker;
            this.btnMute.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnMute.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(30)))), ((int)(((byte)(44)))));
            this.btnMute.FlatAppearance.BorderSize = 0;
            this.btnMute.FlatAppearance.MouseDownBackColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnMute.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(30)))), ((int)(((byte)(44)))));
            this.btnMute.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMute.ForeColor = System.Drawing.Color.Red;
            this.btnMute.Location = new System.Drawing.Point(891, 11);
            this.btnMute.Margin = new System.Windows.Forms.Padding(6, 11, 4, 0);
            this.btnMute.Name = "btnMute";
            this.btnMute.Size = new System.Drawing.Size(20, 20);
            this.btnMute.TabIndex = 24;
            this.btnMute.TabStop = false;
            this.btnMute.UseVisualStyleBackColor = false;
            // 
            // FlyleafBar
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tblBar);
            this.Name = "FlyleafBar";
            this.Size = new System.Drawing.Size(1000, 44);
            this.tblBar.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.TableLayoutPanel tblBar;
        public System.Windows.Forms.Button btnPlay;
        public ColorSlider.ColorSlider seekBar;
        public System.Windows.Forms.Button btnMute;
        public ColorSlider.ColorSlider volBar;
        public System.Windows.Forms.Button btnPlaylist;
    }
}
