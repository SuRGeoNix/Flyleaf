using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Color     = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Point     = System.Drawing.Point;


namespace PartyTime.UI_Example
{
    public class UserInterface
    {
        Form                    display;
        frmDisplay              display2;
        frmControl              control;

        MediaRouter             player;
        AudioPlayer             audioPlayer;

        GraphicsDeviceManager   graphics;
        SpriteBatch             spriteBatch;
        Texture2D               screenPlay;
        Button                  btnBar;

        Thread                  openSilently;

        Size                    displayLastSize;
        Point                   displayLastPos;
        Point                   displayMMoveLastPos;
        Point                   displayMLDownPos;
        bool                    displayMLDown;
        int                     displayMoveSide;
        int                     displayMoveSideCur;

        bool                    seekBarMLDown;
        int                     lastSeekBarMLDown   =    0;
        long                    lastUserActionTicks =    0;
        long                    lastInfoTextUpdate  =    0;
        long                    seekClockTime       =   -1;
        int                     seekSum;
        int                     seekStep;

        float                   aspectRatio; 
        bool                    aspectRatioKeep     = true;
        string                  updatedInfoText     =   "";
        string                  subText             =   "";
        long                    subStart            =    0;
        int                     subDur              =    0;
        bool                    SubsNew             = false;
        byte[]                  firstFrameData;

        // Constants
        const int SEEK_FIRST_STEP_MS                = 5000;
        const int SEEK_STEP_MS                      =  500;
        const int AUDIO_STEP_MS                     =   20;
        const int SUBS_STEP_MS                      =  100;
        const int VOL_STEP_PERCENT                  =    5;

        const int MIN_FORM_SIZE                     =  230; // On Resize
        const int RESIZE_CURSOR_DISTANCE            =    6;
        const int IDLE_TIME_MS                      = 7000; // Hide Cursor / Clock etc..
        const int INFO_TIME_MS                      = 2000; // Show Audio/Subs/Volume Adjustments Info
        const int NAUDIO_DELAY_MS                   =  -80; // Not sure if we still need this, just sounds more accurate with -80 ms

        System.Drawing.Color TRANSPARENCY_KEY_COLOR = System.Drawing.Color.FromArgb(1,1,1); // Form Control Transparency

