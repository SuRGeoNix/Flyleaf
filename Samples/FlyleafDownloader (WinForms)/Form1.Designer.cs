
namespace FlyleafDownloader
{
    partial class Form1
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.txtUrl = new System.Windows.Forms.TextBox();
            this.btnDownload = new System.Windows.Forms.Button();
            this.btnInputs = new System.Windows.Forms.Button();
            this.lstVideoInputs = new System.Windows.Forms.ListView();
            this.columnHeader6 = new System.Windows.Forms.ColumnHeader();
            this.lstAudioInputs = new System.Windows.Forms.ListView();
            this.columnHeader2 = new System.Windows.Forms.ColumnHeader();
            this.lstVideoStreams = new System.Windows.Forms.ListView();
            this.columnHeader3 = new System.Windows.Forms.ColumnHeader();
            this.lstAudioStreams = new System.Windows.Forms.ListView();
            this.columnHeader4 = new System.Windows.Forms.ColumnHeader();
            this.lstAudioStreams2 = new System.Windows.Forms.ListView();
            this.columnHeader5 = new System.Windows.Forms.ColumnHeader();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.splitContainer11 = new System.Windows.Forms.SplitContainer();
            this.lblCurTime = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtSavePath = new System.Windows.Forms.TextBox();
            this.lblPercent = new System.Windows.Forms.Label();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.btnStop = new System.Windows.Forms.Button();
            this.splitContainer2 = new System.Windows.Forms.SplitContainer();
            this.splitContainer3 = new System.Windows.Forms.SplitContainer();
            this.splitContainer6 = new System.Windows.Forms.SplitContainer();
            this.chkDefaultAudio = new System.Windows.Forms.CheckBox();
            this.splitContainer4 = new System.Windows.Forms.SplitContainer();
            this.splitContainer10 = new System.Windows.Forms.SplitContainer();
            this.splitContainer9 = new System.Windows.Forms.SplitContainer();
            this.splitContainer5 = new System.Windows.Forms.SplitContainer();
            this.splitContainer7 = new System.Windows.Forms.SplitContainer();
            this.splitContainer8 = new System.Windows.Forms.SplitContainer();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer11)).BeginInit();
            this.splitContainer11.Panel1.SuspendLayout();
            this.splitContainer11.Panel2.SuspendLayout();
            this.splitContainer11.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).BeginInit();
            this.splitContainer2.Panel1.SuspendLayout();
            this.splitContainer2.Panel2.SuspendLayout();
            this.splitContainer2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer3)).BeginInit();
            this.splitContainer3.Panel1.SuspendLayout();
            this.splitContainer3.Panel2.SuspendLayout();
            this.splitContainer3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer6)).BeginInit();
            this.splitContainer6.Panel1.SuspendLayout();
            this.splitContainer6.Panel2.SuspendLayout();
            this.splitContainer6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer4)).BeginInit();
            this.splitContainer4.Panel1.SuspendLayout();
            this.splitContainer4.Panel2.SuspendLayout();
            this.splitContainer4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer10)).BeginInit();
            this.splitContainer10.Panel1.SuspendLayout();
            this.splitContainer10.Panel2.SuspendLayout();
            this.splitContainer10.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer9)).BeginInit();
            this.splitContainer9.Panel1.SuspendLayout();
            this.splitContainer9.Panel2.SuspendLayout();
            this.splitContainer9.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer5)).BeginInit();
            this.splitContainer5.Panel1.SuspendLayout();
            this.splitContainer5.Panel2.SuspendLayout();
            this.splitContainer5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer7)).BeginInit();
            this.splitContainer7.Panel1.SuspendLayout();
            this.splitContainer7.Panel2.SuspendLayout();
            this.splitContainer7.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer8)).BeginInit();
            this.splitContainer8.Panel1.SuspendLayout();
            this.splitContainer8.Panel2.SuspendLayout();
            this.splitContainer8.SuspendLayout();
            this.SuspendLayout();
            // 
            // txtUrl
            // 
            this.txtUrl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtUrl.Location = new System.Drawing.Point(32, 8);
            this.txtUrl.Name = "txtUrl";
            this.txtUrl.PlaceholderText = "Url";
            this.txtUrl.Size = new System.Drawing.Size(595, 23);
            this.txtUrl.TabIndex = 0;
            // 
            // btnDownload
            // 
            this.btnDownload.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDownload.Location = new System.Drawing.Point(458, 3);
            this.btnDownload.Name = "btnDownload";
            this.btnDownload.Size = new System.Drawing.Size(69, 23);
            this.btnDownload.TabIndex = 2;
            this.btnDownload.Text = "Download";
            this.btnDownload.UseVisualStyleBackColor = true;
            this.btnDownload.Click += new System.EventHandler(this.btnDownload_Click);
            // 
            // btnInputs
            // 
            this.btnInputs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInputs.Location = new System.Drawing.Point(533, 3);
            this.btnInputs.Name = "btnInputs";
            this.btnInputs.Size = new System.Drawing.Size(94, 23);
            this.btnInputs.TabIndex = 6;
            this.btnInputs.Text = "Advanced >>";
            this.btnInputs.UseVisualStyleBackColor = true;
            this.btnInputs.Click += new System.EventHandler(this.btnInputs_Click);
            // 
            // lstVideoInputs
            // 
            this.lstVideoInputs.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader6});
            this.lstVideoInputs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstVideoInputs.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lstVideoInputs.HideSelection = false;
            this.lstVideoInputs.LabelWrap = false;
            this.lstVideoInputs.Location = new System.Drawing.Point(0, 0);
            this.lstVideoInputs.MultiSelect = false;
            this.lstVideoInputs.Name = "lstVideoInputs";
            this.lstVideoInputs.ShowGroups = false;
            this.lstVideoInputs.Size = new System.Drawing.Size(337, 251);
            this.lstVideoInputs.TabIndex = 7;
            this.lstVideoInputs.UseCompatibleStateImageBehavior = false;
            this.lstVideoInputs.View = System.Windows.Forms.View.Details;
            this.lstVideoInputs.SelectedIndexChanged += new System.EventHandler(this.lstVideoInputs_SelectedIndexChanged);
            // 
            // columnHeader6
            // 
            this.columnHeader6.Width = 49;
            // 
            // lstAudioInputs
            // 
            this.lstAudioInputs.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader2});
            this.lstAudioInputs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstAudioInputs.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lstAudioInputs.HideSelection = false;
            this.lstAudioInputs.LabelWrap = false;
            this.lstAudioInputs.Location = new System.Drawing.Point(0, 0);
            this.lstAudioInputs.MultiSelect = false;
            this.lstAudioInputs.Name = "lstAudioInputs";
            this.lstAudioInputs.ShowGroups = false;
            this.lstAudioInputs.Size = new System.Drawing.Size(337, 175);
            this.lstAudioInputs.TabIndex = 8;
            this.lstAudioInputs.UseCompatibleStateImageBehavior = false;
            this.lstAudioInputs.View = System.Windows.Forms.View.Details;
            this.lstAudioInputs.SelectedIndexChanged += new System.EventHandler(this.lstAudioInputs_SelectedIndexChanged);
            // 
            // columnHeader2
            // 
            this.columnHeader2.Width = 25;
            // 
            // lstVideoStreams
            // 
            this.lstVideoStreams.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader3});
            this.lstVideoStreams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstVideoStreams.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lstVideoStreams.HideSelection = false;
            this.lstVideoStreams.LabelWrap = false;
            this.lstVideoStreams.Location = new System.Drawing.Point(0, 0);
            this.lstVideoStreams.Name = "lstVideoStreams";
            this.lstVideoStreams.ShowGroups = false;
            this.lstVideoStreams.Size = new System.Drawing.Size(328, 107);
            this.lstVideoStreams.TabIndex = 9;
            this.lstVideoStreams.UseCompatibleStateImageBehavior = false;
            this.lstVideoStreams.View = System.Windows.Forms.View.Details;
            // 
            // columnHeader3
            // 
            this.columnHeader3.Width = 25;
            // 
            // lstAudioStreams
            // 
            this.lstAudioStreams.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader4});
            this.lstAudioStreams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstAudioStreams.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lstAudioStreams.HideSelection = false;
            this.lstAudioStreams.LabelWrap = false;
            this.lstAudioStreams.Location = new System.Drawing.Point(0, 0);
            this.lstAudioStreams.Name = "lstAudioStreams";
            this.lstAudioStreams.ShowGroups = false;
            this.lstAudioStreams.Size = new System.Drawing.Size(328, 106);
            this.lstAudioStreams.TabIndex = 10;
            this.lstAudioStreams.UseCompatibleStateImageBehavior = false;
            this.lstAudioStreams.View = System.Windows.Forms.View.Details;
            this.lstAudioStreams.SelectedIndexChanged += new System.EventHandler(this.lstAudioStreams_SelectedIndexChanged);
            // 
            // columnHeader4
            // 
            this.columnHeader4.Width = 25;
            // 
            // lstAudioStreams2
            // 
            this.lstAudioStreams2.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader5});
            this.lstAudioStreams2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstAudioStreams2.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lstAudioStreams2.HideSelection = false;
            this.lstAudioStreams2.LabelWrap = false;
            this.lstAudioStreams2.Location = new System.Drawing.Point(0, 0);
            this.lstAudioStreams2.Name = "lstAudioStreams2";
            this.lstAudioStreams2.ShowGroups = false;
            this.lstAudioStreams2.Size = new System.Drawing.Size(328, 175);
            this.lstAudioStreams2.TabIndex = 12;
            this.lstAudioStreams2.UseCompatibleStateImageBehavior = false;
            this.lstAudioStreams2.View = System.Windows.Forms.View.Details;
            this.lstAudioStreams2.SelectedIndexChanged += new System.EventHandler(this.lstAudioStreams2_SelectedIndexChanged);
            // 
            // columnHeader5
            // 
            this.columnHeader5.Width = 25;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(2, 10);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(73, 15);
            this.label3.TabIndex = 13;
            this.label3.Text = "Video Inputs";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(2, 10);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(75, 15);
            this.label4.TabIndex = 14;
            this.label4.Text = "Audio Inputs";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(2, 10);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(84, 15);
            this.label5.TabIndex = 15;
            this.label5.Text = "Audio Streams";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(2, 10);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(82, 15);
            this.label6.TabIndex = 16;
            this.label6.Text = "Video Streams";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(2, 10);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(84, 15);
            this.label7.TabIndex = 17;
            this.label7.Text = "Audio Streams";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer1.IsSplitterFixed = true;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.splitContainer11);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.splitContainer2);
            this.splitContainer1.Size = new System.Drawing.Size(669, 612);
            this.splitContainer1.SplitterDistance = 110;
            this.splitContainer1.TabIndex = 23;
            // 
            // splitContainer11
            // 
            this.splitContainer11.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer11.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer11.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer11.IsSplitterFixed = true;
            this.splitContainer11.Location = new System.Drawing.Point(0, 0);
            this.splitContainer11.Name = "splitContainer11";
            this.splitContainer11.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer11.Panel1
            // 
            this.splitContainer11.Panel1.Controls.Add(this.lblCurTime);
            this.splitContainer11.Panel1.Controls.Add(this.lblStatus);
            this.splitContainer11.Panel1.Controls.Add(this.btnBrowse);
            this.splitContainer11.Panel1.Controls.Add(this.txtSavePath);
            this.splitContainer11.Panel1.Controls.Add(this.txtUrl);
            // 
            // splitContainer11.Panel2
            // 
            this.splitContainer11.Panel2.Controls.Add(this.lblPercent);
            this.splitContainer11.Panel2.Controls.Add(this.progressBar1);
            this.splitContainer11.Panel2.Controls.Add(this.btnStop);
            this.splitContainer11.Panel2.Controls.Add(this.btnDownload);
            this.splitContainer11.Panel2.Controls.Add(this.btnInputs);
            this.splitContainer11.Size = new System.Drawing.Size(669, 110);
            this.splitContainer11.SplitterDistance = 75;
            this.splitContainer11.TabIndex = 23;
            // 
            // lblCurTime
            // 
            this.lblCurTime.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblCurTime.Location = new System.Drawing.Point(478, 56);
            this.lblCurTime.Name = "lblCurTime";
            this.lblCurTime.Size = new System.Drawing.Size(149, 15);
            this.lblCurTime.TabIndex = 9;
            this.lblCurTime.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblStatus
            // 
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.Location = new System.Drawing.Point(458, 41);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(169, 15);
            this.lblStatus.TabIndex = 10;
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowse.Location = new System.Drawing.Point(383, 37);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(69, 23);
            this.btnBrowse.TabIndex = 10;
            this.btnBrowse.Text = "Browse ...";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // txtSavePath
            // 
            this.txtSavePath.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSavePath.Location = new System.Drawing.Point(32, 37);
            this.txtSavePath.Name = "txtSavePath";
            this.txtSavePath.PlaceholderText = "Save Folder";
            this.txtSavePath.Size = new System.Drawing.Size(345, 23);
            this.txtSavePath.TabIndex = 1;
            // 
            // lblPercent
            // 
            this.lblPercent.AutoSize = true;
            this.lblPercent.Location = new System.Drawing.Point(194, 6);
            this.lblPercent.Name = "lblPercent";
            this.lblPercent.Size = new System.Drawing.Size(0, 15);
            this.lblPercent.TabIndex = 9;
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(32, 2);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(345, 23);
            this.progressBar1.TabIndex = 8;
            this.progressBar1.Visible = false;
            // 
            // btnStop
            // 
            this.btnStop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(383, 3);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(69, 23);
            this.btnStop.TabIndex = 7;
            this.btnStop.Text = "Close";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // splitContainer2
            // 
            this.splitContainer2.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer2.IsSplitterFixed = true;
            this.splitContainer2.Location = new System.Drawing.Point(0, 0);
            this.splitContainer2.Name = "splitContainer2";
            this.splitContainer2.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            this.splitContainer2.Panel1.Controls.Add(this.splitContainer3);
            // 
            // splitContainer2.Panel2
            // 
            this.splitContainer2.Panel2.Controls.Add(this.splitContainer5);
            this.splitContainer2.Size = new System.Drawing.Size(669, 498);
            this.splitContainer2.SplitterDistance = 285;
            this.splitContainer2.TabIndex = 0;
            // 
            // splitContainer3
            // 
            this.splitContainer3.Cursor = System.Windows.Forms.Cursors.VSplit;
            this.splitContainer3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer3.IsSplitterFixed = true;
            this.splitContainer3.Location = new System.Drawing.Point(0, 0);
            this.splitContainer3.Name = "splitContainer3";
            // 
            // splitContainer3.Panel1
            // 
            this.splitContainer3.Panel1.Controls.Add(this.splitContainer6);
            // 
            // splitContainer3.Panel2
            // 
            this.splitContainer3.Panel2.Controls.Add(this.splitContainer4);
            this.splitContainer3.Size = new System.Drawing.Size(669, 285);
            this.splitContainer3.SplitterDistance = 337;
            this.splitContainer3.TabIndex = 0;
            // 
            // splitContainer6
            // 
            this.splitContainer6.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer6.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer6.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer6.IsSplitterFixed = true;
            this.splitContainer6.Location = new System.Drawing.Point(0, 0);
            this.splitContainer6.Name = "splitContainer6";
            this.splitContainer6.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer6.Panel1
            // 
            this.splitContainer6.Panel1.Controls.Add(this.chkDefaultAudio);
            this.splitContainer6.Panel1.Controls.Add(this.label3);
            this.splitContainer6.Panel1MinSize = 30;
            // 
            // splitContainer6.Panel2
            // 
            this.splitContainer6.Panel2.Controls.Add(this.lstVideoInputs);
            this.splitContainer6.Size = new System.Drawing.Size(337, 285);
            this.splitContainer6.SplitterDistance = 30;
            this.splitContainer6.TabIndex = 0;
            // 
            // chkDefaultAudio
            // 
            this.chkDefaultAudio.AutoSize = true;
            this.chkDefaultAudio.Checked = true;
            this.chkDefaultAudio.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkDefaultAudio.Location = new System.Drawing.Point(81, 9);
            this.chkDefaultAudio.Name = "chkDefaultAudio";
            this.chkDefaultAudio.Size = new System.Drawing.Size(118, 19);
            this.chkDefaultAudio.TabIndex = 20;
            this.chkDefaultAudio.Text = "Use default audio";
            this.chkDefaultAudio.UseVisualStyleBackColor = true;
            // 
            // splitContainer4
            // 
            this.splitContainer4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer4.IsSplitterFixed = true;
            this.splitContainer4.Location = new System.Drawing.Point(0, 0);
            this.splitContainer4.Name = "splitContainer4";
            this.splitContainer4.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer4.Panel1
            // 
            this.splitContainer4.Panel1.Controls.Add(this.splitContainer10);
            // 
            // splitContainer4.Panel2
            // 
            this.splitContainer4.Panel2.Controls.Add(this.splitContainer9);
            this.splitContainer4.Size = new System.Drawing.Size(328, 285);
            this.splitContainer4.SplitterDistance = 141;
            this.splitContainer4.TabIndex = 0;
            // 
            // splitContainer10
            // 
            this.splitContainer10.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer10.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer10.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer10.IsSplitterFixed = true;
            this.splitContainer10.Location = new System.Drawing.Point(0, 0);
            this.splitContainer10.Name = "splitContainer10";
            this.splitContainer10.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer10.Panel1
            // 
            this.splitContainer10.Panel1.Controls.Add(this.label6);
            // 
            // splitContainer10.Panel2
            // 
            this.splitContainer10.Panel2.Controls.Add(this.lstVideoStreams);
            this.splitContainer10.Size = new System.Drawing.Size(328, 141);
            this.splitContainer10.SplitterDistance = 30;
            this.splitContainer10.TabIndex = 1;
            // 
            // splitContainer9
            // 
            this.splitContainer9.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer9.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer9.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer9.IsSplitterFixed = true;
            this.splitContainer9.Location = new System.Drawing.Point(0, 0);
            this.splitContainer9.Name = "splitContainer9";
            this.splitContainer9.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer9.Panel1
            // 
            this.splitContainer9.Panel1.Controls.Add(this.label5);
            // 
            // splitContainer9.Panel2
            // 
            this.splitContainer9.Panel2.Controls.Add(this.lstAudioStreams);
            this.splitContainer9.Size = new System.Drawing.Size(328, 140);
            this.splitContainer9.SplitterDistance = 30;
            this.splitContainer9.TabIndex = 1;
            // 
            // splitContainer5
            // 
            this.splitContainer5.Cursor = System.Windows.Forms.Cursors.VSplit;
            this.splitContainer5.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer5.IsSplitterFixed = true;
            this.splitContainer5.Location = new System.Drawing.Point(0, 0);
            this.splitContainer5.Name = "splitContainer5";
            // 
            // splitContainer5.Panel1
            // 
            this.splitContainer5.Panel1.Controls.Add(this.splitContainer7);
            // 
            // splitContainer5.Panel2
            // 
            this.splitContainer5.Panel2.Controls.Add(this.splitContainer8);
            this.splitContainer5.Size = new System.Drawing.Size(669, 209);
            this.splitContainer5.SplitterDistance = 337;
            this.splitContainer5.TabIndex = 0;
            // 
            // splitContainer7
            // 
            this.splitContainer7.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer7.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer7.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer7.IsSplitterFixed = true;
            this.splitContainer7.Location = new System.Drawing.Point(0, 0);
            this.splitContainer7.Name = "splitContainer7";
            this.splitContainer7.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer7.Panel1
            // 
            this.splitContainer7.Panel1.Controls.Add(this.label4);
            // 
            // splitContainer7.Panel2
            // 
            this.splitContainer7.Panel2.Controls.Add(this.lstAudioInputs);
            this.splitContainer7.Size = new System.Drawing.Size(337, 209);
            this.splitContainer7.SplitterDistance = 30;
            this.splitContainer7.TabIndex = 1;
            // 
            // splitContainer8
            // 
            this.splitContainer8.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitContainer8.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer8.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer8.IsSplitterFixed = true;
            this.splitContainer8.Location = new System.Drawing.Point(0, 0);
            this.splitContainer8.Name = "splitContainer8";
            this.splitContainer8.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer8.Panel1
            // 
            this.splitContainer8.Panel1.Controls.Add(this.label7);
            // 
            // splitContainer8.Panel2
            // 
            this.splitContainer8.Panel2.Controls.Add(this.lstAudioStreams2);
            this.splitContainer8.Size = new System.Drawing.Size(328, 209);
            this.splitContainer8.SplitterDistance = 30;
            this.splitContainer8.TabIndex = 1;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(669, 612);
            this.Controls.Add(this.splitContainer1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Flyleaf Downloader";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.splitContainer11.Panel1.ResumeLayout(false);
            this.splitContainer11.Panel1.PerformLayout();
            this.splitContainer11.Panel2.ResumeLayout(false);
            this.splitContainer11.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer11)).EndInit();
            this.splitContainer11.ResumeLayout(false);
            this.splitContainer2.Panel1.ResumeLayout(false);
            this.splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).EndInit();
            this.splitContainer2.ResumeLayout(false);
            this.splitContainer3.Panel1.ResumeLayout(false);
            this.splitContainer3.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer3)).EndInit();
            this.splitContainer3.ResumeLayout(false);
            this.splitContainer6.Panel1.ResumeLayout(false);
            this.splitContainer6.Panel1.PerformLayout();
            this.splitContainer6.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer6)).EndInit();
            this.splitContainer6.ResumeLayout(false);
            this.splitContainer4.Panel1.ResumeLayout(false);
            this.splitContainer4.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer4)).EndInit();
            this.splitContainer4.ResumeLayout(false);
            this.splitContainer10.Panel1.ResumeLayout(false);
            this.splitContainer10.Panel1.PerformLayout();
            this.splitContainer10.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer10)).EndInit();
            this.splitContainer10.ResumeLayout(false);
            this.splitContainer9.Panel1.ResumeLayout(false);
            this.splitContainer9.Panel1.PerformLayout();
            this.splitContainer9.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer9)).EndInit();
            this.splitContainer9.ResumeLayout(false);
            this.splitContainer5.Panel1.ResumeLayout(false);
            this.splitContainer5.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer5)).EndInit();
            this.splitContainer5.ResumeLayout(false);
            this.splitContainer7.Panel1.ResumeLayout(false);
            this.splitContainer7.Panel1.PerformLayout();
            this.splitContainer7.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer7)).EndInit();
            this.splitContainer7.ResumeLayout(false);
            this.splitContainer8.Panel1.ResumeLayout(false);
            this.splitContainer8.Panel1.PerformLayout();
            this.splitContainer8.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer8)).EndInit();
            this.splitContainer8.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TextBox txtUrl;
        private System.Windows.Forms.Button btnDownload;
        private System.Windows.Forms.Button btnInputs;
        private System.Windows.Forms.ListView lstVideoInputs;
        private System.Windows.Forms.ListView lstAudioInputs;
        private System.Windows.Forms.ColumnHeader columnHeader2;
        private System.Windows.Forms.ListView lstVideoStreams;
        private System.Windows.Forms.ColumnHeader columnHeader3;
        private System.Windows.Forms.ListView lstAudioStreams;
        private System.Windows.Forms.ColumnHeader columnHeader4;
        private System.Windows.Forms.ListView lstAudioStreams2;
        private System.Windows.Forms.ColumnHeader columnHeader5;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.ColumnHeader columnHeader6;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.SplitContainer splitContainer2;
        private System.Windows.Forms.SplitContainer splitContainer3;
        private System.Windows.Forms.SplitContainer splitContainer4;
        private System.Windows.Forms.SplitContainer splitContainer5;
        private System.Windows.Forms.SplitContainer splitContainer6;
        private System.Windows.Forms.SplitContainer splitContainer10;
        private System.Windows.Forms.SplitContainer splitContainer9;
        private System.Windows.Forms.SplitContainer splitContainer7;
        private System.Windows.Forms.SplitContainer splitContainer8;
        private System.Windows.Forms.SplitContainer splitContainer11;
        private System.Windows.Forms.Label lblCurTime;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.CheckBox chkDefaultAudio;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.TextBox txtSavePath;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblPercent;
    }
}

