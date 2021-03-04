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
            this.components = new System.ComponentModel.Container();
            this.lstMediaFiles = new System.Windows.Forms.ListBox();
            this.lvSubs = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader3 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader4 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader5 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.menu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.recentToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.fileListToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.subtitlesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.tblBar = new SuRGeoNix.Flyleaf.Controls.FlyleafBar();
            this.mediaInfoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menu.SuspendLayout();
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
            // menu
            // 
            this.menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.recentToolStripMenuItem,
            this.toolStripSeparator1,
            this.fileListToolStripMenuItem,
            this.subtitlesToolStripMenuItem,
            this.settingsToolStripMenuItem,
            this.toolStripSeparator2,
            this.mediaInfoToolStripMenuItem,
            this.toolStripSeparator3,
            this.exitToolStripMenuItem});
            this.menu.Name = "menu";
            this.menu.Size = new System.Drawing.Size(181, 176);
            // 
            // recentToolStripMenuItem
            // 
            this.recentToolStripMenuItem.Name = "recentToolStripMenuItem";
            this.recentToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.recentToolStripMenuItem.Text = "Recent";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(177, 6);
            // 
            // fileListToolStripMenuItem
            // 
            this.fileListToolStripMenuItem.Name = "fileListToolStripMenuItem";
            this.fileListToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.fileListToolStripMenuItem.Text = "File List";
            this.fileListToolStripMenuItem.Click += new System.EventHandler(this.fileListToolStripMenuItem_Click);
            // 
            // subtitlesToolStripMenuItem
            // 
            this.subtitlesToolStripMenuItem.Name = "subtitlesToolStripMenuItem";
            this.subtitlesToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.subtitlesToolStripMenuItem.Text = "Subtitles";
            this.subtitlesToolStripMenuItem.Click += new System.EventHandler(this.subtitlesToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.settingsToolStripMenuItem.Text = "Settings";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.settingsToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(177, 6);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(177, 6);
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
            // mediaInfoToolStripMenuItem
            // 
            this.mediaInfoToolStripMenuItem.Name = "mediaInfoToolStripMenuItem";
            this.mediaInfoToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.mediaInfoToolStripMenuItem.Text = "Media Info";
            this.mediaInfoToolStripMenuItem.Click += new System.EventHandler(this.mediaInfoToolStripMenuItem_Click);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
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
            this.menu.ResumeLayout(false);
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
        private System.Windows.Forms.ContextMenuStrip menu;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fileListToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem subtitlesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem recentToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem mediaInfoToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
    }
}