        // Constructors
        public UserInterface(frmDisplay form, ref GraphicsDeviceManager gdm, ref SpriteBatch sb)
        {
            display2    = form;
            graphics    = gdm;
            spriteBatch = sb;

            display     = Control.FromHandle(display2.Window.Handle).FindForm();
            control     = new frmControl();

            Initialize();

            display.Show();
            control.Show(display);
            NormalScreen();
            UnIdle();
        }
        private void Initialize()
        {
            // Players
            player                  = new MediaRouter(1);
            audioPlayer             = new AudioPlayer();
            player.VideoFrameClbk   = VideoFrameClbk;
            player.AudioFrameClbk   = audioPlayer.FrameClbk;
            player.AudioResetClbk   = audioPlayer.ResetClbk;
            player.SubFrameClbk     = SubsFrameClbk;
            player.HWAcceleration   = false;
            player.HighQuality      = false;

            // Forms
            SubscribeEvents();
            InitializeDisplay();
            InitializeControl();
        }
        private void InitializeDisplay()
        {
            // Textures
            screenPlay = new Texture2D(graphics.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);

            btnBar                              = new Button();
            btnBar.Enabled                      = false;
            btnBar.TabStop                      = false;
            btnBar.UseVisualStyleBackColor      = false;
            btnBar.Height                       = 45;
            btnBar.BackColor                    = System.Drawing.Color.FromArgb(0x26,0x28,0x2b);
            display.Controls.Add(btnBar);

            // Form
            display.FormBorderStyle             = FormBorderStyle.None;
            display.BackColor                   = System.Drawing.Color.Black;
            graphics.PreferredBackBufferWidth   = graphics.GraphicsDevice.DisplayMode.Width / 2;
            graphics.PreferredBackBufferHeight  = graphics.PreferredBackBufferWidth / 2;

            aspectRatio                         = graphics.GraphicsDevice.Viewport.AspectRatio;
            FixAspectRatio();

            display.Location                    = new Point(display2.Window.ClientBounds.Width - (display.Width / 2), display2.Window.ClientBounds.Height - (display.Height / 2));
            displayLastSize                     = new Size(graphics.PreferredBackBufferWidth, graphics.PreferredBackBufferHeight);
            displayLastPos                      = display.Location;
            
            graphics.ApplyChanges();
        }
        private void InitializeControl()
        {
            // Form
            control.BackColor                   = TRANSPARENCY_KEY_COLOR;
            control.TransparencyKey             = TRANSPARENCY_KEY_COLOR;

            // Controls
            control.seekBar.BackColor           = TRANSPARENCY_KEY_COLOR;
            control.seekBar.Height              =  40;
            control.seekBar.Minimum             =   0;
            control.seekBar.Maximum             =   1;
            control.seekBar.ScaleDivisions      =   1;
            control.seekBar.ScaleSubDivisions   =   1;
            control.seekBar.SmallChange         =   1;
            control.seekBar.LargeChange         =   1;
            control.seekBar.Value               =   0;

            control.volBar.BackColor            = TRANSPARENCY_KEY_COLOR;
            control.volBar.Height               =  40;
            control.volBar.Minimum              =   0;
            control.volBar.Maximum              = 100;
            control.volBar.ScaleDivisions       =   1;
            control.volBar.ScaleSubDivisions    =   1;
            control.volBar.SmallChange          =   1;
            control.volBar.LargeChange          =   1;
            control.volBar.Value                = audioPlayer.Volume;

            control.lblInfoText.Location        = new Point(10, 10);
            control.lblInfoText.BackColor       = System.Drawing.Color.FromArgb(0x26,0x28,0x2b);

            control.rtbSubs.BackColor           = TRANSPARENCY_KEY_COLOR;
            control.rtbSubs.SendToBack();
        }
        private void SubscribeEvents()
        {
            display.AllowDrop                = true;
            display.DragEnter               += new DragEventHandler         (Display_DragEnter);
            display.DragDrop                += new DragEventHandler         (Display_DragDrop);
            display.MouseDown               += new MouseEventHandler        (Display_MouseDown);
            display.MouseMove               += new MouseEventHandler        (Display_MouseMove);
            display.MouseUp                 += new MouseEventHandler        (Display_MouseUp);
            display.MouseDoubleClick        += new MouseEventHandler        (Display_MouseClickDbl);
            display.KeyDown                 += new KeyEventHandler          (Display_KeyDown);
            display.KeyUp                   += new KeyEventHandler          (Display_KeyUp);
            display.KeyPress                += new KeyPressEventHandler     (Display_KeyPress);
            display.FormClosing             += new FormClosingEventHandler  (Display_Closing);

            control.seekBar.MouseDown       += new MouseEventHandler        (SeekBar_MouseDown);
            control.seekBar.MouseMove       += new MouseEventHandler        (SeekBar_MouseMove);
            control.seekBar.MouseUp         += new MouseEventHandler        (SeekBar_MouseUp);

            control.volBar.MouseUp          += new MouseEventHandler        (VolBar_MouseUp);
            control.volBar.ValueChanged     += new EventHandler             (VolBar_ValueChanged);

            control.rtbSubs.MouseDown       += new MouseEventHandler        (Display_MouseDown);
            control.rtbSubs.MouseMove       += new MouseEventHandler        (Display_MouseMove);
            control.rtbSubs.MouseUp         += new MouseEventHandler        (Display_MouseUp);
            control.rtbSubs.MouseDoubleClick += new MouseEventHandler       (Display_MouseClickDbl);
            control.rtbSubs.ContentsResized += new ContentsResizedEventHandler (RtbSubsContentsResized);
            control.rtbSubs.Enter           += new EventHandler             (RtbSubsEnter);
        }

