/* WinForms / WPF User Control implementation of MediaRouter 's Library
 * 
 * by John Stamatakis
 */

using System;
using System.IO;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Security;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using static SuRGeoNix.Flyleaf.MediaRouter;
using Timer = System.Timers.Timer;
using System.Net;

namespace SuRGeoNix.Flyleaf.Controls
{
    public partial class FlyleafPlayer : UserControl
    {
        public  MediaRouter player;

        bool    designMode = (LicenseManager.UsageMode == LicenseUsageMode.Designtime);

        Form    form;
        Timer   timer;
        long    userFullActivity;
        long    userActivity;
        int     seekSum, seekStep;
        bool    seeking;

        // Shutdown Countdown
        bool    shutdownCancelled;
        long    shutdownCountDown   = -1;

        // History Seconds Save (To re-open and seek at that second)
        int     timerLoop           = 0;
        int     playingStartedSec   = 0;

        static string   HISTORY_PATH            = Path.Combine(Directory.GetCurrentDirectory(), "History");
        static string   ICONS_PATH              = Path.Combine(HISTORY_PATH, "Icons");
        static int      TIMER_INTERVAL          = 400; // timerLoop based on that
        static int      cursorHideTimes         = 0;
        static object   cursorHideTimesLocker   = new object();

        #region Initializing / Disposing
        private void OnLoad(object sender, EventArgs e) { 
            if (designMode) return;

            Thread t = new Thread(() =>
            {
                Thread.Sleep(70);

                thisInitSize = Size;

                thisLastPos     = Location;
                thisLastSize    = Size;
                thisParent      = Parent;

                if (!isWPF && ParentForm != null)
                {
                    form            = ParentForm;
                    formLastSize    = form.Size;
                    formInitSize    = form.Size;
                    formLastPos     = form.Location;
                    formBorderStyle = form.FormBorderStyle;
                }

                if (ParentForm != null) NormalScreen();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start(); 

            if (player.renderer.HookControl != null) return;

            t = new Thread(() =>
            {
                Thread.Sleep(70);
                Initialize();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start(); 
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (designMode) return;

            if (config.hookForm._Enabled && config.hookForm.AllowResize) Application.RemoveMessageFilter(this.mouseMessageFilter);

            player?.Close(); 
        }
        protected override void OnPaintBackground(PaintEventArgs e) { if (designMode) player.Render(); }

        //protected override void OnPaint(PaintEventArgs e) { }

        public FlyleafPlayer()
        {
            InitializeComponent();
            
            if (!designMode) tblBar.Visible = false;

            #if DEBUG
                player  = new MediaRouter(1);
            #else
                player  = new MediaRouter(0);
            #endif
            
            config  = new Settings(player);

            Load                    += OnLoad;
            ParentChanged           += OnParentChanged;
            config.PropertyChanged   = SettingsChanged;
        }
        private void SettingsChanged()
        {
            if (config.bar.Play)    tblBar.EnableColumn(0); else tblBar.DisableColumn(0);
            if (config.bar.Playlist)tblBar.EnableColumn(2); else tblBar.DisableColumn(2);
            if (config.bar.Subs)    tblBar.EnableColumn(3); else tblBar.DisableColumn(3);
            if (config.bar.Mute)    tblBar.EnableColumn(4); else tblBar.DisableColumn(4);
            if (config.bar.Volume)  tblBar.EnableColumn(5); else tblBar.DisableColumn(5);

            if (config.main.AllowDrop)   AllowDrop = true;

            if (designMode)
            {
                player.Activity = config.main._PreviewMode;
                if (Controls.Contains(tblBar) && !ShouldVisible(config.main._PreviewMode, config.bar._Visibility) ) { Controls.Remove(tblBar); } else if (!Controls.Contains(tblBar) && ShouldVisible(config.main._PreviewMode, config.bar._Visibility)) { Controls.Add(tblBar); }
            }
        }
        private void OnParentChanged(object sender, EventArgs e)
        {
            //if (Parent != null) thisParent = Parent;
            if (player.renderer.HookControl != null) return;
            
            if (designMode)
            {
                form = ParentForm;
                player.InitHandle(config.hookForm.HookHandle ? form.Handle : Handle, designMode);
                player.renderer.CreateSample(config.main.SampleFrame);

                return;
            }

            if (ParentForm != null)
            {
                ParentForm.Load += (o, e1) => { OnLoad(o,e1); };
                Initialize();
            }
        }
        public void Initialize()
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => Initialize())); return; }

            try
            {
                Directory.CreateDirectory(Settings.CONFIG_PATH);
                Directory.CreateDirectory(HISTORY_PATH);
                Directory.CreateDirectory(ICONS_PATH);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Flyleaf: IO Error (Run as admin)", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            thisInitSize = Size;
            thisLastPos         = Location;
            thisLastSize        = Size;
            thisParent          = Parent;

            if (!isWPF && ParentForm != null)
            {
                form            = ParentForm;
                formLastSize    = form.Size;
                formInitSize    = form.Size;
                formLastPos     = form.Location;
                formBorderStyle = form.FormBorderStyle;
            }

            // Before InitHandle
            if (config.torrent._Enabled) 
                player.MediaFilesClbk        = MediaFilesReceived;

            screen = Screen.FromControl(this).Bounds;
            player.InitHandle(config.hookForm.HookHandle ? form.Handle : Handle);
            player.OpenFinishedClbk          = OpenFinished;
            player.StatusChanged            += Player_StatusChanged;
            player.audioPlayer.VolumeChanged+= OnVolumeChanged;

            if (config.bar._Visibility != VisibilityMode.Never)
            {
                tblBar.volBar.SetValue(player.Volume);

                HandlersBar();

                if (config.hookForm._Enabled)
                    FormHandlersChangeFocus();
                else
                    HandlersChangeFocus();
            }
            else
            {
                tblBar.btnPlaylist.Click        += BtnPlaylist_Click;
                tblBar.seekBar.ValueChanged     += SeekBar_ValueChanged;
                tblBar.volBar.ValueChanged      += VolBar_ValueChanged;
            }

            if (config.main.EmbeddedList)
            {
                lstMediaFiles.MouseDoubleClick  += lstMediaFiles_MouseClickDbl;
                lstMediaFiles.KeyPress          += lstMediaFiles_KeyPress;

                lvSubs.DoubleClick              += lvSubs_DoubleClick;
                lvSubs.KeyPress                 += lvSubs_KeyPress;
            }

            if (config.main.AllowDrop)              HandlersDragDrop();

            if (config.keys._Enabled)               HandlersKeyboard();

            if (config.main.AllowFullScreen)
                MouseDoubleClick += (o, e) =>   {   FullScreenToggle(); };

            if (config.hookForm._Enabled)
            {
                if (config.hookForm.HookKeys)       FormHandlersKeyboard();
                if (config.hookForm.AllowResize)    FormHandlersMouse();
                if (config.main.AllowDrop)          FormHandlersDragDrop();
                if (config.main.AllowFullScreen)    form.MouseDoubleClick += (o, e) => { FullScreenToggle(); };
            }
            
            if (!config.hookForm.AllowResize || !config.hookForm._Enabled) MouseMove += FlyLeaf_MouseMove;

            if (config.bar._Visibility != VisibilityMode.Never) tblBar.Visible = true;

            if (form != null && form.Created) NormalScreen();

            userActivity                = DateTime.UtcNow.Ticks;
            userFullActivity            = userActivity;

            timer                       = new Timer(TIMER_INTERVAL);
            timer.SynchronizingObject   = this;
            timer.AutoReset             = true;
            timer.Elapsed               += Timer_Elapsed;

            if (!File.Exists(Path.Combine(Settings.CONFIG_PATH, "SettingsDefault.xml"))) Settings.SaveSettings(config, "SettingsDefault.xml");
            if (!File.Exists(Path.Combine(Settings.CONFIG_PATH, "SettingsUser.xml"))) File.Copy(Path.Combine(Settings.CONFIG_PATH,"SettingsDefault.xml"), Path.Combine(Settings.CONFIG_PATH,"SettingsUser.xml"));

            userDefault     = Settings.LoadSettings();
            systemDefault   = Settings.LoadSettings("SettingsDefault.xml");

            Settings.ParseSettings(userDefault, config);
            player.streamer.config = config.torrent;
            SettingsChanged();

            if (config.main.EmbeddedList)
            {
                frmSettings = new FrmSettings(systemDefault, userDefault, config);
                frmSettings.player = player;
                ContextMenuStrip = menu;

                player.History.HistoryChanged += History_HistoryChanged;
                BuildRecentMenu();
            }

            if (!config.main.EmbeddedList && config.main.HistoryEnabled) config.main.HistoryEnabled = false;

            timer.Start();
            player.Render();
        }
        #endregion

        #region History
        History.Entry curHistoryEntry;
        private void History_HistoryChanged(object source, EventArgs e)
        {
            BuildRecentMenu();
        }
        HashSet<string> favIcons = new HashSet<string>();
        Thread favIconsThread;

        public class GZipWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                HttpWebRequest request = (HttpWebRequest)base.GetWebRequest(address);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                return request;
            }
        }
        private void FetchIcons(HashSet<string> curFavIcons)
        {
            if (curFavIcons.Count == 0) return;

            Utils.EnsureThreadDone(favIconsThread);

            favIconsThread = new Thread(() =>
            {
                lock (favIcons)
                {
                    List<string> hosts = new List<string>();
                    using (var client = new GZipWebClient())
                    {
                        foreach (var favIcon in curFavIcons)
                        {
                            favIcons.Add(favIcon);

                            try
                            {
                                Uri cur = new Uri(favIcon);
                                client.DownloadFile(new Uri(favIcon), Path.Combine(ICONS_PATH, cur.DnsSafeHost + ".ico"));
                                hosts.Add(cur.DnsSafeHost);
                            } catch (Exception e) { Console.WriteLine(e.Message); }
                        }
                    }
                    if (hosts.Count > 0) UpdateRecentMenu(hosts);
                }
            });
            favIconsThread.IsBackground = true;
            favIconsThread.Start();
        }

