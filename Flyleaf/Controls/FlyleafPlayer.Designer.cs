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
            this.lvSubs = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader3 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader4 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader5 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
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
            this.lstMediaFiles.Location = new System.Drawing.Point(28, 17);
            this.lstMediaFiles.Name = "lstMediaFiles";
            this.lstMediaFiles.Size = new System.Drawing.Size(341, 156);
            this.lstMediaFiles.TabIndex = 11;
            this.lstMediaFiles.TabStop = false;
            this.lstMediaFiles.Visible = false;
            // 
            // lvSubs
            // 
            this.lvSubs.BackColor = System.Drawing.Color.Black;
            this.lvSubs.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3,
            this.columnHeader4,
            this.columnHeader5});
            this.lvSubs.Font = new System.Drawing.Font("Arial", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(161)));
            this.lvSubs.ForeColor = System.Drawing.Color.Green;
            this.lvSubs.FullRowSelect = true;
            this.lvSubs.HideSelection = false;
            this.lvSubs.Location = new System.Drawing.Point(130, 30);
            this.lvSubs.Name = "lvSubs";
            this.lvSubs.Size = new System.Drawing.Size(336, 174);
            this.lvSubs.TabIndex = 12;
            this.lvSubs.TabStop = false;
            this.lvSubs.UseCompatibleStateImageBehavior = false;
            this.lvSubs.View = System.Windows.Forms.View.Details;
            this.lvSubs.Visible = false;
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "Name";
            // 
            // columnHeader2
            // 
            this.columnHeader2.Text = "Language";
            // 
            // columnHeader3
            // 
            this.columnHeader3.Text = "Rating";
            // 
            // columnHeader4
            // 
            this.columnHeader4.Text = "Downloaded";
            // 
            // columnHeader5
            // 
            this.columnHeader5.Text = "Location";
            // 
            // tblBar
            // 
            this.tblBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.tblBar.Location = new System.Drawing.Point(0, 216);
            this.tblBar.Name = "tblBar";
            this.tblBar.Size = new System.Drawing.Size(499, 44);
            this.tblBar.TabIndex = 0;
            this.tblBar.TabStop = false;
            // 
            // FlyleafPlayer
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.Controls.Add(this.lvSubs);
            this.Controls.Add(this.lstMediaFiles);
            this.Controls.Add(this.tblBar);
            this.Name = "FlyleafPlayer";
            this.Size = new System.Drawing.Size(499, 260);
            this.ResumeLayout(false);

        }

        #endregion
        public System.Windows.Forms.ListBox lstMediaFiles;
        public FlyleafBar tblBar;
        private System.Windows.Forms.ListView lvSubs;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.ColumnHeader columnHeader2;
        private System.Windows.Forms.ColumnHeader columnHeader3;
        private System.Windows.Forms.ColumnHeader columnHeader4;
        private System.Windows.Forms.ColumnHeader columnHeader5;
    }
}