        // Screens
        private void ScreenPlay(string infoText = "")
        {
            try
            {
                lock(screenPlay)
                {
                    graphics.GraphicsDevice.Clear(Color.Black);
                    spriteBatch.Begin();
                    if (display.Width == graphics.GraphicsDevice.DisplayMode.Width)
                    {
                        spriteBatch.Draw(screenPlay, new Rectangle(0, (int)(display.Height - (display.Width / aspectRatio)) / 2, display.Width, (int)(display.Width / aspectRatio)), Color.White);
                    }
                    else
                    {
                        spriteBatch.Draw(screenPlay, new Rectangle(0, 0, display.Width, display.Height), Color.White);
                    }
                    spriteBatch.End();
                    graphics.GraphicsDevice.Present();
                }
            }
            catch (Exception e)
            {
                if (e.Message.Equals("Text contains characters that cannot be resolved by this SpriteFont.\r\nParameter name: text")) spriteBatch = new SpriteBatch(graphics.GraphicsDevice);
                Log(e.Message + " - " + e.StackTrace);
            }
        }
        private void ScreenStop()
        {
            try
            {
                graphics.GraphicsDevice.Clear(Color.Black);

                // Screen Play
                if (firstFrameData != null)
                {
                    screenPlay.SetData(firstFrameData);
                    spriteBatch.Begin();
                    if (display.Width == graphics.GraphicsDevice.DisplayMode.Width)
                    {
                        spriteBatch.Draw(screenPlay, new Rectangle(0, (int)(display.Height - (display.Width / aspectRatio)) / 2, display.Width, (int)(display.Width / aspectRatio)), Color.White);
                    }
                    else
                    {
                        spriteBatch.Draw(screenPlay, new Rectangle(0, 0, display.Width, display.Height), Color.White);
                    }
                    spriteBatch.End();
                }

                graphics.GraphicsDevice.Present();
            }
            catch (Exception e)
            {
                if (e.Message.Equals("Text contains characters that cannot be resolved by this SpriteFont.\r\nParameter name: text")) spriteBatch = new SpriteBatch(graphics.GraphicsDevice);
                Log(e.StackTrace);
            }
        }
        public  void UpdateLoop()
        {
            if (IsIdle()) GoIdle();

            // Update Subtitles Show / Hide
            if (player.CurTime >= subStart && player.CurTime - subStart < subDur * 10000)
            {
                if ( SubsNew ) { SubsNew = false; ASSToRichText(control.rtbSubs, subText); }

                if ( IsIdle() )
                    control.rtbSubs.Location = new Point((control.Width / 2) - (control.rtbSubs.Width / 2), control.Height - (control.rtbSubs.Height + 50));
                else
                    control.rtbSubs.Location = new Point((control.Width / 2) - (control.rtbSubs.Width / 2), control.Height - (control.rtbSubs.Height + 80));

            } 
            else 
                control.rtbSubs.Text = "";               

            // Update SeekBar Value = Cur Time
            if (seekClockTime == -1) control.seekBar.Value = (int)(player.CurTime / 10000000);

            // Update Info Text [Adjustments]
            if (seekClockTime == -1)
                if (DateTime.UtcNow.Ticks - lastInfoTextUpdate < INFO_TIME_MS * 10000)
                    FixWidthInfoText(updatedInfoText);

            // Update Into Text [Current Time / Duration]
                else
                    FixWidthInfoText("[" + (new TimeSpan(player.CurTime)).ToString(@"hh\:mm\:ss") + " / " + (new TimeSpan(player.Duration)).ToString(@"hh\:mm\:ss") + "]");
        }

        // Processes
        private void Open(string url)
        {
            int ret;
            bool isSubs = false;

            string ext = url.Substring(url.LastIndexOf(".") + 1);
            if ((new List<string> { "srt", "txt" }).Contains(ext)) isSubs = true;

            if (!isSubs)
            {
                player.Stop();
                if (player.hasAudio) audioPlayer.ResetClbk();

                firstFrameData = null;
                ret = player.Open(url);
                if (ret != 0) { Log("Error opening " + ret); return; }
                if (player.hasVideo)
                {
                    screenPlay  = new Texture2D(spriteBatch.GraphicsDevice, player.Width, player.Height, false, SurfaceFormat.Color);
                    aspectRatio = (float) player.Width / (float) player.Height;
                    FixAspectRatio();
                }
                
                ScreenPlay(); ScreenPlay(); ScreenPlay(); ScreenPlay();

                if (openSilently != null && openSilently.IsAlive) openSilently.Abort();
                openSilently = new Thread(() => 
                { 
                    player.Play(); 
                    if (player.hasAudio) audioPlayer.Play(); 
                });
                openSilently.Start();

                graphics.GraphicsDevice.Clear(Color.Black);
                
                player.AudioExternalDelay = NAUDIO_DELAY_MS * 10000;
                control.seekBar.Maximum = (int)(player.Duration / 10000000);
                control.seekBar.Value = 0;
            }
            else
            {
                // Subtitles
                // Identify by BOM else Check if UTF-8, finally convert from (identified or system default codepage) to UTF-8 (supported by MR/FFmpeg)
                Encoding subsEnc = Subtitles.Detect(url);
                if (subsEnc != Encoding.UTF8) url = Subtitles.Convert(url, subsEnc, Encoding.UTF8);
                if (url == null || url.Trim() == "") return;

                ret = player.OpenSubs(url);
                if (ret != 0) { Log("Error opening " + ret); return; }
            }

        }
        private void Stop()
        {
            if (player != null)
            {
                player.Stop();
                audioPlayer.Stop();
                ScreenStop();
            }
        }
        private void UpdateInfoText(string infoText)
        {
            updatedInfoText = infoText;
            lastInfoTextUpdate = DateTime.UtcNow.Ticks;
        }
        private void FixWidthInfoText(string infoText)
        {
            using (Graphics g = control.lblInfoText.CreateGraphics())
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                control.lblInfoText.Width = (int) g.MeasureString(infoText, control.lblInfoText.Font).Width - 10;
            }
                
