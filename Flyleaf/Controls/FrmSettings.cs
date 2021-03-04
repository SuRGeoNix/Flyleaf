using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SuRGeoNix.Flyleaf.Controls
{
    public partial class FrmSettings : Form
    {
        internal MediaRouter  player;
        FrmLanguages frmLanguages;

        SettingsLoad systemDefault;
        SettingsLoad userDefault;
        SettingsLoad currentLoad;
        Settings     current;

        SettingsLoad selectedLoad;

        ColorDialog         colorDialog = new ColorDialog();
        FontDialog          fontDialog  = new FontDialog();

        ColorConverter      colorConv   = new ColorConverter();
        FontConverter       fontConv    = new FontConverter();
        PaddingConverter    paddConv    = new PaddingConverter();
        PointConverter      pointConv   = new PointConverter();

        public FrmSettings(SettingsLoad system, SettingsLoad user, Settings current)
        {
            frmLanguages = new FrmLanguages();

            systemDefault   = system;
            userDefault     = user;
            this.current    = current;

            InitializeComponent();
            
            Resize          += FrmSettings_Resize;
            FormClosing     += FrmSettings_FormClosing;
            VisibleChanged  += FrmSettings_VisibleChanged;
            frmLanguages.VisibleChanged += FrmLanguages_VisibleChanged;
            KeyPreview = true;
            KeyPress += FrmSettings_KeyPress;

            cmbSettings.SelectedIndexChanged    += cmbSettings_SelectedIndexChanged;
            subsEnabled.CheckedChanged          += subsEnabled_CheckedChanged;
            torEnabled.CheckedChanged           += torEnabled_CheckedChanged;
            torSleepMode.CheckedChanged         += torSleepMode_CheckedChanged;
            ShutdownOnFinish.CheckedChanged     += ShutdownOnFinish_CheckedChanged;
            KeepHistory.CheckedChanged          += KeepHistory_CheckedChanged;
            subsRectEnabled.CheckedChanged      += subsRectEnabled_CheckedChanged;
            subsSurface.SelectedIndexChanged    += subsSurface_SelectedIndexChanged;
            surfRectEnabled.CheckedChanged      += surfRectEnabled_CheckedChanged;
            surf.SelectedIndexChanged           += surf_SelectedIndexChanged;
            msg.SelectedIndexChanged            += msg_SelectedIndexChanged;
            btnClearHistory.Click               += btnClearHistory_Click;
            btnLanguages.Click                  += btnLanguages_Click;
            btnSurfCopyAll.Click                += btnSurfCopyAll_Click;
            btnTorDownloadPath.Click            += btnTorDownloadPath_Click;
            btnTorDownloadTemp.Click            += btnTorDownloadTemp_Click;
            btnTorRecalculate.Click             += btnTorRecalculate_Click;

            ClearBackColor. Validating          += (o, e) => { ValidateColor    (ClearBackColor ); };
            ClearBackColor. MouseDoubleClick    += (o, e) => { ShowColorDialog  (ClearBackColor ); };

            subsColor.      Validating          += (o, e) => { ValidateColor    (subsColor      ); };
            subsRectColor.  Validating          += (o, e) => { ValidateColor    (subsRectColor  ); };
            subsPosition.   Validating          += (o, e) => { ValidateSize     (subsPosition); };
            subsRectPadding.Validating          += (o, e) => { ValidatePadding  (subsRectPadding); };
            subsFont.       Validating          += (o, e) => { ValidateFont     (subsFont       ); };

            subsFont.       MouseDoubleClick    += (o, e) => { ShowFontDialog   (subsFont       ); };
            subsColor.      MouseDoubleClick    += (o, e) => { ShowColorDialog  (subsColor      ); };
            subsRectColor.  MouseDoubleClick    += (o, e) => { ShowColorDialog  (subsRectColor  ); };

            surfColor.      Validating          += (o, e) => { ValidateColor    (surfColor      ); };
            surfRectColor.  Validating          += (o, e) => { ValidateColor    (surfRectColor  ); };
            surfPosition.   Validating          += (o, e) => { ValidateSize     (surfPosition); };
            surfRectPadding.Validating          += (o, e) => { ValidatePadding  (surfRectPadding); };
            surfFont.       Validating          += (o, e) => { ValidateFont     (surfFont       ); };

            surfColor.      MouseDoubleClick    += (o, e) => { ShowColorDialog  (surfColor      ); };
            surfRectColor.  MouseDoubleClick    += (o, e) => { ShowColorDialog  (surfRectColor  ); };
            surfFont.       MouseDoubleClick    += (o, e) => { ShowFontDialog   (surfFont       ); };

            subsDownload.   Items.AddRange(Enum.GetNames(typeof(MediaRouter.DownloadSubsMode)));
            subsSurface.    Items.AddRange(Enum.GetNames(typeof(Settings.OSDSurfaces)));
            surf.           Items.AddRange(Enum.GetNames(typeof(Settings.OSDSurfaces)));
            msgSurf.        Items.AddRange(Enum.GetNames(typeof(Settings.OSDSurfaces)));
            msg.            Items.AddRange(Enum.GetNames(typeof(OSDMessage.Type)));
            msgVisibility.  Items.AddRange(Enum.GetNames(typeof(MediaRouter.VisibilityMode)));

            (new ToolTip()).SetToolTip(ShutdownAfterIdle, "Will shutdown in X seconds if you dont do anything");
            (new ToolTip()).SetToolTip(btnSurfCopyAll, "Excludes position field & subtitles surface");
            (new ToolTip()).SetToolTip(torSleepMode, "Power save mode for torrent streaming");
            (new ToolTip()).SetToolTip(torSleepModeAutoCustom, "Auto or Custom numeric value such as 2048");

            (new ToolTip()).SetToolTip(ClearBackColor, "Double Click for Palette");
            (new ToolTip()).SetToolTip(subsColor, "Double Click for Palette");
            (new ToolTip()).SetToolTip(surfColor, "Double Click for Palette");
            (new ToolTip()).SetToolTip(surfRectColor, "Double Click for Palette");
            (new ToolTip()).SetToolTip(subsFont, "Double Click for Font Selection");
            (new ToolTip()).SetToolTip(surfFont, "Double Click for Font Selection");

            FrmSettings_Resize(this, null);
        }

        private void btnTorRecalculate_Click(object sender, EventArgs e)
        {
            lblTorCacheSize.Text        = $"Size: {Utils.BytesToReadableString_(Utils.GetDirectorySize(torDownloadPath.Text))}";
            lblTorCacheSizeTemp.Text    = $"Temp: {Utils.BytesToReadableString_(Utils.GetDirectorySize(torDownloadTemp.Text))}";
        }

        private void FrmSettings_Resize(object sender, EventArgs e)
        {
            tabControl1.Height = ClientSize.Height - tableLayoutPanel1.Height;
            cmbSettings.Location = new Point(ClientSize.Width - cmbSettings.Width - 10, cmbSettings.Location.Y);
        }
        private void FrmSettings_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
        private void FrmSettings_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char) Keys.Enter)
            {
                
                btnApply_Click(sender, e);
                e.Handled = true;
            }
        }
        private void FrmSettings_VisibleChanged(object sender, EventArgs e)
        {
            if (!Visible) return;

            currentLoad = new SettingsLoad();
            Settings.ParseSettings(current, currentLoad);
            cmbSettings.Text = "Current";
            cmbSettings_SelectedIndexChanged(sender, e);
            btnTorRecalculate_Click(this, null);
        }
        private void FrmLanguages_VisibleChanged(object sender, EventArgs e)
        {
            if (frmLanguages.Visible) return;

            txtLangs.Text = String.Join(",", frmLanguages.Languages);
        }

        private void ValidateColor(TextBox source)
        {
            try
            {
                var tmp = (Color) colorConv.ConvertFromString(source.Text);
                source.BackColor = Color.White;
            } catch (Exception) { source.BackColor = Color.Red; }
        }
        private void ValidateFont(TextBox source)
        {
            try
            {
                var tmp = (Font) fontConv.ConvertFromString(source.Text);
                source.BackColor = Color.White;
            } catch (Exception) { source.BackColor = Color.Red; }
        }
        private void ValidatePadding(TextBox source)
        {
            try
            {
                var tmp = (Padding) paddConv.ConvertFromString(source.Text);
                source.BackColor = Color.White;
            } catch (Exception) { source.BackColor = Color.Red; }
        }
        private void ValidateSize(TextBox source)
        {
            try
            {
                var tmp = (Point) pointConv.ConvertFromString(source.Text);
                source.BackColor = Color.White;
            } catch (Exception) { source.BackColor = Color.Red; }
        }

        private void ShowFontDialog(TextBox target)
        {
            try
            {
                fontDialog.Font = (Font) fontConv.ConvertFromString(target.Text);
            } catch (Exception e) { MessageBox.Show(e.Message); }

            if (fontDialog.ShowDialog() == DialogResult.OK) target.Text = fontConv.ConvertToString(fontDialog.Font);
        }
        private void ShowColorDialog(TextBox target)
        {
            try
            {
                colorDialog.Color = (Color) colorConv.ConvertFromString(target.Text);
            } catch (Exception e) { MessageBox.Show(e.Message); }

            if (colorDialog.ShowDialog() == DialogResult.OK) target.Text = colorConv.ConvertToString(colorDialog.Color);
        }

        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            player.History.Clear();
        }
        private void btnTorDownloadPath_Click(object sender, EventArgs e)
        {
            using(var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    torDownloadPath.Text = fbd.SelectedPath;
            }
        }
        private void btnTorDownloadTemp_Click(object sender, EventArgs e)
        {
            using(var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    torDownloadTemp.Text = fbd.SelectedPath;
            }
        }
        private void torEnabled_CheckedChanged(object sender, EventArgs e)
        {
            groupBox11.Enabled  = torEnabled.Checked;
            groupBox9.Enabled   = torEnabled.Checked;
        }
        private void torSleepMode_CheckedChanged(object sender, EventArgs e)
        {
            torSleepModeAutoCustom.Enabled = torSleepMode.Checked;
        }

        private void subsEnabled_CheckedChanged(object sender, EventArgs e)
        {
            groupBox3.Enabled   = subsEnabled.Checked;
            groupBox4.Enabled   = subsEnabled.Checked;
        }
        private void subsRectEnabled_CheckedChanged(object sender, EventArgs e)
        {
            groupBox5.Enabled = subsRectEnabled.Checked;
        }
        private void ShutdownOnFinish_CheckedChanged(object sender, EventArgs e)
        {
            ShutdownAfterIdle.Enabled = ShutdownOnFinish.Checked;
        }
        private void KeepHistory_CheckedChanged(object sender, EventArgs e)
        {
            KeepHistoryEntries.Enabled = KeepHistory.Checked;
        }
        private void cmbSettings_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (cmbSettings.Text)
            {
                case "System Default":
                    selectedLoad = systemDefault;
                    btnApply.Text = "Load";
                    btnSurfCopyAll.Enabled = false;
                    btnSave.Enabled = false;
                    btnLanguages.Enabled = false;
                    break;
                case "User Default":
                    selectedLoad = userDefault;
                    btnApply.Text = "Load";
                    btnSurfCopyAll.Enabled = false;
                    btnSave.Enabled = false;
                    btnLanguages.Enabled = false;
                    break;
                case "Current":
                    selectedLoad = currentLoad;
                    btnApply.Text = "Apply";
                    btnSurfCopyAll.Enabled = true;
                    btnSave.Enabled = true;
                    btnLanguages.Enabled = true;
                    break;
            }

            FillSettingsToForm(selectedLoad);
            txtLangs.Text = String.Join(",", frmLanguages.Languages);
        }
        private void subsSurface_SelectedIndexChanged(object sender, EventArgs e)
        {
            FillSubsSurfaceToForm(selectedLoad, subsSurface.Text);
        }
        private void surf_SelectedIndexChanged(object sender, EventArgs e)
        {
            FillSurfaceToForm(selectedLoad, surf.Text);
        }
        private void surfRectEnabled_CheckedChanged(object sender, EventArgs e)
        {
            groupBox7.Enabled = surfRectEnabled.Checked;
        }
        private void msg_SelectedIndexChanged(object sender, EventArgs e)
        {
            FillMessageToForm(selectedLoad, msg.Text);
        }

        private void FillSettingsToForm(SettingsLoad load)
        {
            ClearBackColor.Text         = load.main.ClearBackColor2;
            IdleTimeout.Text            = load.main.IdleTimeout.ToString();
            SeekOnSlide.Checked         = load.bar.SeekOnSlide;

            ShutdownOnFinish.Checked    = !load.main.ShutdownOnFinish;
            ShutdownOnFinish.Checked    = load.main.ShutdownOnFinish;

            ShutdownAfterIdle.Text      = load.main.ShutdownAfterIdle.ToString();

            KeepHistory.Checked         = !load.main.HistoryEnabled;
            KeepHistory.Checked         = load.main.HistoryEnabled;
            KeepHistoryEntries.Text     = load.main.HistoryEntries.ToString();

            audioEnabled.Checked        = !load.audio._Enabled;
            audioEnabled.Checked        = load.audio._Enabled;

            subsEnabled.Checked         = !load.subtitles._Enabled;
            subsEnabled.Checked         = load.subtitles._Enabled;
            frmLanguages.Languages      = load.subtitles.Languages;
            subsDownload.Text           = load.subtitles.DownloadSubs.ToString();

            surf.Text                   = load.messages[0].Surface.ToString();
            FillSurfaceToForm            (load, surf.Text);
            subsSurface.Text            = load.messages[(int) OSDMessage.Type.Subtitles].Surface.ToString();
            FillSubsSurfaceToForm        (load, subsSurface.Text);

            msg.Text                    = OSDMessage.Type.Time.ToString();
            FillMessageToForm            (load, msg.Text);

            AudioDelayStep.Text         = load.keys.AudioDelayStep.ToString();
            AudioDelayStep2.Text        = load.keys.AudioDelayStep2.ToString();
            SubsDelayStep.Text          = load.keys.SubsDelayStep.ToString();
            SubsDelayStep2.Text         = load.keys.SubsDelayStep2.ToString();
            VolStep.Text                = load.keys.VolStep.ToString();
            SubsPosYStep.Text           = load.keys.SubsPosYStep.ToString();
            SubsFontSizeStep.Text       = load.keys.SubsFontSizeStep.ToString();
            SeekStep.Text               = load.keys.SeekStep.ToString();
            SeekStepFirst.Text          = load.keys.SeekStepFirst.ToString();

            videoHA.Checked             = load.video.HardwareAcceleration;
            videoQueueMinSize.Text      = load.video.QueueMinSize.ToString();
            videoQueueMaxSize.Text      = load.video.QueueMaxSize.ToString();
            videoVSync.Checked          = load.video.VSync;
            videoThreads.Text           = load.video.DecoderThreads.ToString();
            videoAspectRatio.Text       = load.video.AspectRatio == MediaRouter.ViewPorts.CUSTOM ? load.video.CustomRatio.ToString() : load.video.AspectRatio.ToString();

            torEnabled.Checked          = !load.torrent._Enabled;
            torEnabled.Checked          = load.torrent._Enabled;
            torDownloadNext.Checked     = load.torrent.DownloadNext;
            torBufferDuration.Text      = load.torrent.BufferDuration.ToString();
            torDownloadPath.Text        = load.torrent.DownloadPath.ToString();
            torDownloadTemp.Text        = load.torrent.DownloadTemp.ToString();

            torSleepMode.Checked        = !torSleepMode.Checked;

            if (load.torrent.SleepMode == 0)
            {
                torSleepMode.Checked = false;
                torSleepModeAutoCustom.Text = "";
                torSleepModeAutoCustom.Enabled = false;
            }
            else
            {
                torSleepMode.Checked = true;
                torSleepModeAutoCustom.Enabled = true;
                torSleepModeAutoCustom.Text = load.torrent.SleepMode == -1 ? "Auto" : load.torrent.SleepMode.ToString();
            }

            torMinThreads.Text          = load.torrent.MinThreads.ToString();
            torMaxThreads.Text          = load.torrent.MaxThreads.ToString();
            torBlockRequests.Text       = load.torrent.BlockRequests.ToString();

            torTimeoutGlobal.Text       = load.torrent.TimeoutGlobal.ToString();
            torRetriesGlobal.Text       = load.torrent.RetriesGlobal.ToString();
            torTimeoutBuffer.Text       = load.torrent.TimeoutBuffer.ToString();
            torRetriesBuffer.Text       = load.torrent.RetriesBuffer.ToString();
        }
        private void FillFormToSettings(SettingsLoad load, bool surfCopyAll = false)
        {
            load.main.ClearBackColor2       = ClearBackColor.Text;
            load.main.IdleTimeout           = int.Parse(IdleTimeout.Text);
            load.bar.SeekOnSlide            = SeekOnSlide.Checked;
            load.main.ShutdownOnFinish      = ShutdownOnFinish.Checked;
            load.main.ShutdownAfterIdle     = int.Parse(ShutdownAfterIdle.Text);

            load.main.HistoryEnabled        = KeepHistory.Checked;
            load.main.HistoryEntries        = int.Parse(KeepHistoryEntries.Text);

            load.audio._Enabled             = audioEnabled.Checked;

            load.subtitles._Enabled         = subsEnabled.Checked;
            load.subtitles.DownloadSubs     = (MediaRouter.DownloadSubsMode) Enum.Parse(typeof(MediaRouter.DownloadSubsMode), subsDownload.Text);
            load.subtitles.Languages        = frmLanguages.Languages;

            int surfIndex                   = (int) Enum.Parse(typeof(Settings.OSDSurfaces), surf.Text);
            FillFormToSurface(load, surfIndex);

            if (surfCopyAll)
            {
                for (int i=0; i<load.surfaces.Length; i++)
                    if (i != surfIndex) FillFormToSurface(load, i, true);
            }

            int msgIndex                        = (int) Enum.Parse(typeof(OSDMessage.Type), msg.Text);
            load.messages[msgIndex].Visibility  = (MediaRouter.VisibilityMode) Enum.Parse(typeof(MediaRouter.VisibilityMode), msgVisibility.Text);
            load.messages[msgIndex].Surface     = (Settings.OSDSurfaces) Enum.Parse(typeof(Settings.OSDSurfaces), msgSurf.Text);

            int subSurfaceIndex                         = (int) Enum.Parse(typeof(Settings.OSDSurfaces), subsSurface.Text);
            load.surfaces[subSurfaceIndex].Color2       = subsColor.Text;
            load.surfaces[subSurfaceIndex].Font2        = subsFont.Text;
            load.surfaces[subSurfaceIndex].OnViewPort   = subsOnViewPort.Checked;
            load.surfaces[subSurfaceIndex].RectColor2   = subsRectColor.Text;
            load.surfaces[subSurfaceIndex].RectEnabled  = subsRectEnabled.Checked;
            load.surfaces[subSurfaceIndex].RectPadding  = (Padding) paddConv.ConvertFromString(subsRectPadding.Text);
            load.surfaces[subSurfaceIndex].Position     = (Point) pointConv.ConvertFromString(subsPosition.Text);

            load.messages[(int) OSDMessage.Type.Subtitles].Surface = (Settings.OSDSurfaces) Enum.Parse(typeof(Settings.OSDSurfaces), subsSurface.Text);

            load.keys.AudioDelayStep        = int.Parse(AudioDelayStep.Text);
            load.keys.AudioDelayStep2       = int.Parse(AudioDelayStep2.Text);
            load.keys.SubsDelayStep         = int.Parse(SubsDelayStep.Text);
            load.keys.SubsDelayStep2        = int.Parse(SubsDelayStep2.Text);
            load.keys.VolStep               = int.Parse(VolStep.Text);
            load.keys.SubsPosYStep          = int.Parse(SubsPosYStep.Text);
            load.keys.SubsFontSizeStep      = int.Parse(SubsFontSizeStep.Text);
            load.keys.SeekStep              = int.Parse(SeekStep.Text);
            load.keys.SeekStepFirst         = int.Parse(SeekStepFirst.Text);

            load.video.HardwareAcceleration = videoHA.Checked;
            load.video.QueueMinSize         = int.Parse(videoQueueMinSize.Text);
            load.video.QueueMaxSize         = int.Parse(videoQueueMaxSize.Text);
            load.video.VSync                = videoVSync.Checked;
            load.video.DecoderThreads       = int.Parse(videoThreads.Text);
            if (videoAspectRatio.Text.ToLower() == "keep")
                load.video.AspectRatio      = MediaRouter.ViewPorts.KEEP;
            else if (videoAspectRatio.Text.ToLower() == "fill")
                load.video.AspectRatio      = MediaRouter.ViewPorts.FILL;
            else if (videoAspectRatio.Text.Contains("/") || videoAspectRatio.Text.Contains(":"))
            {
                load.video.AspectRatio      = MediaRouter.ViewPorts.CUSTOM;
                string[] flt = videoAspectRatio.Text.Split('/');
                if (flt.Length == 1)
                    flt = videoAspectRatio.Text.Split(':');
                load.video.CustomRatio      = float.Parse(flt[0]) / float.Parse(flt[1]);
            }
            else
            {
                load.video.AspectRatio      = MediaRouter.ViewPorts.CUSTOM;
                load.video.CustomRatio      = float.Parse(videoAspectRatio.Text);
            }
            
            load.torrent._Enabled           = torEnabled.Checked;
            load.torrent.DownloadNext       = torDownloadNext.Checked;
            load.torrent.BufferDuration     = int.Parse(torBufferDuration.Text);
            load.torrent.DownloadPath       = torDownloadPath.Text;
            load.torrent.DownloadTemp       = torDownloadTemp.Text;

            if (!torSleepMode.Checked)
                load.torrent.SleepMode = 0;
            else if (torSleepModeAutoCustom.Text.ToLower() == "auto")
                load.torrent.SleepMode = -1;
            else
                load.torrent.SleepMode      = int.Parse(torSleepModeAutoCustom.Text);

            load.torrent.MinThreads         = int.Parse(torMinThreads.Text);
            load.torrent.MaxThreads         = int.Parse(torMaxThreads.Text);
            load.torrent.BlockRequests      = int.Parse(torBlockRequests.Text);

            load.torrent.TimeoutGlobal      = int.Parse(torTimeoutGlobal.Text);
            load.torrent.RetriesGlobal      = int.Parse(torRetriesGlobal.Text);
            load.torrent.TimeoutBuffer      = int.Parse(torTimeoutBuffer.Text);
            load.torrent.RetriesBuffer      = int.Parse(torRetriesBuffer.Text);
        }
        private void FillSubsSurfaceToForm(SettingsLoad load, string surface)
        {
            int surfIndex               = (int) Enum.Parse(typeof(Settings.OSDSurfaces), surface);
            subsColor.Text              = load.surfaces[surfIndex].Color2;
            subsFont.Text               = load.surfaces[surfIndex].Font2;
            subsOnViewPort.Checked      = load.surfaces[surfIndex].OnViewPort;
            subsRectColor.Text          = load.surfaces[surfIndex].RectColor2;
            subsRectEnabled.Checked     = load.surfaces[surfIndex].RectEnabled;
            subsRectPadding.Text        = paddConv.ConvertToString(load.surfaces[surfIndex].RectPadding);
            subsPosition.Text           = pointConv.ConvertToString(load.surfaces[surfIndex].Position);
        }
        private void FillSurfaceToForm(SettingsLoad load, string surface)
        {
            int surfIndex               = (int) Enum.Parse(typeof(Settings.OSDSurfaces), surface);
            surfColor.Text              = load.surfaces[surfIndex].Color2;
            surfFont.Text               = load.surfaces[surfIndex].Font2;
            surfOnViewPort.Checked      = load.surfaces[surfIndex].OnViewPort;
            surfRectColor.Text          = load.surfaces[surfIndex].RectColor2;
            surfRectEnabled.Checked     = !load.surfaces[surfIndex].RectEnabled;
            surfRectEnabled.Checked     = load.surfaces[surfIndex].RectEnabled;
            surfRectPadding.Text        = paddConv.ConvertToString(load.surfaces[surfIndex].RectPadding);
            surfPosition.Text           = pointConv.ConvertToString(load.surfaces[surfIndex].Position);
        }
        private void FillMessageToForm(SettingsLoad load, string message)
        {
            int msgIndex        = (int) Enum.Parse(typeof(OSDMessage.Type), message);
            msgVisibility.Text  = load.messages[msgIndex].Visibility.ToString();
            msgSurf.Text        = load.messages[msgIndex].Surface.ToString();
        }
        private void FillFormToSurface(SettingsLoad load, int surfIndex, bool excludePosition = false)
        {
            //int surfIndex                         = (int) Enum.Parse(typeof(Settings.OSDSurfaces), surface);
            load.surfaces[surfIndex].Color2       = surfColor.Text;
            load.surfaces[surfIndex].Font2        = surfFont.Text;
            load.surfaces[surfIndex].OnViewPort   = surfOnViewPort.Checked;
            load.surfaces[surfIndex].RectColor2   = surfRectColor.Text;
            load.surfaces[surfIndex].RectEnabled  = surfRectEnabled.Checked;
            load.surfaces[surfIndex].RectPadding  = (Padding) paddConv.ConvertFromString(surfRectPadding.Text);
            if (!excludePosition)
            load.surfaces[surfIndex].Position     = (Point) pointConv.ConvertFromString(surfPosition.Text);
        }

        private void btnLanguages_Click(object sender, EventArgs e)
        {
            //Enabled = false;
            frmLanguages.ShowDialog(this);
        }
        private void btnApply_Click(object sender, EventArgs e)
        {
            switch (cmbSettings.Text)
            {
                case "System Default":
                    currentLoad = Settings.LoadSettings("SettingsDefault.xml");
                    break;

                case "User Default":
                    currentLoad = Settings.LoadSettings();
                    break;

                case "Current":
                    FillFormToSettings(currentLoad);
                    break;
            }

            Settings.ParseSettings(currentLoad, current);
            player.torrentStreamer?.ParseSettingsToBitSwarm();
        }
        private void btnSave_Click(object sender, EventArgs e)
        {
            switch (cmbSettings.Text)
            {
                case "System Default":
                    currentLoad = Settings.LoadSettings("SettingsDefault.xml");
                    Settings.ParseSettings(currentLoad, current);
                    Settings.SaveSettings(current);
                    userDefault = Settings.LoadSettings();
                    break;

                case "Current":
                    FillFormToSettings(currentLoad);
                    Settings.ParseSettings(currentLoad, current);
                    Settings.SaveSettings(current);
                    userDefault = Settings.LoadSettings();
                    break;
            }

            player.torrentStreamer?.ParseSettingsToBitSwarm();

            Hide();
        }
        private void btnSurfCopyAll_Click(object sender, EventArgs e)
        {
            FillFormToSettings(currentLoad, true);
            Settings.ParseSettings(currentLoad, current);
        }
    }
}