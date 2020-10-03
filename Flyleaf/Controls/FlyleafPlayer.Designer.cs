namespace SuRGeoNix.Flyleaf.Controls
{
    partial class FlyleafPlayer
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
            this.lstMediaFiles = new System.Windows.Forms.ListBox();
            this.tblBar = new SuRGeoNix.Flyleaf.Controls.FlyleafBar();
            this.SuspendLayout();
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
            this.lstMediaFiles.Location = new System.Drawing.Point(35, 22);
            this.lstMediaFiles.Name = "lstMediaFiles";
            this.lstMediaFiles.Size = new System.Drawing.Size(341, 156);
            this.lstMediaFiles.TabIndex = 11;
            this.lstMediaFiles.TabStop = false;
            this.lstMediaFiles.Visible = false;
            // 
            // tblBar
            // 
            this.tblBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.tblBar.Location = new System.Drawing.Point(0, 209);
            this.tblBar.Name = "tblBar";
            this.tblBar.Size = new System.Drawing.Size(419, 44);
            this.tblBar.TabIndex = 0;
            this.tblBar.TabStop = false;
            // 
            // FlyLeaf
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.Controls.Add(this.lstMediaFiles);
            this.Controls.Add(this.tblBar);
            this.Name = "FlyLeaf";
            this.Size = new System.Drawing.Size(419, 253);
            this.ResumeLayout(false);

        }

        #endregion
        public System.Windows.Forms.ListBox lstMediaFiles;
        public FlyleafBar tblBar;
    }
}