            control.lblInfoText.Text = infoText;
        }
        private void UpdateSeekClockTime(long ticks)
        {
            if (ticks / 10000000.0 < control.seekBar.Minimum) ticks = (long)control.seekBar.Minimum * 10000000;
            if (ticks / 10000000.0 > control.seekBar.Maximum) ticks = (long)control.seekBar.Maximum * 10000000;
            seekClockTime = ticks;
            FixWidthInfoText("[" + (new TimeSpan(ticks)).ToString(@"hh\:mm\:ss") + " / " + (new TimeSpan(player.Duration)).ToString(@"hh\:mm\:ss") + "]");
        }
        private bool IsIdle()
        {
            if ((DateTime.UtcNow.Ticks - lastUserActionTicks) / 10000 > IDLE_TIME_MS) return true;

            return false;
        }
        private void GoIdle()
        {
            if (display2.IsMouseVisible)        display2.IsMouseVisible     = false;
            if (control.lblInfoText.Visible)    control.lblInfoText.Visible = false;
            if (control.seekBar.Visible)        control.seekBar.Visible     = false;
            if (control.volBar.Visible)         control.volBar.Visible      = false;
            if (btnBar.Visible)                 btnBar.Visible              = false;
        }
        private void UnIdle()
        {
            lastUserActionTicks = DateTime.UtcNow.Ticks;
            if (!display2.IsMouseVisible)       display2.IsMouseVisible     = true;
            if (!control.lblInfoText.Visible)   control.lblInfoText.Visible = true;
            if (!control.seekBar.Visible)       control.seekBar.Visible     = true;
            if (!control.volBar.Visible)        control.volBar.Visible      = true;
            if (!btnBar.Visible)                btnBar.Visible              = true;
            display.Focus();
        }
        private void ASSToRichText(RichTextBox rtb, string text)
        {
            rtb.Text = "";

            bool bold = false;
            bool italic = false;
            bool changed = false;
            string cur = "";
            FontStyle fontStyle = FontStyle.Regular;

            for (int i=0; i<text.Length; i++)
            {

                if ( changed )
                {
                    if ( !bold && !italic ) fontStyle = FontStyle.Regular;
                    else
                    if (  bold &&  italic ) fontStyle = FontStyle.Bold | FontStyle.Italic;
                    else
                    if (  bold && !italic ) fontStyle = FontStyle.Bold;
                    else
                    if ( !bold &&  italic ) 
                        fontStyle = FontStyle.Italic;

                    rtb.AppendText(cur);

                    cur = "";
                    changed = false;
                    rtb.SelectionFont = new Font(rtb.Font, fontStyle); 
                }

                if ( text.Length > i + 4 && text[i] == '{' &&  text[i+1] == '\\')
                {
                    if      ( text[i+2] == 'i' && (text[i+3] == '0' || text[i+3] == '1') && text[i+4] == '}' )
                    {
                        italic = text[i+3] == '1' ? true : false;
                        changed = true;
                        i += 4;
                    } 
                    else if ( text[i+2] == 'b' && (text[i+3] == '0' || text[i+3] == '1') && text[i+4] == '}' )
                    {
                        bold = text[i+3] == '1' ? true : false;
                        changed = true;
                        i += 4;
                    }
                }

                if ( !changed ) cur += text[i];

            }

            rtb.AppendText(cur);
            rtb.SelectAll();
            rtb.SelectionAlignment = HorizontalAlignment.Center;
        }

        // UI Callbacks
        private void VideoFrameClbk(byte[] frameData, long timestamp, long screamDistanceTicks)
        {
            if (frameData       == null)    return;
            if (firstFrameData  == null)    firstFrameData   = frameData;
            lock (screenPlay)               screenPlay.SetData(frameData);
            ScreenPlay();
        }
        private void SubsFrameClbk(string text, int duration)
        {
            try
            {
                // Lazy ASS -> TXT
                subText     = text.Substring(text.LastIndexOf(",,") + 2).Replace("\\N", "\n").Trim();
                subStart    = player.CurTime;
                subDur      = duration;
                SubsNew     = true;
            }
            catch (Exception e)
            {
                Log("SubsFrameClbk-> " + e.StackTrace);
            }
        }