        private void UpdateRecentMenu(List<string> hosts)
        {
            if (form.InvokeRequired) { form.BeginInvoke(new Action(() => UpdateRecentMenu(hosts))); return; }

            foreach (var t1 in recentToolStripMenuItem.DropDownItems)
            {
                var curMenu =((ToolStripMenuItem)t1);
                var curTag = (History.Entry)curMenu.Tag;

                if ((curTag.UrlType == InputType.Web || curTag.UrlType == InputType.WebLive))
                {
                    string curHost = (new Uri(curTag.Url)).DnsSafeHost;
                    string icoPath = Path.Combine(ICONS_PATH, curHost + ".ico");
                    if (hosts.Contains(curHost) && File.Exists(icoPath))
                        curMenu.Image = Image.FromFile(icoPath);
                }
            }
        }
        private void BuildRecentMenu()
        {
            if (!config.main.EmbeddedList) return;

            if (form.InvokeRequired) { form.BeginInvoke(new Action(() => BuildRecentMenu())); return; }

            lock (favIcons)
            {
                recentToolStripMenuItem.DropDownItems.Clear();
                HashSet<string> curFavIcons = new HashSet<string>();

                if (player.History.Entries.Count != 0)
                {
                    ToolStripMenuItem[] menuRecent = new ToolStripMenuItem[player.History.Entries.Count];

                    for (int i=0; i<player.History.Entries.Count; i++)
                    {
                        try
                        {
                            var curEntry    = player.History.Entries[player.History.Entries.Count - (i + 1)];
                            string text     = "";
                            menuRecent[i]   = new ToolStripMenuItem();

                            if (curEntry.UrlType == InputType.File)
                            {
                                FileInfo fi = new FileInfo(curEntry.Url);
                                menuRecent[i].Image = Properties.Resources.VideoFile;
                                menuRecent[i].ToolTipText = fi.DirectoryName;
                                text = fi.Name;
                            }
                            else if (curEntry.UrlType == InputType.TorrentPart || curEntry.UrlType == InputType.TorrentFile)
                            {
                                menuRecent[i].Image = Properties.Resources.Torrent;
                                text = curEntry.TorrentFile;
                            }
                            else if (curEntry.UrlType == InputType.Web || curEntry.UrlType == InputType.WebLive)
                            {
                                text = curEntry.Url;
                                Uri uri = new Uri(curEntry.Url);

                                if (File.Exists(Path.Combine(ICONS_PATH, uri.DnsSafeHost + ".ico")))
                                    menuRecent[i].Image = Image.FromFile(Path.Combine(ICONS_PATH, uri.DnsSafeHost + ".ico"));
                                else
                                {
                                    menuRecent[i].Image = Properties.Resources.Web;
                                    if (!favIcons.Contains(uri.Scheme + "://" + uri.DnsSafeHost + "/favicon.ico"))
                                        curFavIcons.Add(uri.Scheme + "://" + uri.DnsSafeHost + "/favicon.ico");
                                }
                            }

                            if (text.Length > 100)
                            {
                                if (menuRecent[i].ToolTipText == null || menuRecent[i].ToolTipText == "")
                                    menuRecent[i].ToolTipText = text;
                                text = text.Substring(0, 100) + "...";
                            }
                            menuRecent[i].Text  = text;
                            menuRecent[i].Tag   = curEntry;
                            menuRecent[i].Click += MenuHistory_Click;

                        } catch (Exception) { }
                    }

                    recentToolStripMenuItem.Text = $"Recent ({menuRecent.Length})";
                    recentToolStripMenuItem.DropDownItems.AddRange(menuRecent);
                }

                if (curFavIcons.Count > 0) FetchIcons(curFavIcons);
            }
        }
        private void MenuHistory_Click(object sender, EventArgs e)
        {
            curHistoryEntry = (History.Entry) ((ToolStripMenuItem)sender).Tag;

            if (player.Url == curHistoryEntry.Url && curHistoryEntry.TorrentFile != null)
                player.Open(curHistoryEntry.TorrentFile, true);
            else
                player.Open(curHistoryEntry.Url);
        }
        #endregion

