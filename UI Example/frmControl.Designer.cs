
namespace PartyTime.UI_Example
{
    partial class frmControl
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
            this.seekBar = new ColorSlider.ColorSlider();
            this.lblSubs = new System.Windows.Forms.Label();
            this.lblInfoText = new System.Windows.Forms.Label();
            this.volBar = new ColorSlider.ColorSlider();
            this.rtbSubs = new System.Windows.Forms.RichTextBox();
            this.lstMediaFiles = new System.Windows.Forms.ListBox();
            this.SuspendLayout();
            // 
            // seekBar
            // 
            this.seekBar.BackColor = System.Drawing.Color.Black;
            this.seekBar.BarPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(94)))), ((int)(((byte)(110)))));
            this.seekBar.BarPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(60)))), ((int)(((byte)(74)))));
            this.seekBar.BorderRoundRectSize = new System.Drawing.Size(25, 25);
            this.seekBar.ElapsedInnerColor = System.Drawing.Color.FromArgb(((int)(((byte)(21)))), ((int)(((byte)(56)))), ((int)(((byte)(152)))));
            this.seekBar.ElapsedPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(99)))), ((int)(((byte)(130)))), ((int)(((byte)(208)))));
            this.seekBar.ElapsedPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(95)))), ((int)(((byte)(140)))), ((int)(((byte)(180)))));
            this.seekBar.Font = new System.Drawing.Font("Verdana", 7.25F);
            this.seekBar.ForeColor = System.Drawing.Color.White;
            this.seekBar.LargeChange = ((uint)(1u));
            this.seekBar.Location = new System.Drawing.Point(12, 592);
            this.seekBar.Margin = new System.Windows.Forms.Padding(1234);
            this.seekBar.Name = "seekBar";
            this.seekBar.ScaleDivisions = 1;
            this.seekBar.ScaleSubDivisions = 1;
            this.seekBar.ShowDivisionsText = false;
            this.seekBar.ShowSmallScale = false;
            this.seekBar.Size = new System.Drawing.Size(1080, 29);
            this.seekBar.SmallChange = ((uint)(1u));
            this.seekBar.TabIndex = 4;
            this.seekBar.TabStop = false;
            this.seekBar.ThumbInnerColor = System.Drawing.Color.FromArgb(((int)(((byte)(21)))), ((int)(((byte)(56)))), ((int)(((byte)(152)))));
            this.seekBar.ThumbPenColor = System.Drawing.Color.FromArgb(((int)(((byte)(21)))), ((int)(((byte)(56)))), ((int)(((byte)(152)))));
            this.seekBar.ThumbRoundRectSize = new System.Drawing.Size(5, 5);
            this.seekBar.ThumbSize = new System.Drawing.Size(20, 20);
            this.seekBar.TickAdd = 0F;
            this.seekBar.TickColor = System.Drawing.Color.White;
            this.seekBar.TickDivide = 0F;
            this.seekBar.TickStyle = System.Windows.Forms.TickStyle.None;
            this.seekBar.Value = 50;
            // 
            // lblSubs
            // 
            this.lblSubs.AutoSize = true;
            this.lblSubs.Font = new System.Drawing.Font("Arial", 22F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSubs.ForeColor = System.Drawing.Color.White;
            this.lblSubs.Location = new System.Drawing.Point(443, 499);
            this.lblSubs.Name = "lblSubs";
            this.lblSubs.Size = new System.Drawing.Size(391, 70);
            this.lblSubs.TabIndex = 5;
            this.lblSubs.Text = "Subtitles Label\r\nMore than one Lines Aligned";
            this.lblSubs.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblSubs.Visible = false;
            // 
            // lblInfoText
            // 
            this.lblInfoText.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.lblInfoText.Font = new System.Drawing.Font("Arial", 16F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblInfoText.ForeColor = System.Drawing.Color.White;
            this.lblInfoText.Location = new System.Drawing.Point(12, 9);
            this.lblInfoText.Margin = new System.Windows.Forms.Padding(0);
            this.lblInfoText.Name = "lblInfoText";
            this.lblInfoText.Size = new System.Drawing.Size(153, 25);
            this.lblInfoText.TabIndex = 6;
            this.lblInfoText.Text = "Info Text Label";
            this.lblInfoText.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // volBar
            // 
            this.volBar.BackColor = System.Drawing.Color.Black;
            this.volBar.BarPenColorBottom = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(94)))), ((int)(((byte)(110)))));
            this.volBar.BarPenColorTop = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(60)))), ((int)(((byte)(74)))));
            this.volBar.BorderRoundRectSize = new System.Drawing.Size(1, 1);
            this.volBar.ColorSchema = ColorSlider.ColorSlider.ColorSchemas.GreenColors;
            this.volBar.ElapsedInnerColor = System.Drawing.Color.Green;
            this.volBar.ElapsedPenColorBottom = System.Drawing.Color.LightGreen;
            this.volBar.ElapsedPenColorTop = System.Drawing.Color.SpringGreen;
            this.volBar.Font = new System.Drawing.Font("Verdana", 7.25F);
            this.volBar.ForeColor = System.Drawing.Color.White;
            this.volBar.LargeChange = ((uint)(1u));
            this.volBar.Location = new System.Drawing.Point(1121, 592);
            this.volBar.Name = "volBar";
            this.volBar.ScaleDivisions = 1;
            this.volBar.ScaleSubDivisions = 1;
            this.volBar.ShowDivisionsText = false;
            this.volBar.ShowSmallScale = false;
            this.volBar.Size = new System.Drawing.Size(98, 29);
            this.volBar.SmallChange = ((uint)(1u));
            this.volBar.TabIndex = 8;
            this.volBar.TabStop = false;
            this.volBar.ThumbInnerColor = System.Drawing.Color.Green;
            this.volBar.ThumbPenColor = System.Drawing.Color.Green;
            this.volBar.ThumbRoundRectSize = new System.Drawing.Size(20, 20);
            this.volBar.ThumbSize = new System.Drawing.Size(20, 20);
            this.volBar.TickAdd = 0F;
            this.volBar.TickColor = System.Drawing.Color.White;
            this.volBar.TickDivide = 0F;
            this.volBar.TickStyle = System.Windows.Forms.TickStyle.None;
            this.volBar.Value = 50;
            // 
            // rtbSubs
            // 
            this.rtbSubs.BackColor = System.Drawing.Color.Black;
            this.rtbSubs.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbSubs.CausesValidation = false;
            this.rtbSubs.Cursor = System.Windows.Forms.Cursors.Default;
            this.rtbSubs.DetectUrls = false;
            this.rtbSubs.Font = new System.Drawing.Font("Arial", 32.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(161)));
            this.rtbSubs.ForeColor = System.Drawing.Color.White;
            this.rtbSubs.Location = new System.Drawing.Point(229, 385);
            this.rtbSubs.Name = "rtbSubs";
            this.rtbSubs.ReadOnly = true;
            this.rtbSubs.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.None;
            this.rtbSubs.ShortcutsEnabled = false;
            this.rtbSubs.Size = new System.Drawing.Size(801, 94);
            this.rtbSubs.TabIndex = 9;
            this.rtbSubs.TabStop = false;
            this.rtbSubs.Text = "Subtitles Label\nMore than one Lines Aligned";
            // 
            // lstMediaFiles
            // 
            this.lstMediaFiles.AllowDrop = true;
            this.lstMediaFiles.BackColor = System.Drawing.Color.Black;
            this.lstMediaFiles.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lstMediaFiles.Font = new System.Drawing.Font("Arial", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(161)));
            this.lstMediaFiles.ForeColor = System.Drawing.Color.Green;
            this.lstMediaFiles.FormattingEnabled = true;
            this.lstMediaFiles.HorizontalScrollbar = true;
            this.lstMediaFiles.ItemHeight = 22;
            this.lstMediaFiles.Location = new System.Drawing.Point(317, 100);
            this.lstMediaFiles.Name = "lstMediaFiles";
            this.lstMediaFiles.Size = new System.Drawing.Size(603, 332);
            this.lstMediaFiles.Sorted = true;
            this.lstMediaFiles.TabIndex = 10;
            this.lstMediaFiles.TabStop = false;
            this.lstMediaFiles.Visible = false;
            // 
            // frmControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(1231, 621);
            this.Controls.Add(this.lstMediaFiles);
            this.Controls.Add(this.rtbSubs);
            this.Controls.Add(this.volBar);
            this.Controls.Add(this.lblInfoText);
            this.Controls.Add(this.lblSubs);
            this.Controls.Add(this.seekBar);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "frmControl";
            this.ShowInTaskbar = false;
            this.Text = "`";
            this.Load += new System.EventHandler(this.frmControl_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public ColorSlider.ColorSlider seekBar;
        public System.Windows.Forms.Label lblSubs;
        public System.Windows.Forms.Label lblInfoText;
        public ColorSlider.ColorSlider volBar;
        public System.Windows.Forms.RichTextBox rtbSubs;
        public System.Windows.Forms.ListBox lstMediaFiles;
    }
}