        // UI Events [DISPLAY KEYBOARD]
        private void Display_KeyDown(object sender, KeyEventArgs e)
        {
            lastUserActionTicks = DateTime.UtcNow.Ticks;
            UnIdle();

            switch (e.KeyCode)
            {
                // SEEK
                case Keys.Right:
                    if (!player.isReady) return;

                    seekStep++;
                    if (seekStep == 1) { seekClockTime = player.CurTime; seekSum = SEEK_FIRST_STEP_MS; }
                    seekSum += seekStep * SEEK_STEP_MS;

                    UpdateSeekClockTime(seekClockTime + ((long)seekSum * 10000));
                    control.seekBar.Value = (int)(seekClockTime / 10000000);

                    break;
                case Keys.Left:
                    if (!player.isReady) return;

                    seekStep++;
                    if (seekStep == 1) { seekClockTime = player.CurTime; seekSum = -SEEK_FIRST_STEP_MS; }
                    seekSum -= seekStep * SEEK_STEP_MS;

                    UpdateSeekClockTime(seekClockTime + ((long)seekSum * 10000));
                    control.seekBar.Value = (int)(seekClockTime / 10000000);

                    break;

                // AUDIO
                case Keys.OemOpenBrackets:
                    if (!player.hasAudio) return;

                    player.AudioExternalDelay -= AUDIO_STEP_MS * 10000;
                    UpdateInfoText("[AUDIO DELAY] " + player.AudioExternalDelay / 10000);

                    break;

                case Keys.OemCloseBrackets:
                    if (!player.hasAudio) return;

                    player.AudioExternalDelay += AUDIO_STEP_MS * 10000;
                    UpdateInfoText("[AUDIO DELAY] " + player.AudioExternalDelay / 10000);

                    break;

                // SUBTITLES
                case Keys.OemSemicolon:
                    if (!player.hasSubs) return;

                    player.SubsExternalDelay -= SUBS_STEP_MS * 10000;
                    UpdateInfoText("[SUBS DELAY] " + player.SubsExternalDelay / 10000);

                    break;

                case Keys.OemQuotes:
                    if (!player.hasSubs) return;

                    player.SubsExternalDelay += SUBS_STEP_MS * 10000;
                    UpdateInfoText("[SUBS DELAY] " + player.SubsExternalDelay / 10000);

                    break;

                // VOLUME
                case Keys.Up:
                    audioPlayer.VolUp   (VOL_STEP_PERCENT);
                    UpdateInfoText("[Volume] " + audioPlayer.Volume + "%");
                    control.volBar.Value = audioPlayer.Volume;
                    break;

                case Keys.Down:
                    audioPlayer.VolDown (VOL_STEP_PERCENT);
                    UpdateInfoText("[Volume] " + audioPlayer.Volume + "%");
                    control.volBar.Value = audioPlayer.Volume;
                    break;
            }
        }
        private void Display_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Right:
                    if (!player.isReady) return;

                    player.Seek((int)(seekClockTime / 10000), false);
                    seekSum = 0; seekStep = 0; seekClockTime = -1;

                    if (!player.isPlaying)
                    {
                        if (openSilently != null && openSilently.IsAlive) openSilently.Abort();
                        openSilently = new Thread(() => { player.Play(); });
                        openSilently.Start();
                        Thread.Sleep(100);
                        player.Pause();
                        audioPlayer.Pause();
                    }

                    break;

                case Keys.Left:
                    if (!player.isReady) return;

                    player.Seek((int)(seekClockTime / 10000), false);
                    seekSum = 0; seekStep = 0; seekClockTime = -1;

                    if (!player.isPlaying)
                    {
                        if (openSilently != null && openSilently.IsAlive) openSilently.Abort();
                        openSilently = new Thread(() => { player.Play(); });
                        openSilently.Start();
                        Thread.Sleep(100);
                        player.Pause();
                        audioPlayer.Pause();
                    }