        #region Activity
        private void Player_StatusChanged(object source, StatusChangedArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => Player_StatusChanged(source, e))); return; }

            if (e.status == MediaRouter.Status.PLAYING) 
                { tblBar.btnPlay.BackgroundImage = Properties.Resources.Pause;  tblBar.btnPlay.Text  = " "; playingStartedSec = (int)(player.CurTime / 10000000) + 1; timerLoop = 0; }
            else
                { tblBar.btnPlay.BackgroundImage = Properties.Resources.Play;   tblBar.btnPlay.Text  = "";  }
        }
        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ActivityMode curActivityMode = CurActivityMode();

            if (player.Activity != curActivityMode)
            {
                player.Activity = curActivityMode;

                if      (curActivityMode == ActivityMode.FullActive)    GoFullActive();
                else if (curActivityMode == ActivityMode.Active)        GoActive();
                else if (curActivityMode == ActivityMode.Idle)          GoIdle();
            }

            if (resizing) tblBar.Visible = false;

            if (config.main.ShutdownOnFinish && !shutdownCancelled && curActivityMode == ActivityMode.Idle && player.decoder.Finished && !player.isPlaying)
            {
                if (shutdownCountDown == -1) shutdownCountDown = DateTime.UtcNow.Ticks;

                long distance = ((long)config.main.ShutdownAfterIdle * 1000 * 10000) - (DateTime.UtcNow.Ticks - shutdownCountDown);
                //long distance = (config.main.ShutdownAfterIdle * 1000 * 10000) - (Utils.GetLastInputTime() * 10000); // To check inactivity before flyleaf's idle
                player.renderer.NewMessage(OSDMessage.Type.Failed, "Shutdown in " + (new TimeSpan(distance)).ToString(@"hh\:mm\:ss"));

                if (distance < 0)
                {
                    Utils.Shutdown();
                    form?.Close();
                }
            }

            if (!player.isPlaying) return;

            if (!seeking && !player.isLive)
            {
                if (tblBar.seekBar.Maximum == 1) return; // TEMP... thorws exception just after open
                int      barValue = (int)(player.CurTime / 10000000) + 1;
                if      (barValue > (int)tblBar.seekBar.Maximum) barValue = (int)tblBar.seekBar.Maximum;
                else if (barValue < (int)tblBar.seekBar.Minimum) barValue = (int)tblBar.seekBar.Minimum;

                tblBar.seekBar.SetValue(barValue);

                // Saves the current second in history (should be only if the duration of movie is large enough and playing time was at least X secs) - see also on OpenFinished for Seek
                timerLoop++;

                if (timerLoop == 20)// && player.UrlType != InputType.WebLive)
                {
                    timerLoop = 0;
                    if ((int)tblBar.seekBar.Value - playingStartedSec > 60)
                        player.History.Update((int)(player.CurTime / 10000000), player.AudioExternalDelay, player.SubsExternalDelay);
                }
            }
        }
        private ActivityMode CurActivityMode()
        {
            if ((DateTime.UtcNow.Ticks - userFullActivity   ) / 10000 < config.main.IdleTimeout) return ActivityMode.FullActive;
            if ((DateTime.UtcNow.Ticks - userActivity       ) / 10000 < config.main.IdleTimeout) return ActivityMode.Active;

            return ActivityMode.Idle;
        }
        private void GoIdle()
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => GoIdle())); return; }

            if (frmSettings != null && frmSettings.Visible) return;

            player.Activity = ActivityMode.Idle;

            if (config.main.HideCursor)
            {
                lock (cursorHideTimesLocker)
                {
                    Cursor.Hide();
                    cursorHideTimes++;
                }
            }

            ToggleBarVisibility();
            if (!player.isPlaying) player.Render();
        }
        private void GoActive()
        {
            if (shutdownCountDown != -1) shutdownCancelled = true;

            player.Activity = ActivityMode.Active;
            ToggleBarVisibility();
        }
        private void GoFullActive()
        {
            if (shutdownCountDown != -1) shutdownCancelled = true;

            if (InvokeRequired) { BeginInvoke(new Action(() => GoFullActive())); return; }

            player.Activity = ActivityMode.FullActive;

            if (config.main.HideCursor)
            {
                lock (cursorHideTimesLocker)
                {
                    for (int i = 0; i < cursorHideTimes; i++) Cursor.Show();
                    cursorHideTimes = 0;
                }
            }

            if (resizing) return;

            ToggleBarVisibility();
            if (!player.isPlaying) player.Render();
            
        }
        private void ToggleBarVisibility()
        {
            if (config.bar._Visibility == VisibilityMode.Never) return;

            bool shouldbe = ShouldVisible(player.Activity, config.bar._Visibility);

            if (!tblBar.Visible && shouldbe)
                tblBar.Visible = true;
            else if (tblBar.Visible && !shouldbe)
                { tblBar.Visible = false; player.Render(); }
        }
        #endregion

        #region Implementation Main
        public void Open(string url)
        {
            curHistoryEntry = null;
            player.Open(url);
        }
        public void MediaFilesReceived(List<string> mediaFiles, List<long> mediaFilesSizes)
        {
            if (!config.main.EmbeddedList)
            {
                player.Open(mediaFiles[0], true);
                return;
            }

            if (lstMediaFiles.InvokeRequired)
            {
                lstMediaFiles.BeginInvoke(new Action(() => MediaFilesReceived(mediaFiles, mediaFilesSizes)));
                return;
            }

            List<string> mediaFilesSorted = Utils.GetMoviesSorted(mediaFiles);

            lstMediaFiles.BeginUpdate();
            lstMediaFiles.Items.Clear();
            lstMediaFiles.Items.AddRange(mediaFilesSorted.ToArray());
            lstMediaFiles.EndUpdate();

            if (curHistoryEntry != null && curHistoryEntry.TorrentFile != null)
                player.Open(curHistoryEntry.TorrentFile, true);
            else if (lstMediaFiles.Items.Count == 1)
                player.Open(mediaFilesSorted[0], true);
            else
                FixLstMedia(true);
        }
        public void OpenFinished(bool success, string selectedFile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OpenFinished(success, selectedFile)));
                return;
            }

            if (!player.isLive)
            {
                tblBar.seekBar.SetValue(0);
                tblBar.seekBar.Maximum = (int)(player.Duration / 10000000);
            }
            else
            {
                tblBar.seekBar.Maximum = 1;
                tblBar.seekBar.SetValue(1);
            }

            if (player.isFailed) return;

            if (!player.hasAudio || !player.doAudio) 
                { tblBar.btnMute.Enabled = false; tblBar.volBar.Enabled = false; }
            else
                { tblBar.btnMute.Enabled = true; tblBar.volBar.Enabled = true; }

            FixAspectRatio(true);

            if (config.main.EmbeddedList && !player.isTorrent)
            {
                lstMediaFiles.Items.Clear();
                lstMediaFiles.Items.Add(selectedFile);
            }

            // Open at saved history second (only if history secs > 1m and duration-secs > 5m and duration > 15m)
            if (curHistoryEntry != null && !player.isLive && player.History.GetCurrent().CurSecond > 60 && (int)(player.Duration / 10000000) - player.History.GetCurrent().CurSecond > 60 * 5 && (int)(player.Duration / 10000000) > 60 * 15)
                player.Seek(player.History.GetCurrent().CurSecond * 1000, true, true, true);
            else
                Play();
        }
        public void Play() { shutdownCountDown = -1; shutdownCancelled = false; player.Play(); }
        public void MuteUnmute()
        {
            if (!player.hasAudio && !config.hookForm._Enabled) return;

            if (tblBar.btnMute.Text == "")
            {
                tblBar.btnMute.BackgroundImage = Properties.Resources.Mute;
                tblBar.btnMute.Text = " ";
                if (config.hookForm._Enabled) player.Mute2 = true; else player.Mute = true;
            }
            else
            {
                tblBar.btnMute.BackgroundImage = Properties.Resources.Speaker;
                tblBar.btnMute.Text = "";
                if (config.hookForm._Enabled) player.Mute2 = false; else player.Mute = false;
            }
        }
        public void PlayPause()
        {
            if (!player.isReady) return;

            if (tblBar.btnPlay.Text == "")
            {
                tblBar.btnPlay.BackgroundImage = Properties.Resources.Pause;
                tblBar.btnPlay.Text = " ";
                Play();
            } 
            else
            {
                tblBar.btnPlay.BackgroundImage = Properties.Resources.Play;
                tblBar.btnPlay.Text = "";
                player.Pause();
            }
        }
        public void VolUp   (int v)
        {
            if (player.Volume + v + 1 > 99) player.Volume = 100; else player.Volume += v + 1;
        }
        public void VolDown (int v)
        {
            if (player.Volume  - (v - 1) < 1) player.Volume = 0; else player.Volume -= v - 1;
        }
        public void OnVolumeChanged(object sender, AudioPlayer.VolumeChangedArgs e)
        { 
            if (InvokeRequired) { BeginInvoke(new Action(() => OnVolumeChanged(sender, e))); return; }

            tblBar.volBar.Value = e.volume;

            if (e.mute)
            {
                tblBar.btnMute.BackgroundImage = Properties.Resources.Mute;
                tblBar.btnMute.Text = " ";
            }
            else
            {
                tblBar.btnMute.BackgroundImage = Properties.Resources.Speaker;
                tblBar.btnMute.Text = "";   
            }
        }
        #endregion

        #region Implementation - Screen
        Rectangle               screen;
        bool                    isFullScreen;
        Size                    thisInitSize;
        Size                    formInitSize;
        Control                 thisParent;
        FormBorderStyle         formBorderStyle;
        Size                    thisLastSize;
        Size                    formLastSize;
        Point                   thisLastPos;
        Point                   formLastPos;
        public void FullScreenToggle()
        {
            if (!config.main.AllowFullScreen || isWPF) return;

            if (form.InvokeRequired) { form.BeginInvoke(new Action(() => FullScreenToggle())); return; }

            if (form.Width != screen.Width && form.Height != screen.Height)
                FullScreen();
            else
                NormalScreen();
        }
        public void FullScreen()
        {
            if (!config.main.AllowFullScreen || isWPF) return;

            screen = Screen.FromControl(this).Bounds;

            if (form.Width != screen.Width && form.Height != screen.Height)
            {
                formLastPos         = form.Location;
                formLastSize        = form.Size;
                formBorderStyle     = form.FormBorderStyle;

                if (!config.hookForm._Enabled) form.FormBorderStyle = FormBorderStyle.None;

                form.Location       = screen.Location;
                form.Size           = screen.Size;

                if (!config.hookForm._Enabled)
                {
                    thisLastPos     = Location;
                    thisLastSize    = Size;

                    thisParent      = Parent;
                    Parent          = form;

                    Location        = screen.Location;
                    Size            = form.Size;
                    BringToFront();
                    Focus();
                    player.Render();
                }
                isFullScreen = true;
            }

            if (lstMediaFiles.Visible) FixLstMedia(true);
            if (lvSubs.Visible) FixLvSubs(true);
        }
        public void NormalScreen    (bool fromOpen = false)
        {
            if (!config.main.AllowFullScreen  || isWPF || form == null) return;

            if (form.InvokeRequired) { form.BeginInvoke(new Action(() => NormalScreen())); return; }

            if (!config.hookForm._Enabled)
            {
                Parent = thisParent;
                form.FormBorderStyle = formBorderStyle;
                Focus();
            }
            
            form.Location   = formLastPos;
            form.Size       = formLastSize;

            Location        = thisLastPos;
            Size            = thisLastSize;

            isFullScreen = false;

            player.Render();
            FixAspectRatio();
        }
        public void FixAspectRatio  (bool fromOpen = false)
        {
            if (isWPF) return;
            if (isFullScreen) { player.renderer.HookResized(null, null); return; }
            if (!config.hookForm._Enabled || !config.hookForm.AutoResize) return;
            if (form.InvokeRequired) { form.BeginInvoke(new Action(() => FixAspectRatio())); return; }

            float AspectRatio = config.video.AspectRatio == ViewPorts.KEEP ? player.DecoderRatio : player.CustomRatio;

            if (fromOpen) 
                if (config.hookForm._Enabled && config.hookForm.HookHandle) form.Size = formInitSize; else form.Size = thisInitSize;

            if ( form.Width / AspectRatio > form.Height)
                form.Size = new Size((int)(form.Height * AspectRatio), form.Height);
            else
                form.Size = new Size(form.Width, (int)(form.Width / AspectRatio));

            if (lstMediaFiles.Visible) FixLstMedia(true);
            if (lvSubs.Visible) FixLvSubs(true);

            // Should be docked?
            //screenPlay.Size = form.Size;
        }
        #endregion

        #region User Actions
        private void BtnPlay_Click              (object sender, EventArgs e) { PlayPause(); }
        private void BtnMute_Click              (object sender, EventArgs e) { MuteUnmute(); }
        private void BtnPlaylist_Click          (object sender, EventArgs e) { if (!config.main.EmbeddedList) return; if (lstMediaFiles.Visible) FixLstMedia(false); else FixLstMedia(true); }
        private void BtnSubs_Click(object sender, EventArgs e)
        {
            if (lvSubs.Visible) FixLvSubs(false); else FixLvSubs(true);
        }

        private void SeekBar_ValueChanged       (object sender, EventArgs e)
        {
            if (!player.isReady) return;

            shutdownCountDown = -1; shutdownCancelled = false;

            long seektime = (((long)(tblBar.seekBar.Value) * 1000) + 500);
            if (config.bar.SeekOnSlide) player.Seek((int)seektime); else Interlocked.Exchange(ref player.SeekTime, seektime * 10000);
            if (!player.isPlaying) player.Render(); // To refresh the OSD time (CPU/GPU heavy) - alternative solution in the Timer_Elapsed but with delay
        }
        private void SeekBar_MouseDown          (object sender, MouseEventArgs e)
        {
            seeking = true;
            userFullActivity = DateTime.UtcNow.Ticks;
        }
        private void SeekBar_MouseUp            (object sender, MouseEventArgs e)
        {
            seeking = false;

            if (!player.isReady) return;

            long seektime = (((long)(tblBar.seekBar.Value) * 1000) + 500);
            player.Seek((int)seektime, true);
        }

        private void VolBar_ValueChanged        (object sender, EventArgs e)
        {
            player.Volume = (int)tblBar.volBar.Value;
        }

        private void FlyLeaf_DragEnter          (object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
            //lastUserActionTicks = DateTime.UtcNow.Ticks;
        }
        private void FlyLeaf_DragDrop           (object sender, DragEventArgs e)
        {
            if ( e.Data.GetDataPresent(DataFormats.FileDrop) )
            {
                Cursor = Cursors.Default;
                string[] filenames = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                if (filenames.Length > 0) Open(filenames[0]);
            }
            else if ( e.Data.GetDataPresent(DataFormats.Text) )
            {
                Cursor = Cursors.Default;
                string url = e.Data.GetData(DataFormats.Text, false).ToString();
                if (url.Length > 0) Open(url);
            }

            form?.Activate();
        }

        private void FlyLeaf_KeyDown            (object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    // AUDIO
                    case Keys.OemOpenBrackets:
                        if (!player.hasAudio) return;

                        player.AudioExternalDelay -= config.keys.AudioDelayStep2 * 10000;

                        break;

                    case Keys.OemCloseBrackets:
                        if (!player.hasAudio) return;

                        player.AudioExternalDelay += config.keys.AudioDelayStep2 * 10000;

                        break;

                    // SUBTITLES
                    case Keys.OemSemicolon:
                        if (!player.hasSubs) return;

                        player.SubsExternalDelay -= config.keys.SubsDelayStep2 * 10000;

                        break;

                    case Keys.OemQuotes:
                        if (!player.hasSubs) return;

                        player.SubsExternalDelay += config.keys.SubsDelayStep2 * 10000;

                        break;

                    case Keys.Up:
                        if (!player.hasSubs) return;

                        player.SubsPosition += config.keys.SubsPosYStep;

                        break;

                    case Keys.Down:
                        if (!player.hasSubs) return;

                        player.SubsPosition -= config.keys.SubsPosYStep;
                        
                        break;

                    case Keys.Right:
                        if (!player.hasSubs) return;

                        player.SubsFontSize += config.keys.SubsFontSizeStep;
                        
                        break;

                    case Keys.Left:
                        if (!player.hasSubs) return;

                        player.SubsFontSize -= config.keys.SubsFontSizeStep;
                        
                        break;

                    // MISC
                    case Keys.V: // Open Clipboard
                        e.SuppressKeyPress = true;

                        Open(Clipboard.GetText());

                        break;

                    case Keys.C:
                        e.SuppressKeyPress = true;

                        Clipboard.SetText(player.Url);
                        break;

                    case Keys.S: // SeekOnSlide On/Off
                        e.SuppressKeyPress = true;

                        config.bar.SeekOnSlide = !config.bar.SeekOnSlide;

                        break;

                    case Keys.N:
                        e.SuppressKeyPress = true;

                        if (!player.isReady) return;
                        player.OpenNextAvailableSub();

                        break;

                    case Keys.P:
                        e.SuppressKeyPress = true;

                        if (!player.isReady) return;
                        if (player.PrevSubId == -1 || player.PrevSubId == player.CurSubId) return;

                        player.OpenSubs(player.PrevSubId);

                        break;
                }

                return;
            }

            decimal seekValue = 0;

            switch (e.KeyCode)
            {
                // SEEK
                case Keys.Right:
                    if (!player.isReady) return;

                    seeking = true;
                    seekStep++;
                    if (seekStep == 1) seekSum = config.keys.SeekStepFirst - config.keys.SeekStep;
                    seekSum += seekStep * config.keys.SeekStep;

                    seekValue = tblBar.seekBar.Value + seekSum / 1000;
                    if (seekValue > tblBar.seekBar.Maximum) seekValue = tblBar.seekBar.Maximum;
                    if (seekValue < 0) seekValue = 0;
                    if (seekValue != tblBar.seekBar.Value) tblBar.seekBar.Value = seekValue;

                    break;

                case Keys.Left:
                    if (!player.isReady) return;

                    seeking = true;
                    seekStep++;
                    if (seekStep == 1) seekSum = -(config.keys.SeekStepFirst - config.keys.SeekStep);
                    seekSum -= seekStep * config.keys.SeekStep;
                    
                    seekValue = tblBar.seekBar.Value + seekSum / 1000;
                    if (seekValue < tblBar.seekBar.Minimum) seekValue = tblBar.seekBar.Minimum;
                    if (seekValue != tblBar.seekBar.Value) tblBar.seekBar.Value = seekValue;

                    break;

                // AUDIO
                case Keys.OemOpenBrackets:
                    if (!player.hasAudio) return;

                    player.AudioExternalDelay -= config.keys.AudioDelayStep * 10000;

                    break;

                case Keys.OemCloseBrackets:
                    if (!player.hasAudio) return;

                    player.AudioExternalDelay += config.keys.AudioDelayStep * 10000;

                    break;

                // SUBTITLES
                case Keys.OemSemicolon:
                    if (!player.hasSubs) return;

                    player.SubsExternalDelay -= config.keys.SubsDelayStep * 10000;

                    break;

                case Keys.OemQuotes:
                    if (!player.hasSubs) return;

                    player.SubsExternalDelay += config.keys.SubsDelayStep * 10000;

                    break;

                // VOLUME
                case Keys.Up:
                    VolUp(config.keys.VolStep);
                    if (config.bar._Visibility != VisibilityMode.Never) tblBar.volBar.SetValue(player.Volume);
                    
                    break;

                case Keys.Down:
                    VolDown(config.keys.VolStep);
                    if (config.bar._Visibility != VisibilityMode.Never) tblBar.volBar.SetValue(player.Volume);

                    break;
            }
        }
        private void FlyLeaf_KeyUp              (object sender, KeyEventArgs e)
        {
            if (e.Control) return;

            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:

                    seeking = false;

                    if (!player.isReady) return;

                    //if (!config.bar.SeekOnSlide)
                    //{
                        seeking = true;
                        long seektime = (((long)(tblBar.seekBar.Value) * 1000) + 500);
                        player.Seek((int)seektime, true, e.KeyCode == Keys.Right ? true : false);
                    //}
                    

                    seeking     = false;
                    seekSum     = 0;
                    seekStep    = 0;

                    break;
            }
        }
        private void FlyLeaf_KeyPress           (object sender, KeyPressEventArgs e)
        {
            char c = Char.ToLower(e.KeyChar);
            
            if (c == KeyCodeToUnicode(Keys.F) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.F))
            {
                FullScreenToggle();
            }
            else if (c == KeyCodeToUnicode(Keys.H) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.H))
            {
                player.HWAccel = !player.HWAccel;
            }
            else if (c == KeyCodeToUnicode(Keys.I) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.I))
            {
                userFullActivity = 0;
                userActivity = 0;
                GoIdle();
                return;
            }
            else if (c == KeyCodeToUnicode(Keys.P) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.P)|| Keys.Space == (Keys) c) { PlayPause(); }
            else if (c == KeyCodeToUnicode(Keys.R) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.R))
            {
                if (player.ViewPort == MediaRouter.ViewPorts.KEEP) player.ViewPort = MediaRouter.ViewPorts.FILL;
                else if (player.ViewPort != MediaRouter.ViewPorts.KEEP) player.ViewPort = MediaRouter.ViewPorts.KEEP;
            }
            else if (c == KeyCodeToUnicode(Keys.M) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.M)) { MuteUnmute(); }
            else if (c == KeyCodeToUnicode(Keys.S) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.S))
            {
                player.doSubs = !player.doSubs;
            }
            else if (c == KeyCodeToUnicode(Keys.A) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.A))
            {
                player.doAudio = !player.doAudio;
            }
            else if (c == KeyCodeToUnicode(Keys.O) || Char.ToUpper(c) == KeyCodeToUnicode(Keys.O))
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.ShowDialog();
                Open(ofd.FileName);
            }

            else if ((Keys)c == Keys.Escape)
            {
                if (config.main.EmbeddedList && lstMediaFiles.Visible) FixLstMedia(false);
                else if (lvSubs.Visible) FixLvSubs(false);
                else if (isFullScreen) FullScreenToggle();
            }
            //{
            //    if (picHelp.Visible)
            //        picHelp.Visible = false;

            //    else
            //        MediaFilesToggle();
            //}

            //lastUserActionTicks         = DateTime.UtcNow.Ticks;
            }

        private void FlyLeaf_MouseMove          (object sender, MouseEventArgs e)
        {
            if (displayMMoveLastPos == e.Location) return;
            displayMMoveLastPos = e.Location;

            userFullActivity = DateTime.UtcNow.Ticks;
        }

        private void lstMediaFiles_MouseClickDbl(object sender, MouseEventArgs e)
        {
            curHistoryEntry = null;

            if (!player.isTorrent) { lstMediaFiles.Visible = false; if (!player.isPlaying) player.Render(); return; }

            if (lstMediaFiles.SelectedItem == null) return;

            player.Open(lstMediaFiles.SelectedItem.ToString(), true);
            FixLstMedia(false);
            Focus();
        }
        private void lstMediaFiles_KeyPress     (object sender, KeyPressEventArgs e)
        {
            curHistoryEntry = null;
            userActivity = DateTime.UtcNow.Ticks;

            if (e.KeyChar != (char)13) { FlyLeaf_KeyPress(sender, e); return; }

            if (!player.isTorrent) { FixLstMedia(false); return; }
            if (lstMediaFiles.SelectedItem == null) return;

            player.Open(lstMediaFiles.SelectedItem.ToString(), true);
            FixLstMedia(false);
            Focus();
        }
        private void lstMediaFiles_MouseMove    (object sender, MouseEventArgs e) { userFullActivity = DateTime.UtcNow.Ticks; }

        private void lvSubs_KeyPress(object sender, KeyPressEventArgs e)
        {
            userActivity = DateTime.UtcNow.Ticks;

            if ( e.KeyChar != (char)13 ) { FlyLeaf_KeyPress(sender, e); return; }

            lvSubs_DoubleClick(sender, e);
        }
        private void lvSubs_DoubleClick(object sender, EventArgs e)
        {
            if (lvSubs.SelectedItems.Count < 1) return;

            player.OpenSubs(int.Parse(lvSubs.SelectedItems[0].SubItems[lvSubs.Columns.Count].Text));

            FixLvSubs(false);
            Focus();
        }

        private void FixLstMedia(bool visible)
        {
            if (visible)
            {
                FixLvSubs(false);
                lstMediaFiles.Width     = Width  / 2 + Width  / 4;
                lstMediaFiles.Height    = Height / 2 + Height / 4;
                lstMediaFiles.Location  = new Point(Width / 8, Height / 8);

                if (!lstMediaFiles.Visible)
                {
                    for (int i=0; i<lstMediaFiles.Items.Count-1; i++)
                        if (lstMediaFiles.Items[i].ToString() == player.UrlName) { lstMediaFiles.SelectedItem = lstMediaFiles.Items[i]; break; }

                    lstMediaFiles.Visible = true;
                }
            }
            else
                lstMediaFiles.Visible = false;

            if (!player.isPlaying) player.Render();
        }
        private void FixLvSubs(bool visible)
        {
            if (form.InvokeRequired) { form.BeginInvoke(new Action(() => FixLvSubs(visible))); return; }

            if (visible)
            {
                if (!lvSubs.Visible)
                {
                    lvSubs.BeginUpdate();
                    lvSubs.Items.Clear();

                    for (int i = 0; i < player.AvailableSubs.Count; i++)
                    {
                        SubAvailable sub = player.AvailableSubs[i];
                        string name = sub.sub != null ? sub.sub.SubFileName : sub.path != null ? sub.path : "";
                        string lang = sub.lang != null ? sub.lang.LanguageName : "Unknown";
                        string rating = sub.sub != null ? sub.sub.SubRating : "0.0";
                        string downloaded = sub.sub != null && sub.sub.AvailableAt != null ? "Yes" : "";
                        string matchedby = "";
                        if (sub.sub != null)
                        {
                            if (sub.sub.MatchedBy == "moviehash")
                                matchedby = "M";
                            else if (sub.sub.MatchedBy == "imdbid")
                                matchedby = "I";
                            else if (sub.sub.MatchedBy == "fulltext")
                                matchedby = "F";
                            else
                                matchedby = "?";
                        }
                        string location = sub.streamIndex != -1 ? "Embedded" : sub.sub != null ? $"Opensubtitles {matchedby}" : "External";
                        lvSubs.Items.Add(new ListViewItem(new string[] { name, lang, rating, downloaded, location, i.ToString() }));
                    }

                    lvSubs.EndUpdate();
                    if (lvSubs.Items.Count > 0 && player.CurSubId < lvSubs.Items.Count && player.CurSubId >= 0) lvSubs.Items[player.CurSubId].Selected = true;
                }

                lvSubs.Columns[1].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
                lvSubs.Columns[2].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
                lvSubs.Columns[3].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
                lvSubs.Columns[4].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

                FixLstMedia(false);
                lvSubs.Width    = Width  / 2 + Width  / 4;
                lvSubs.Height   = Height / 2 + Height / 4;
                lvSubs.Location = new Point(Width / 8, Height / 8);
                lvSubs.Visible  = true;

                lvSubs.Columns[0].Width = lvSubs.Width - 460;
            }
            else
            {
                lvSubs.Visible = false;
            }

            if (!player.isPlaying) player.Render();
        }
        #endregion

        #region Event Handlers

        // KEYBOARD
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            userActivity = DateTime.UtcNow.Ticks;

            if (!config.keys._Enabled) return base.ProcessCmdKey(ref msg, keyData);

            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:

                    if (msg.HWnd == this.Handle) {
                        KeyEventArgs a = new KeyEventArgs(keyData);
                        FlyLeaf_KeyDown(null, a);
                        return true;
                    }

                    break;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
        public char KeyCodeToUnicode(Keys key)
        {
            byte[] keyboardState = new byte[255];
            bool keyboardStateStatus = GetKeyboardState(keyboardState);

            if (!keyboardStateStatus)
            {
                return '\0';
            }

            uint virtualKeyCode = (uint)key;
            uint scanCode = MapVirtualKey(virtualKeyCode, 0);
            IntPtr inputLocaleIdentifier = GetKeyboardLayout(0);

            StringBuilder result = new StringBuilder();
            ToUnicodeEx(virtualKeyCode, scanCode, keyboardState, result, (int)5, (uint)0, inputLocaleIdentifier);

            return result.ToString()[0];
        }

        [DllImport("user32.dll")]
        static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        // HANDLERS - Flyleaf
        private void HandlersBar()
        {
            tblBar.btnPlay.Click        += BtnPlay_Click;
            tblBar.btnMute.Click        += BtnMute_Click;
            tblBar.btnPlaylist.Click    += BtnPlaylist_Click;
            tblBar.btnSubs.Click        += BtnSubs_Click;
            tblBar.seekBar.ValueChanged += SeekBar_ValueChanged;
            tblBar.volBar.ValueChanged  += VolBar_ValueChanged;
            tblBar.seekBar.MouseDown    += SeekBar_MouseDown;
            tblBar.seekBar.MouseUp      += SeekBar_MouseUp;
        }
        private void HandlersKeyboard()
        {
            KeyDown     += FlyLeaf_KeyDown;
            KeyPress    += FlyLeaf_KeyPress;
            KeyUp       += FlyLeaf_KeyUp;
        }
        private void HandlersDragDrop()
        {
            AllowDrop    = true;
            DragDrop    += FlyLeaf_DragDrop;
            DragEnter   += FlyLeaf_DragEnter;
        }
        private void HandlersChangeFocus()
        {
            tblBar.btnPlay.     GotFocus += (o, e) => { this.ActiveControl = null; };
            tblBar.btnPlaylist. GotFocus += (o, e) => { this.ActiveControl = null; };
            tblBar.btnSubs.     GotFocus += (o, e) => { this.ActiveControl = null; };
            tblBar.seekBar.     GotFocus += (o, e) => { this.ActiveControl = null; };
            tblBar.btnMute.     GotFocus += (o, e) => { this.ActiveControl = null; };
            tblBar.volBar.      GotFocus += (o, e) => { this.ActiveControl = null; };
        }

        // HANDLERS - PARENT FORM - KEYBOARD | DRAG & DROP | GOT FOCUS
        private void FormHandlersKeyboard()
        {
            form.KeyDown    += FlyLeaf_KeyDown;
            form.KeyUp      += FlyLeaf_KeyUp;
            form.KeyPress   += FlyLeaf_KeyPress;            
        }
        private void FormHandlersDragDrop()
        {
            form.AllowDrop   = true;
            form.DragDrop   += FlyLeaf_DragDrop;
            form.DragEnter  += FlyLeaf_DragEnter;
        }
        private void FormHandlersChangeFocus()
        {
            tblBar.btnPlay.     GotFocus += (o, e) => { form.ActiveControl = null; };
            tblBar.btnPlaylist. GotFocus += (o, e) => { form.ActiveControl = null; };
            tblBar.btnSubs.     GotFocus += (o, e) => { form.ActiveControl = null; };
            tblBar.seekBar.     GotFocus += (o, e) => { form.ActiveControl = null; };
            tblBar.btnMute.     GotFocus += (o, e) => { form.ActiveControl = null; };
            tblBar.volBar.      GotFocus += (o, e) => { form.ActiveControl = null; };
        }

        // HANDLERS - PARENT FORM - MOUSE
        const int MIN_FORM_SIZE             = 230;
        const int RESIZE_CURSOR_DISTANCE    = 6;
        MouseHandler mouseMessageFilter;
        Point       displayMMoveLastPos;
        Point       displayMLDownPos;
        int         displayMoveSide;
        int         displayMoveSideCur;
        bool        resizing = false;

        private void FormHandlersMouse()
        {
            mouseMessageFilter = new MouseHandler();
            mouseMessageFilter.TargetForm = form;
            mouseMessageFilter.flyLeaf = this;
            Application.AddMessageFilter(mouseMessageFilter);
        }
        
        private class MouseHandler : IMessageFilter {
            public Form TargetForm { get; set; }
            public FlyleafPlayer flyLeaf;
            public bool PreFilterMessage( ref Message m )
            {
                switch (m.Msg)
                {
                    case 0x0200:
                        flyLeaf.WM_MOUSEMOVE_MK_LBUTTON(new MouseEventArgs(m.WParam == (IntPtr)0x0001 ? MouseButtons.Left : MouseButtons.None, 0, Control.MousePosition.X - TargetForm.Location.X, Control.MousePosition.Y - TargetForm.Location.Y, 0));
                        break;
                    case 0x0201:
                        flyLeaf.WM_LBUTTONDOWN(Control.MousePosition.X - TargetForm.Location.X, Control.MousePosition.Y - TargetForm.Location.Y);
                        break;
                    case 0x0202:
                        flyLeaf.WM_LBUTTONUP();
                        break;
                }

                return false;
            }
        }
        
        private void WM_LBUTTONUP()                { if (Form.ActiveForm != form) return; displayMoveSideCur = 0; resizing = false; GoFullActive(); if (lvSubs.Visible) FixLvSubs(true); if (lstMediaFiles.Visible) FixLstMedia(true); }
        private void WM_LBUTTONDOWN(int x, int y)  { displayMLDownPos = new Point(x, y); userFullActivity = DateTime.UtcNow.Ticks; }
        private void WM_MOUSEMOVE_MK_LBUTTON(MouseEventArgs e)
        {
            if (displayMMoveLastPos == e.Location) return;
            displayMMoveLastPos = e.Location;

            if (Form.ActiveForm != form)
            {
                if (displayMoveSide != 0)
                {
                    form.Cursor = Cursors.Default;
                    displayMoveSide = 0;
                    displayMoveSideCur = 0;
                }

                userActivity = DateTime.UtcNow.Ticks;
                return;
            }

            userFullActivity = DateTime.UtcNow.Ticks;

            if (isFullScreen) return;
            
            try
            {
                if (e.Button == MouseButtons.Left) // RESIZING or MOVING
                { 
                    if (displayMoveSide != 0 || displayMoveSideCur != 0)
                    {
                        resizing = true;

                        if (displayMoveSideCur == 0) displayMoveSideCur = displayMoveSide;
                            
                        if (config.video.AspectRatio != ViewPorts.FILL)
                        {
                            float AspectRatio = config.video.AspectRatio == ViewPorts.KEEP ? player.DecoderRatio : player.CustomRatio;

                            // RESIZE FORM [KEEP ASPECT RATIO]
                            if (displayMoveSideCur == 1)
                            {
                                int oldHeight = form.Height;

                                if (form.Width - e.X > MIN_FORM_SIZE && form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    if (e.X > e.Y)
                                    {
                                        int oldWidth = form.Width;
                                        form.Height -= e.Y;
                                        form.Width = (int)(form.Height * AspectRatio);
                                        form.Location = new Point(form.Location.X - (form.Width - oldWidth), form.Location.Y - (form.Height - oldHeight));
                                    }
                                    else
                                    {
                                        form.Width -= e.X;
                                        form.Height = (int)(form.Width / AspectRatio);
                                        form.Location = new Point(form.Location.X + e.X, form.Location.Y - (form.Height - oldHeight));
                                    }
                                }
                                else if (form.Width - e.X > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y - (form.Height - oldHeight));
                                }
                                else if (form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    int oldWidth = form.Width;
                                    form.Height -= e.Y;
                                    form.Width = (int)(form.Height * AspectRatio);
                                    form.Location = new Point(form.Location.X - (form.Width - oldWidth), form.Location.Y - (form.Height - oldHeight));
                                }
                            }
                            else if (displayMoveSideCur == 2)
                            {
                                if (e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 3)
                            {
                                int oldHeight = form.Height;

                                if (form.Height - e.Y > MIN_FORM_SIZE && e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                    form.Location = new Point(form.Location.X, form.Location.Y + (oldHeight - form.Height));
                                }
                                else if (form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    form.Height -= e.Y;
                                    form.Width = (int)(form.Height * AspectRatio);
                                    form.Location = new Point(form.Location.X, form.Location.Y + (oldHeight - form.Height));
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                    form.Location = new Point(form.Location.X, form.Location.Y + (oldHeight - form.Height));
                                }
                            }
                            else if (displayMoveSideCur == 4)
                            {
                                if (form.Width - e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                }
                                else if (form.Width - e.X > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                }
                            }
                            else if (displayMoveSideCur == 5)
                            {
                                if (form.Width - e.X > MIN_FORM_SIZE)
                                {
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                    form.Width -= e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 6)
                            {
                                if (e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height = (int)(form.Width / AspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 7)
                            {
                                if (form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    form.Location = new Point(form.Location.X, form.Location.Y + e.Y);
                                    form.Height -= e.Y;
                                    form.Width = (int)(form.Height * AspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 8)
                            {
                                if (e.Y > MIN_FORM_SIZE)
                                {
                                    form.Height = e.Y;
                                    form.Width = (int)(form.Height * AspectRatio);
                                }
                            }
                        }
                        else
                        {
                            // RESIZE FORM [DONT KEEP ASPECT RATIO]
                            if (displayMoveSideCur == 1)
                            {
                                if (form.Width - e.X > MIN_FORM_SIZE && form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Height -= e.Y;
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y + e.Y);
                                }
                                else if (form.Width - e.X > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                }
                                else if (form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    form.Height -= e.Y;
                                    form.Location = new Point(form.Location.X, form.Location.Y + e.Y);
                                }
                            }
                            else if (displayMoveSideCur == 2)
                            {
                                if (e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height = e.Y;
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                }
                                else if (e.Y > MIN_FORM_SIZE)
                                {
                                    form.Height = e.Y;
                                }
                            }
                            else if (displayMoveSideCur == 3)
                            {
                                if (form.Height - e.Y > MIN_FORM_SIZE && e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                    form.Height -= e.Y;
                                    form.Location = new Point(form.Location.X, form.Location.Y + e.Y);
                                }
                                else if (form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    form.Height -= e.Y;
                                    form.Location = new Point(form.Location.X, form.Location.Y + e.Y);
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    form.Width = e.X;
                                }
                            }
                            else if (displayMoveSideCur == 4)
                            {
                                if (form.Width - e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Height = e.Y;
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                }
                                else if (form.Width - e.X > MIN_FORM_SIZE)
                                {
                                    form.Width -= e.X;
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                }
                                else if (e.Y > MIN_FORM_SIZE)
                                {
                                    form.Height = e.Y;
                                }
                            }
                            else if (displayMoveSideCur == 5)
                            {
                                if (form.Width - e.X > MIN_FORM_SIZE)
                                {
                                    form.Location = new Point(form.Location.X + e.X, form.Location.Y);
                                    form.Width -= e.X;
                                }
                            }
                            else if (displayMoveSideCur == 6)
                            {
                                if (e.X > MIN_FORM_SIZE)
                                    form.Width = e.X;
                            }
                            else if (displayMoveSideCur == 7)
                            {
                                if (form.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    form.Location = new Point(form.Location.X, form.Location.Y + e.Y);
                                    form.Height -= e.Y;
                                }
                            }
                            else if (displayMoveSideCur == 8)
                            {
                                if (e.Y > MIN_FORM_SIZE)
                                    form.Height = e.Y;
                            }
                        }
                    }
                    else
                    {
                        // MOVING FORM WHILE PRESSING LEFT BUTTON
                        if (seeking) return;
                        if(lvSubs.Visible) return;
                        if (config.bar._Visibility != VisibilityMode.Never && tblBar.Visible && e.Y > tblBar.Location.Y) return;
                        form.Location = new Point(form.Location.X + e.X - displayMLDownPos.X, form.Location.Y + e.Y - displayMLDownPos.Y);
                    }
                }
                else
                {
                    // SHOW PROPER CURSOR AT THE EDGES (FOR RESIZING)
                    if (e.X <= RESIZE_CURSOR_DISTANCE && e.Y <= RESIZE_CURSOR_DISTANCE)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeNWSE;
                        displayMoveSide = 1;
                    }
                    else if (e.X + RESIZE_CURSOR_DISTANCE >= form.Width && e.Y + RESIZE_CURSOR_DISTANCE >= form.Height)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeNWSE;
                        displayMoveSide = 2;
                    }
                    else if (e.X + RESIZE_CURSOR_DISTANCE >= form.Width && e.Y <= RESIZE_CURSOR_DISTANCE)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeNESW;
                        displayMoveSide = 3;
                    }
                    else if (e.X <= RESIZE_CURSOR_DISTANCE && e.Y + RESIZE_CURSOR_DISTANCE >= form.Height)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeNESW;
                        displayMoveSide = 4;
                    }
                    else if (e.X <= RESIZE_CURSOR_DISTANCE)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeWE;
                        displayMoveSide = 5;
                    }
                    else if (e.X + RESIZE_CURSOR_DISTANCE >= form.Width)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeWE;
                        displayMoveSide = 6;
                    }
                    else if (e.Y <= RESIZE_CURSOR_DISTANCE)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeNS;
                        displayMoveSide = 7;
                    }
                    else if (e.Y + RESIZE_CURSOR_DISTANCE >= form.Height)
                    {
                        if (displayMoveSideCur == 0) form.Cursor = Cursors.SizeNS;
                        displayMoveSide = 8;
                    }
                    else
                    {
                        if (displayMoveSideCur == 0)
                        {
                            form.Cursor = Cursors.Default;
                            displayMoveSide = 0;
                        }
                    }
                }
            } 
            catch (Exception) { }
        }
        #endregion

        #region Properties
        public bool isWPF = false;

        FrmSettings frmSettings;
        SettingsLoad systemDefault;
        SettingsLoad userDefault;

        [Browsable(false)]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
        public Settings         config              { get; set; }

        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        [DisplayName("FL Main")]
        public Settings.Main    _main               { get { return config.main;     } set { config.main     = value; } }

        [DisplayName("FL Keys")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Keys    _keys               { get { return config.keys;     } set { config.keys     = value; } }

        [DisplayName("FL Bar")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Bar     _bar                { get { return config.bar;      } set { config.bar      = value; } }

        [DisplayName("FL Single Mode")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.HookForm _form              { get { return config.hookForm; } set { config.hookForm = value; } }

        [DisplayName("FL Audio")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Audio   _audio              { get { return config.audio;    } set { config.audio = value; } }

        [DisplayName("FL Subtitles")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Subtitles _subtitles        { get { return config.subtitles;} set { config.subtitles = value; } }

        [DisplayName("FL Video")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Video   _video              { get { return config.video;    } set { config.video      = value; } }

        [DisplayName("FL Msg Time")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message0           { get { return config.messages[0]; } set { config.messages[0] = value; } }

        [DisplayName("FL Msg HardwareAcceleration")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message1           { get { return config.messages[1]; } set { config.messages[1] = value; } }

        [DisplayName("FL Msg Volume")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message2           { get { return config.messages[2]; } set { config.messages[2] = value; } }

        [DisplayName("FL Msg Mute")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message3           { get { return config.messages[3]; } set { config.messages[3] = value; } }

        [DisplayName("FL Msg Open")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message4           { get { return config.messages[4]; } set { config.messages[4] = value; } }

        [DisplayName("FL Msg Play")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message5           { get { return config.messages[5]; } set { config.messages[5] = value; } }

        [DisplayName("FL Msg Paused")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message6           { get { return config.messages[6]; } set { config.messages[6] = value; } }

        [DisplayName("FL Msg Buffering")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message7           { get { return config.messages[7]; } set { config.messages[7] = value; } }

        [DisplayName("FL Msg Failed")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message8           { get { return config.messages[8]; } set { config.messages[8] = value; } }

        [DisplayName("FL Msg AudioDelay")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message9           { get { return config.messages[9]; } set { config.messages[9] = value; } }

        [DisplayName("FL Msg SubsDelay")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message10          { get { return config.messages[10]; } set { config.messages[10] = value; } }

        [DisplayName("FL Msg SubsHeight")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message11          { get { return config.messages[11]; } set { config.messages[11] = value; } }

        [DisplayName("FL Msg SubsFontSize")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message12          { get { return config.messages[12]; } set { config.messages[12] = value; } }

        [DisplayName("FL Msg Subtitles")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message13          { get { return config.messages[13]; } set { config.messages[13] = value; } }

        [DisplayName("FL Msg TorrentStats")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message14          { get { return config.messages[14]; } set { config.messages[14] = value; } }

        [DisplayName("FL Msg TopLeft")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message15          { get { return config.messages[15]; } set { config.messages[15] = value; } }

        [DisplayName("FL Msg TopLeft2")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message16          { get { return config.messages[16]; } set { config.messages[16] = value; } }

        [DisplayName("FL Msg TopRight")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message17          { get { return config.messages[17]; } set { config.messages[17] = value; } }

        [DisplayName("FL Msg TopRight2")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message18          { get { return config.messages[18]; } set { config.messages[18] = value; } }

        [DisplayName("FL Msg BottomLeft")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message19          { get { return config.messages[19]; } set { config.messages[19] = value; } }

        [DisplayName("FL Msg BottomRight")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Message _message20          { get { return config.messages[20]; } set { config.messages[20] = value; } }

        [DisplayName("FL Surface TopLeft")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface1           { get { return config.surfaces[0]; } set { config.surfaces[0] = value; } }

        [DisplayName("FL Surface TopRight")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface2           { get { return config.surfaces[1]; } set { config.surfaces[1] = value; } }

        [DisplayName("FL Surface TopLeft 2")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface3           { get { return config.surfaces[2]; } set { config.surfaces[2] = value; } }

        [DisplayName("FL Surface TopRight 2")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface4           { get { return config.surfaces[3]; } set { config.surfaces[3] = value; } }

        [DisplayName("FL Surface BottomLeft")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface5           { get { return config.surfaces[4]; } set { config.surfaces[4] = value; } }

        [DisplayName("FL Surface BottomRight")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface6           { get { return config.surfaces[5]; } set { config.surfaces[5] = value; } }

        [DisplayName("FL Surface BootomCenter")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content)]
        public Settings.Surface _surface7           { get { return config.surfaces[6]; } set { config.surfaces[6] = value; } }

        [DisplayName("FL Surface All")]
        [DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
        public Settings.SurfacesAll _surfaceAll     { get { return config.surfacesAll; } set {config.surfacesAll = value; } }
        #endregion

        #region Misc
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmSettings.Show();
        }
        private void subtitlesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BtnSubs_Click(sender, e);
        }
        private void fileListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BtnPlaylist_Click(sender, e);
        }
        #endregion
    }
}