                    break;
            }
        }
        private void Display_KeyPress(object sender, KeyPressEventArgs e)
        {
            switch (Char.ToUpper(e.KeyChar))
            {
                case (char)Keys.F:
                    FullScreenToggle();
                    break;

                case (char)Keys.Space:
                case (char)Keys.P:
                    if (player.isPlaying) { player.Pause(); audioPlayer.Pause(); } else { player.Play(); audioPlayer.Play(); }
                    break;

                case (char)Keys.S:
                    Stop();
                    break;

                case (char)Keys.R:
                    aspectRatioKeep = !aspectRatioKeep;
                    if (aspectRatioKeep) FixAspectRatio();
                    break;

                case (char)Keys.Escape:
                    NormalScreen();
                    break;
            }
        }

        // UI Events [DISPLAY DRAG]
        private void Display_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // VIDEO / AUDIO / SUBTITLE FILES
                display.Cursor = Cursors.Default;
                string[] filenames = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                if (filenames.Length > 0) Open(filenames[0]);
            }

            display.Focus();
            display.Activate();
            display.Show();
        }
        private void Display_DragEnter(object sender, DragEventArgs e)      { e.Effect = DragDropEffects.All; lastUserActionTicks = DateTime.UtcNow.Ticks; }

        // UI Events [DISPLAY MOUSE]
        private void Display_MouseDown(object sender, MouseEventArgs e)     { if (e.Button == MouseButtons.Left) { displayMLDown = true; displayMLDownPos = e.Location; } }
        private void Display_MouseUp(object sender, MouseEventArgs e)       { if (e.Button == MouseButtons.Left) { displayMLDown = false; displayMoveSideCur = 0; UnIdle(); FixFrmControl(); } }
        private void Display_MouseClickDbl(object sender, MouseEventArgs e) { if (e.Clicks >= 2 && e.Button == MouseButtons.Left) FullScreenToggle(); }
        private void Display_MouseMove(object sender, MouseEventArgs e)
        {
            if (displayMMoveLastPos == e.Location) return;

            displayMMoveLastPos = e.Location;

            if (!display2.IsMouseVisible && !displayMLDown) UnIdle();

            if (graphics.IsFullScreen || graphics.PreferredBackBufferWidth == graphics.GraphicsDevice.DisplayMode.Width) return;

            if (displayMLDown) // RESIZING or MOVING
            {
                if (displayMoveSide != 0 || displayMoveSideCur != 0)
                {
                    lock (screenPlay)
                    {
                        if (displayMoveSideCur == 0) displayMoveSideCur = displayMoveSide;

                        if (aspectRatioKeep)
                        {
                            // RESIZE FORM [KEEP ASPECT RATIO]
                            if (displayMoveSideCur == 1)
                            {
                                int oldHeight = display.Height;

                                if (display.Width - e.X > MIN_FORM_SIZE && display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    if (e.X > e.Y)
                                    {
                                        int oldWidth = display.Width;
                                        display.Height -= e.Y;
                                        display.Width = (int)(display.Height * aspectRatio);
                                        display.Location = new Point(display.Location.X - (display.Width - oldWidth), display.Location.Y - (display.Height - oldHeight));
                                    }
                                    else
                                    {
                                        display.Width -= e.X;
                                        display.Height = (int)(display.Width / aspectRatio);
                                        display.Location = new Point(display.Location.X + e.X, display.Location.Y - (display.Height - oldHeight));
                                    }
                                }
                                else if (display.Width - e.X > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y - (display.Height - oldHeight));
                                }
                                else if (display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    int oldWidth = display.Width;
                                    display.Height -= e.Y;
                                    display.Width = (int)(display.Height * aspectRatio);
                                    display.Location = new Point(display.Location.X - (display.Width - oldWidth), display.Location.Y - (display.Height - oldHeight));
                                }
                            }
                            else if (displayMoveSideCur == 2)
                            {
                                if (e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 3)
                            {
                                int oldHeight = display.Height;

                                if (display.Height - e.Y > MIN_FORM_SIZE && e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                    display.Location = new Point(display.Location.X, display.Location.Y + (oldHeight - display.Height));
                                }
                                else if (display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    display.Height -= e.Y;
                                    display.Width = (int)(display.Height * aspectRatio);
                                    display.Location = new Point(display.Location.X, display.Location.Y + (oldHeight - display.Height));
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                    display.Location = new Point(display.Location.X, display.Location.Y + (oldHeight - display.Height));
                                }
                            }
                            else if (displayMoveSideCur == 4)
                            {
                                if (display.Width - e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                }
                                else if (display.Width - e.X > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                }
                            }
                            else if (displayMoveSideCur == 5)
                            {
                                if (display.Width - e.X > MIN_FORM_SIZE)
                                {
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                    display.Width -= e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 6)
                            {
                                if (e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height = (int)(display.Width / aspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 7)
                            {
                                if (display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    display.Location = new Point(display.Location.X, display.Location.Y + e.Y);
                                    display.Height -= e.Y;
                                    display.Width = (int)(display.Height * aspectRatio);
                                }
                            }
                            else if (displayMoveSideCur == 8)
                            {
                                if (e.Y > MIN_FORM_SIZE)
                                {
                                    display.Height = e.Y;
                                    display.Width = (int)(display.Height * aspectRatio);
                                }
                            }
                        }
                        else
                        {
                            // RESIZE FORM [DONT KEEP ASPECT RATIO]
                            if (displayMoveSideCur == 1)
                            {
                                if (display.Width - e.X > MIN_FORM_SIZE && display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Height -= e.Y;
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y + e.Y);
                                }
                                else if (display.Width - e.X > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                }
                                else if (display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    display.Height -= e.Y;
                                    display.Location = new Point(display.Location.X, display.Location.Y + e.Y);
                                }
                            }
                            else if (displayMoveSideCur == 2)
                            {
                                if (e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height = e.Y;
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                }
                                else if (e.Y > MIN_FORM_SIZE)
                                {
                                    display.Height = e.Y;
                                }
                            }
                            else if (displayMoveSideCur == 3)
                            {
                                if (display.Height - e.Y > MIN_FORM_SIZE && e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                    display.Height -= e.Y;
                                    display.Location = new Point(display.Location.X, display.Location.Y + e.Y);
                                }
                                else if (display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    display.Height -= e.Y;
                                    display.Location = new Point(display.Location.X, display.Location.Y + e.Y);
                                }
                                else if (e.X > MIN_FORM_SIZE)
                                {
                                    display.Width = e.X;
                                }
                            }
                            else if (displayMoveSideCur == 4)
                            {
                                if (display.Width - e.X > MIN_FORM_SIZE && e.Y > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Height = e.Y;
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                }
                                else if (display.Width - e.X > MIN_FORM_SIZE)
                                {
                                    display.Width -= e.X;
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                }
                                else if (e.Y > MIN_FORM_SIZE)
                                {
                                    display.Height = e.Y;
                                }
                            }
                            else if (displayMoveSideCur == 5)
                            {
                                if (display.Width - e.X > MIN_FORM_SIZE)
                                {
                                    display.Location = new Point(display.Location.X + e.X, display.Location.Y);
                                    display.Width -= e.X;
                                }
                            }
                            else if (displayMoveSideCur == 6)
                            {
                                if (e.X > MIN_FORM_SIZE)
                                    display.Width = e.X;
                            }
                            else if (displayMoveSideCur == 7)
                            {
                                if (display.Height - e.Y > MIN_FORM_SIZE)
                                {
                                    display.Location = new Point(display.Location.X, display.Location.Y + e.Y);
                                    display.Height -= e.Y;
                                }
                            }
                            else if (displayMoveSideCur == 8)
                            {
                                if (e.Y > MIN_FORM_SIZE)
                                    display.Height = e.Y;
                            }
                        }

                        ScreenPlay();
                        lock (screenPlay) GoIdle();
                    }

                }
                else
                {
                    // MOVING FORM WHILE PRESSING LEFT BUTTON
                    display.Location = new Point(display.Location.X + e.Location.X - displayMLDownPos.X, display.Location.Y + e.Location.Y - displayMLDownPos.Y);
                    control.Location = display.Location;
                }

                //lock (screenPlay) FixFrmControl();
            }
            else
            {
                // SHOW PROPER CURSOR AT THE EDGES (FOR RESIZING)
                if (e.X <= RESIZE_CURSOR_DISTANCE && e.Y <= RESIZE_CURSOR_DISTANCE)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeNWSE;
                    displayMoveSide = 1;
                }
                else if (e.X + RESIZE_CURSOR_DISTANCE >= display.Width && e.Y + RESIZE_CURSOR_DISTANCE >= display.Height)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeNWSE;
                    displayMoveSide = 2;
                }
                else if (e.X + RESIZE_CURSOR_DISTANCE >= display.Width && e.Y <= RESIZE_CURSOR_DISTANCE)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeNESW;
                    displayMoveSide = 3;
                }
                else if (e.X <= RESIZE_CURSOR_DISTANCE && e.Y + RESIZE_CURSOR_DISTANCE >= display.Height)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeNESW;
                    displayMoveSide = 4;
                }
                else if (e.X <= RESIZE_CURSOR_DISTANCE)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeWE;
                    displayMoveSide = 5;
                }
                else if (e.X + RESIZE_CURSOR_DISTANCE >= display.Width)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeWE;
                    displayMoveSide = 6;
                }
                else if (e.Y <= RESIZE_CURSOR_DISTANCE)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeNS;
                    displayMoveSide = 7;
                }
                else if (e.Y + RESIZE_CURSOR_DISTANCE >= display.Height)
                {
                    if (displayMoveSideCur == 0) display.Cursor = Cursors.SizeNS;
                    displayMoveSide = 8;
                }
                else
                {
                    if (displayMoveSideCur == 0)
                    {
                        display.Cursor = Cursors.Default;
                        displayMoveSide = 0;
                    }

                }
            }
        }

        // UI Events [DISPLAY CLOSE]
        private void Display_Closing(object sender, FormClosingEventArgs e) { player.Stop(); }

        // UI Events [CONTROL SEEKBAR]
        private void SeekBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) seekBarMLDown = false;

            UpdateSeekClockTime((long)(lastSeekBarMLDown) * 10000000);
            control.seekBar.Value   =  lastSeekBarMLDown;
            if ( player.hasAudio) audioPlayer.ResetClbk(); // Clear Buffer For Silence During Seeking
            player.Seek(lastSeekBarMLDown * 1000, false);
            seekClockTime           =  -1;
            display.Focus();
        }
        private void SeekBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                seekBarMLDown           =  true;
                lastUserActionTicks     =  DateTime.UtcNow.Ticks;
                lastSeekBarMLDown       =  control.seekBar.Value;
                UpdateSeekClockTime((long)(control.seekBar.Value) * 10000000);
            }
        }
        private void SeekBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (seekBarMLDown)
            {
                lastUserActionTicks     =  DateTime.UtcNow.Ticks;
                lastSeekBarMLDown       =  control.seekBar.Value;
                UpdateSeekClockTime((long)(control.seekBar.Value) * 10000000);
            }
        }

        // UI Events [CONTROL VOLBAR]
        private void VolBar_ValueChanged(object sender, EventArgs e)
        {
            UpdateInfoText("[VOLUME] " + control.volBar.Value + "%");
            audioPlayer.Volume = control.volBar.Value;
          
        }
        private void VolBar_MouseUp(object sender, MouseEventArgs e) { display.Focus(); }

        // UI Events [CONTROL RTBSUBS]
        private void RtbSubsContentsResized(object sender, ContentsResizedEventArgs e)  { control.rtbSubs.ClientSize = new Size(control.rtbSubs.ClientSize.Width, e.NewRectangle.Height); }
        private void RtbSubsEnter(object sender, EventArgs e) { control.seekBar.Focus(); }

        // Full Screen / Aspect Ratio
        private void FullScreenToggle() { if (graphics.PreferredBackBufferWidth == graphics.GraphicsDevice.DisplayMode.Width) NormalScreen(); else FullScreen(); }
        private void FullScreen()
        {
            if (graphics.PreferredBackBufferWidth != graphics.GraphicsDevice.DisplayMode.Width)
            {
                displayLastPos                  = display.Location;
                displayLastSize.Width           = graphics.PreferredBackBufferWidth;
                displayLastSize.Height          = graphics.PreferredBackBufferHeight;
            }

            graphics.PreferredBackBufferWidth   = graphics.GraphicsDevice.DisplayMode.Width;
            graphics.PreferredBackBufferHeight  = graphics.GraphicsDevice.DisplayMode.Height;
            display.Location = new Point(0, 0);
            graphics.ApplyChanges();

            FixFrmControl();

            if (player.isStopped) ScreenStop(); else ScreenPlay();
        }
        private void NormalScreen()
        {
            graphics.PreferredBackBufferWidth   = displayLastSize.Width;
            graphics.PreferredBackBufferHeight  = displayLastSize.Height;
            display.Location                    = displayLastPos;
            graphics.ApplyChanges();

            FixFrmControl();

            if (player.isStopped) ScreenStop(); else ScreenPlay();
        }
        private void FixAspectRatio(bool force = true)
        {
            lock (screenPlay)
            {
                if (display.Width == graphics.GraphicsDevice.DisplayMode.Width) return;

                if (display2.Window.ClientBounds.Width != displayLastSize.Width || force)
                {
                    graphics.PreferredBackBufferWidth   = display2.Window.ClientBounds.Width;
                    graphics.PreferredBackBufferHeight  = (int)(display2.Window.ClientBounds.Width / aspectRatio);
                }
                else if (display2.Window.ClientBounds.Height != displayLastSize.Height)
                {
                    graphics.PreferredBackBufferWidth   = (int)(display2.Window.ClientBounds.Height * aspectRatio);
                    graphics.PreferredBackBufferHeight  = display2.Window.ClientBounds.Height;
                }

                displayLastSize = new Size(display2.Window.ClientBounds.Width, display2.Window.ClientBounds.Height);
                graphics.ApplyChanges();
            }

            FixFrmControl();
        }
        private void FixFrmControl()
        {
            control.Width       = display.Width;
            control.Height      = display.Height;
            control.Location    = display.Location;
            btnBar.Width        = display.Width + 20;
            btnBar.Location     = new Point(-10, display.Height - (btnBar.Height - 5) );

            control.seekBar.Width       = display.Width - 135;
            control.seekBar.Location    = new Point(15, display.Height - control.seekBar.Height);

            control.volBar.Width        = 70;
            control.volBar.Location     = new Point(control.seekBar.Location.X + control.seekBar.Width + 35, control.seekBar.Location.Y);
        }

        // Logging
        private void Log(string msg) { Console.WriteLine(msg); }
    }
}
