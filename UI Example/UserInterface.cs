using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Color     = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Texture2D = Microsoft.Xna.Framework.Graphics.Texture2D;

using Point     = System.Drawing.Point;

using SharpDX.DXGI;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;

using Device    = SharpDX.Direct3D11.Device;
using Resource  = SharpDX.Direct3D11.Resource;
using STexture2D= SharpDX.Direct3D11.Texture2D;
using SharpDX;
using Message = System.Windows.Forms.Message;

namespace PartyTime.UI_Example
{
    public class UserInterface
    {
        #region Declaration

        Form                    display;
        frmDisplay              display2;
        frmControl              control;

        MediaRouter             player;
        AudioPlayer             audioPlayer;

        GraphicsDeviceManager   graphics;
        SpriteBatch             spriteBatch;
        Texture2D               screenPlay;
        Button                  btnBar;

        Size                    displayLastSize;
        Point                   displayLastPos;
        Point                   displayMMoveLastPos;
        Point                   displayMLDownPos;
        bool                    displayMLDown;
        int                     displayMoveSide;
        int                     displayMoveSideCur;

        bool                    resizing;
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

        System.Drawing.Color TRANSPARENCY_KEY_COLOR = System.Drawing.Color.FromArgb(1,1,1); // Form Control Transparency
        
        List<string> movieExts = new List<string>() { "mp4", "mkv", "mpg", "mpeg" , "mpv", "mp4p", "mpe" , "m2v", "amv" , "asf", "m4v", "3gp", "ogg", "vob", "ts", "rm", "3g2", "f4v", "f4a", "f4p", "f4b", "mts", "m2ts", "gifv", "avi", "mov", "flv", "wmv", "qt", "avchd", "swf"};

        // HW Accelleration | SharpDX | D3D11
        Device                              _device;
        SwapChain                           _swapChain;

        STexture2D                          _backBuffer;

        VideoDevice1                        videoDevice1;
        VideoProcessor                      videoProcessor;
        VideoContext1                       videoContext1;
        VideoProcessorEnumerator vpe;
        VideoProcessorContentDescription    vpcd;
        VideoProcessorOutputViewDescription vpovd;
        VideoProcessorInputViewDescription  vpivd;
        VideoProcessorInputView             vpiv;
        VideoProcessorOutputView            vpov;
        VideoProcessorStream[]              vpsa;
        
        Control                             screenPlayHW;
        IntPtr                              screenPlayHWHandle;
        #endregion

        // Constructors
        public UserInterface(frmDisplay form, ref GraphicsDeviceManager gdm, ref SpriteBatch sb)
        {
            display2    = form;
            graphics    = gdm;
            spriteBatch = sb;

            display     = Control.FromHandle(display2.Window.Handle).FindForm();
            control     = new frmControl();

            Initialize();
        }
        private void Initialize()
        {
            // Players
            player                          = new MediaRouter();
            audioPlayer                     = new AudioPlayer();

            player.HWAccel                  = true;
            player.HighQuality              = false;

            player.VideoFrameClbk           = VideoFrameClbk;
            player.AudioFrameClbk           = audioPlayer.FrameClbk;
            player.AudioResetClbk           = audioPlayer.ResetClbk;
            player.SubFrameClbk             = SubsFrameClbk;

            player.OpenTorrentSuccessClbk   = OpenTorrentSuccess;
            player.OpenStreamSuccessClbk    = OpenStreamSuccess;
            player.MediaFilesClbk           = MediaFilesReceived;

            // Forms
            InitializeDisplay();
            InitializeControl();

            SubscribeEvents();

            FullScreenToggle();
            InitializeID3D11();

            display.Show();
            control.Show(display);
            NormalScreen();
            UnIdle();
        }
        private void InitializeDisplay()
        {
            // Textures
            screenPlay                          = new Texture2D(graphics.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
            screenPlayHW                        = new Control();
            screenPlayHW.Visible                = false;
            screenPlayHW.Enabled                = false;
            screenPlayHW.TabStop                = false;
            screenPlayHWHandle                  = screenPlayHW.Handle;
            display.Controls.Add(screenPlayHW);

            // Bar Background
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

            // Controls | Seek Bar
            control.seekBar.BackColor           = TRANSPARENCY_KEY_COLOR;
            control.seekBar.Height              =  40;
            control.seekBar.Minimum             =   0;
            control.seekBar.Maximum             =   1;
            control.seekBar.ScaleDivisions      =   1;
            control.seekBar.ScaleSubDivisions   =   1;
            control.seekBar.SmallChange         =   1;
            control.seekBar.LargeChange         =   1;
            control.seekBar.Value               =   0;

            // Controls | Volume Bar
            control.volBar.BackColor            = TRANSPARENCY_KEY_COLOR;
            control.volBar.Height               =  40;
            control.volBar.Minimum              =   0;
            control.volBar.Maximum              = 100;
            control.volBar.ScaleDivisions       =   1;
            control.volBar.ScaleSubDivisions    =   1;
            control.volBar.SmallChange          =   1;
            control.volBar.LargeChange          =   1;
            control.volBar.Value                = audioPlayer.Volume;

            // Controls | Info Text
            control.lblInfoText.Location        = new Point(10, 10);
            control.lblInfoText.BackColor       = System.Drawing.Color.FromArgb(0x26,0x28,0x2b);

            // Controls | Subs RTB
            control.rtbSubs.BackColor           = TRANSPARENCY_KEY_COLOR;
            control.rtbSubs.Text                = "";
            control.rtbSubs.SendToBack();
            screenPlayHW.SendToBack();
        }
        private void SubscribeEvents()
        {
            display.AllowDrop                = true;
            display.DragEnter               += new DragEventHandler         (Display_DragEnter);
            display.DragDrop                += new DragEventHandler         (Display_DragDrop);
            display.MouseDown               += new MouseEventHandler        (Display_MouseDown);
            display.MouseMove               += new MouseEventHandler        (Display_MouseMove);
            display.MouseUp                 += new MouseEventHandler        (Display_MouseUp);
            display.MouseClick              += new MouseEventHandler        (Display_MouseClick);
            display.MouseDoubleClick        += new MouseEventHandler        (Display_MouseClickDbl);
            display.KeyDown                 += new KeyEventHandler          (Display_KeyDown);
            display.KeyUp                   += new KeyEventHandler          (Display_KeyUp);
            display.KeyPress                += new KeyPressEventHandler     (Display_KeyPress);
            display.FormClosing             += new FormClosingEventHandler  (Display_Closing);

            control.FormClosing             += new FormClosingEventHandler  (Display_Closing);

            control.seekBar.MouseDown       += new MouseEventHandler        (SeekBar_MouseDown);
            control.seekBar.MouseMove       += new MouseEventHandler        (SeekBar_MouseMove);
            control.seekBar.MouseUp         += new MouseEventHandler        (SeekBar_MouseUp);

            control.volBar.MouseUp          += new MouseEventHandler        (VolBar_MouseUp);
            control.volBar.ValueChanged     += new EventHandler             (VolBar_ValueChanged);

            control.rtbSubs.MouseDown       += new MouseEventHandler        (Display_MouseDown);
            control.rtbSubs.MouseMove       += new MouseEventHandler        (Display_MouseMove);
            control.rtbSubs.MouseUp         += new MouseEventHandler        (Display_MouseUp);
            control.rtbSubs.MouseDoubleClick+= new MouseEventHandler        (Display_MouseClickDbl);
            control.rtbSubs.ContentsResized += new ContentsResizedEventHandler (RtbSubsContentsResized);
            control.rtbSubs.Enter           += new EventHandler             (RtbSubsEnter);

            control.lstMediaFiles.MouseDoubleClick+= new MouseEventHandler  (lstMediaFiles_MouseClickDbl);
            control.lstMediaFiles.KeyPress  += new KeyPressEventHandler     (lstMediaFiles_KeyPress);
            control.lstMediaFiles.DragEnter += new DragEventHandler         (Display_DragEnter);
            control.lstMediaFiles.DragDrop  += new DragEventHandler         (Display_DragDrop);
            control.lstMediaFiles.MouseMove += new MouseEventHandler        (lstMediaFiles_MouseMove);

            screenPlayHW.Resize             += new EventHandler             (screenPlayHW_Resize);
        }
        private void InitializeID3D11()
        {
            // SwapChain Description
            var desc = new SwapChainDescription()
            {
                BufferCount         = 1,
                ModeDescription     = new ModeDescription(0, 0, new Rational(60, 1), Format.B8G8R8A8_UNorm),
                IsWindowed          = true,
                OutputHandle        = screenPlayHWHandle,
                SampleDescription   = new SampleDescription(1, 0),
                SwapEffect          = SwapEffect.Discard,
                Usage               = Usage.RenderTargetOutput
            };

            // Create Device and SwapChain
            //Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, /*DeviceCreationFlags.Debug | */DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, new [] { SharpDX.Direct3D.FeatureLevel.Level_12_1, SharpDX.Direct3D.FeatureLevel.Level_12_0, SharpDX.Direct3D.FeatureLevel.Level_11_1, SharpDX.Direct3D.FeatureLevel.Level_11_0, SharpDX.Direct3D.FeatureLevel.Level_10_1, SharpDX.Direct3D.FeatureLevel.Level_10_0 }, desc, out _device, out _swapChain);
            Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport, desc, out _device, out _swapChain);
            
            _backBuffer     = STexture2D.FromSwapChain<STexture2D>(_swapChain, 0);

            // Ignore all windows events | Prevent Alt + Enter for Fullscreen
            var factory = _swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(screenPlayHWHandle, WindowAssociationFlags.IgnoreAll);
            
            // Prepare Video Processor Emulator | Input | Output | Stream
            videoDevice1    = _device.QueryInterface<VideoDevice1>();
            videoContext1   = _device.ImmediateContext.QueryInterface<VideoContext1>();

            vpcd = new VideoProcessorContentDescription()
            {
                InputFrameRate = new Rational(1, 1),
                OutputFrameRate = new Rational(1, 1),

                InputFrameFormat = VideoFrameFormat.Progressive,

                InputWidth = 1,
                InputHeight = 1,

                OutputWidth = 1,
                OutputHeight = 1,

                Usage = VideoUsage.PlaybackNormal
            };

            videoDevice1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);
            videoDevice1.CreateVideoProcessor(vpe, 0, out videoProcessor);

            vpivd = new VideoProcessorInputViewDescription()
            {
                FourCC = 0,
                Dimension = VpivDimension.Texture2D,
                Texture2D = new Texture2DVpiv() { MipSlice = 0, ArraySlice = 0 }
            };

            vpovd = new VideoProcessorOutputViewDescription() { Dimension = VpovDimension.Texture2D };

            videoDevice1.CreateVideoProcessorOutputView((Resource) _backBuffer, vpe, vpovd, out vpov);

            vpsa = new VideoProcessorStream[1];
        }

        // Screens
        private void ScreenPlay()
        {
            try
            {
                bool isIdle = IsIdle();

                if (player.iSHWAccelSuccess)
                {
                    if (display.Width == graphics.GraphicsDevice.DisplayMode.Width)
                        if ( display.Width / aspectRatio > display.Height)
                            screenPlayHW.SetBounds((int)(display.Width - (display.Height * aspectRatio)) / 2, 0, (int)(display.Height * aspectRatio), display.Height);
                        else
                            screenPlayHW.SetBounds(0, (int)(display.Height - (display.Width / aspectRatio)) / 2, display.Width, (int)(display.Width / aspectRatio));
                    else
                        screenPlayHW.SetBounds(0, 0, display.Width, display.Height);

                    _swapChain.Present(1, PresentFlags.None);

                    return;
                }

                lock(screenPlay)
                {
                    graphics.GraphicsDevice.Clear(Color.Black);
                    spriteBatch.Begin();

                    if (display.Width == graphics.GraphicsDevice.DisplayMode.Width)
                        if ( display.Width / aspectRatio > display.Height)
                            spriteBatch.Draw(screenPlay, new Rectangle((int)(display.Width - (display.Height * aspectRatio)) / 2, 0, (int)(display.Height * aspectRatio), display.Height), Color.White);
                        else
                            spriteBatch.Draw(screenPlay, new Rectangle(0, (int)(display.Height - (display.Width / aspectRatio)) / 2, display.Width, (int)(display.Width / aspectRatio)), Color.White);
                    else
                        spriteBatch.Draw(screenPlay, new Rectangle(0, 0, display.Width, display.Height), Color.White);

                    spriteBatch.End();
                    graphics.GraphicsDevice.Present();
                }
            }
            catch (Exception e)
            {
                Log(e.Message + " - " + e.StackTrace);

                if (e.Message.Equals("Text contains characters that cannot be resolved by this SpriteFont.\r\nParameter name: text")) spriteBatch = new SpriteBatch(graphics.GraphicsDevice);
            }
        }
        private void ScreenStop()
        {
            try
            {
                graphics.GraphicsDevice.Clear(Color.Black);

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
            bool isIdle = IsIdle();

            if (isIdle) { GoIdle(); if (!player.hasSubs) return; }

            // Update Subtitles Show / Hide
            if (player.hasSubs && SubsNew)
            {
                if (control.rtbSubs.InvokeRequired) { control.rtbSubs.BeginInvoke(new Action(() =>UpdateLoop())); return; }

                if (player.CurTime >= subStart && player.CurTime - subStart < subDur * 10000)
                {
                    if (SubsNew) { SubsNew = false; Subtitles.ASSToRichText(control.rtbSubs, subText); }

                    if (isIdle)
                        control.rtbSubs.Location = new Point((control.Width / 2) - (control.rtbSubs.Width / 2), control.Height - (control.rtbSubs.Height + 50));
                    else
                        control.rtbSubs.Location = new Point((control.Width / 2) - (control.rtbSubs.Width / 2), control.Height - (control.rtbSubs.Height + 80));
                }
            }
            else if ( player.hasSubs && !SubsNew)
            {
                if ( !(player.CurTime >= subStart && player.CurTime - subStart < subDur * 10000) )
                {
                    if (control.rtbSubs.InvokeRequired) { control.rtbSubs.BeginInvoke(new Action(() =>UpdateLoop())); return; }
                    control.rtbSubs.Text = "";
                }
                    
            }

            if (isIdle) return;
   
            if (seekClockTime == -1)
            {
                // Update SeekBar Value = Cur Time
                control.seekBar.Value = (int)(player.CurTime / 10000000);

                // Update Info Text [Adjustments]
                if (DateTime.UtcNow.Ticks - lastInfoTextUpdate < INFO_TIME_MS * 10000)
                    FixWidthInfoText(updatedInfoText);
                // Update Into Text [Current Time / Duration]
                else
                    FixWidthInfoText("[" + (new TimeSpan(player.CurTime)).ToString(@"hh\:mm\:ss") + " / " + (new TimeSpan(player.Duration)).ToString(@"hh\:mm\:ss") + "]");
            }
        }
        private void screenPlayHW_Resize(object sender, EventArgs e)
        {
            try
            {
                if (_swapChain != null && player.iSHWAccelSuccess)
                {
                    lock (screenPlayHW)
                    {   
                        Utilities.Dispose(ref vpov);
                        Utilities.Dispose(ref _backBuffer);

                        _swapChain.ResizeBuffers(0, 0, 0, Format.Unknown, SwapChainFlags.None);

                        _backBuffer = STexture2D.FromSwapChain<STexture2D>(_swapChain, 0);
                        videoDevice1.CreateVideoProcessorOutputView((Resource) _backBuffer, vpe, vpovd, out vpov);
                    }
                }
            }
            catch (Exception e1)
            {
                Log(e1.Message + " - " + e1.StackTrace);
            }
        }

        // Processes
        private void Open(string url)
        {
            int ret;
            bool isSubs = false;
            subText = "";
            control.rtbSubs.Text = "";

            string ext = url.Substring(url.LastIndexOf(".") + 1);
            if ((new List<string> { "srt", "txt" }).Contains(ext)) isSubs = true;

            if (!isSubs)
            {
                player.Close();
                if (player.hasAudio) audioPlayer.ResetClbk();
                control.lstMediaFiles.Visible = false;
                firstFrameData = null;

                player.Open(url);
                if (player.isTorrent) { UpdateInfoText($"Opening Torrent ..."); return; }
                UpdateInfoText($"Opening {Path.GetFileName(url)}");

                if (player.isFailed) { UpdateInfoText($"Opening {Path.GetFileName(url)} Failed"); Log("Error opening"); return; }
                if (player.hasAudio) { audioPlayer._RATE = player._RATE; audioPlayer.Initialize(); }

                aspectRatio = (float)player.Width / (float)player.Height;
                FixAspectRatio();

                screenPlayHW.Visible = player.iSHWAccelSuccess;

                control.lstMediaFiles.Items.Clear();
                control.lstMediaFiles.Items.Add(url);

                UpdateInfoText($"Opening {Path.GetFileName(url)} Success");
                Play();
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
        private void Play()
        {
            if (player.isTorrent) UpdateInfoText($"Buffering ...");

            screenPlay = new Texture2D(spriteBatch.GraphicsDevice, player.Width, player.Height, false, SurfaceFormat.Color);

            ScreenPlay(); ScreenPlay(); ScreenPlay(); ScreenPlay();

            player.Play();
            graphics.GraphicsDevice.Clear(Color.Black);

            control.seekBar.Maximum     = (int)(player.Duration / 10000000);
            control.seekBar.Value       = 0;

            if (player.hasAudio)
            {
                audioPlayer.Play();
                player.AudioExternalDelay = -1 * (AudioPlayer.NAUDIO_DELAY_MS + 70) * 10000; // for some reason even if we set DesiredLatency = 200 it is not exactly what we expect (+70)
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
            try
            {
                using (Graphics g = control.lblInfoText.CreateGraphics())
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    control.lblInfoText.Width = (int) g.MeasureString(infoText, control.lblInfoText.Font).Width - 10;
                }

                control.lblInfoText.Text = infoText;
            } catch (Exception) { }
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
            if (!control.seekBar.Visible) return;
            if (control.InvokeRequired) { control.BeginInvoke(new Action(() =>GoIdle())); return; }

            display2.IsMouseVisible     = false;
            control.lblInfoText.Visible = false;
            control.seekBar.Visible     = false;
            control.volBar.Visible      = false;
            btnBar.Visible              = false;

            ScreenPlay();
            if (!resizing) display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 1.0f);
        }
        private void UnIdle()
        {
            if (control.seekBar.Visible) return;
            if (control.InvokeRequired) { control.BeginInvoke(new Action(() =>UnIdle())); return; }

            display2.IsMouseVisible     = true;
            control.lblInfoText.Visible = true;
            control.seekBar.Visible     = true;
            control.volBar.Visible      = true;
            btnBar.Visible              = true;

            lastUserActionTicks         = DateTime.UtcNow.Ticks;
            display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 10.0f);

            ScreenPlay();
            display.Focus();
        }

        // Processes Streaming
        public void OpenTorrentSuccess(bool success)
        {
            if (player.isFailed || !success)
                UpdateInfoText($"Opening Torrent Failed");
            else
                UpdateInfoText($"Opening Torrent Success");
        }
        public void MediaFilesReceived(List<string> mediaFiles, List<long> mediaFilesSizes)
        {
            if ( control.lstMediaFiles.InvokeRequired  )
            {
                control.lstMediaFiles.BeginInvoke(new Action(() => MediaFilesReceived(mediaFiles, mediaFilesSizes)));
                return;
            }

            string selectedFile = "";

            control.lstMediaFiles.BeginUpdate();
            control.lstMediaFiles.Items.Clear();

            for (int i=0; i<mediaFiles.Count; i++)
            {
                string ext = Path.GetExtension(mediaFiles[i]);
                if ( ext == null || ext.Trim() == "") continue;

                if ( movieExts.Contains(ext.Substring(1,ext.Length-1)) )
                {
                    control.lstMediaFiles.Items.Add(mediaFiles[i]);
                    selectedFile = mediaFiles[i];
                }
            }
            control.lstMediaFiles.EndUpdate();

            if (control.lstMediaFiles.Items.Count == 1)
            {
                player.SetMediaFile(selectedFile);
                UpdateInfoText($"Opening {selectedFile} ...");
            }
            else
            {
                control.lstMediaFiles.Visible = true;
                FixLstMediaFiles();
            }
        }
        public void SetMediaFile(string selectedFile)
        {
            control.lstMediaFiles.Visible = false;
            player.SetMediaFile(selectedFile);
            UpdateInfoText($"Opening {selectedFile} ...");
        }
        public void OpenStreamSuccess(bool success, string selectedFile)
        {
            if (control.lstMediaFiles.InvokeRequired)
            {
                control.lstMediaFiles.BeginInvoke(new Action(() => OpenStreamSuccess(success, selectedFile)));
                return;
            }

            aspectRatio = (float)player.Width / (float)player.Height;
            FixAspectRatio();

            screenPlayHW.Visible = player.iSHWAccelSuccess;

            if (!success) { UpdateInfoText($"Opening {selectedFile} Failed"); Log("Error opening " + selectedFile); return; }

            UpdateInfoText($"Opening {selectedFile} Success");

            if (player.hasAudio) { audioPlayer._RATE = player._RATE; audioPlayer.Initialize(); }

            Play();
        }

        // UI Callbacks
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void VideoFrameClbk(byte[] frameData, long timestamp, IntPtr texture)
        {
            if (player.iSHWAccelSuccess)
            {
                try
                {
                    STexture2D curShared    = _device.OpenSharedResource<STexture2D>(texture);

                    videoDevice1.CreateVideoProcessorInputView(curShared, vpe, vpivd, out vpiv);
                    VideoProcessorStream vps = new VideoProcessorStream()
                    {
                        PInputSurface = (IntPtr) vpiv,
                        Enable = new RawBool(true)
                    };
                    vpsa[0] = vps;
                    videoContext1.VideoProcessorBlt(videoProcessor, vpov, 0, 1, vpsa);

                    _swapChain.Present(1, PresentFlags.None);

                    Utilities.Dispose(ref vpiv);
                    Utilities.Dispose(ref curShared);
                }
                catch (Exception e)
                {
                    Log(e.Message + " - " + e.StackTrace);
                }
            }
            else
            {
                try
                {
                    if (frameData       == null)    return;
                    if (firstFrameData  == null)    firstFrameData   = frameData;

                    if ( screenPlay.Width != player.Width) lock (screenPlay) screenPlay = new Texture2D(spriteBatch.GraphicsDevice, player.Width, player.Height, false, SurfaceFormat.Color);

                    lock (screenPlay)               screenPlay.SetData(frameData);
                    ScreenPlay();
                }
                catch (Exception e)
                {
                    Log(e.Message + " - " + e.StackTrace);

                    try
                    {
                        Log("Graphic Device Resets");
                        graphics = new GraphicsDeviceManager(display2);
                    } catch (Exception e1) {Log(e1.Message + " - " + e1.StackTrace); player.Pause(); }
                }
            }

            if (player.hasSubs) UpdateLoop();
        }
        private void SubsFrameClbk(string text, long start, int duration)
        {
            try
            {
                // Lazy ASS -> TXT
                subText     = text.LastIndexOf(",,") == -1 ? text : text.Substring(text.LastIndexOf(",,") + 2).Replace("\\N", "\n").Trim();
                subText     = Regex.Replace(subText, @"{\\an?[0-9]+}","");
                subStart    = start;
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

                    break;

                case Keys.Left:
                    if (!player.isReady) return;

                    player.Seek((int)(seekClockTime / 10000), false);
                    seekSum = 0; seekStep = 0; seekClockTime = -1;

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
                    if (player.isPlaying) { player.Pause(); audioPlayer.ResetClbk(); } else { player.Play(); }
                    break;

                case (char)Keys.S:
                    Stop();
                    break;

                case (char)Keys.H:
                    player.HWAccel = !player.HWAccel;
                    UpdateInfoText($"[HW ACCELERATION] {player.HWAccel}");
                    break;

                case (char)Keys.R:
                    aspectRatioKeep = !aspectRatioKeep;
                    if (aspectRatioKeep) FixAspectRatio();
                    break;

                case (char)Keys.Escape:
                    if (control.lstMediaFiles.Visible)
                        control.lstMediaFiles.Visible = false;
                    else if (!control.lstMediaFiles.Visible)
                        { control.lstMediaFiles.Visible = true; FixLstMediaFiles(); }
                    break;
            }
        }

        // UI Events [DISPLAY DRAG]
        private void Display_DragDrop(object sender, DragEventArgs e)
        {
            if ( e.Data.GetDataPresent(DataFormats.FileDrop) )
            {
                // VIDEO / AUDIO / SUBTITLE FILES
                display.Cursor = Cursors.Default;
                string[] filenames = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                if (filenames.Length > 0) Open(filenames[0]);
            }
            else if ( e.Data.GetDataPresent(DataFormats.Text) )
            {
                display.Cursor = Cursors.Default;
                string url = e.Data.GetData(DataFormats.Text, false).ToString();
                if (url.Length > 0) Open(url);
            }

            display.Focus();
            display.Activate();
            display.Show();
            display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 10.0f);
        }
        private void Display_DragEnter(object sender, DragEventArgs e)      { e.Effect = DragDropEffects.All; lastUserActionTicks = DateTime.UtcNow.Ticks; display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 80.0f); }

        // UI Events [DISPLAY MOUSE]
        private void Display_MouseDown(object sender, MouseEventArgs e)     { display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 50.0f); if (e.Button == MouseButtons.Left) { displayMLDown = true; displayMLDownPos = e.Location; } }
        private void Display_MouseUp(object sender, MouseEventArgs e)       { display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 10.0f); if (e.Button == MouseButtons.Left) { displayMLDown = false; resizing = false; displayMoveSideCur = 0; FixFrmControl(); UnIdle(); } }
        private void Display_MouseClickDbl(object sender, MouseEventArgs e) { if (e.Clicks >= 2 && e.Button == MouseButtons.Left) FullScreenToggle(); }
        private void Display_MouseClick(object sender, MouseEventArgs e) 
        { 
            if (e.Button == MouseButtons.Right) if (player.isPlaying) { player.Pause(); audioPlayer.ResetClbk(); } else { player.Play(); }

            else if ( e.Button == MouseButtons.Middle )
            { 
                if (control.lstMediaFiles.Visible)
                    control.lstMediaFiles.Visible = false;
                else 
                    { control.lstMediaFiles.Visible = true; FixLstMediaFiles(); }
            } 
        }
        private void Display_MouseMove(object sender, MouseEventArgs e)
        {
            if (displayMMoveLastPos == e.Location) return;

            displayMMoveLastPos = e.Location;

            if (!display2.IsMouseVisible && !displayMLDown) UnIdle();

            lastUserActionTicks = DateTime.UtcNow.Ticks;

            try
            {
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

                            resizing = true;
                            GoIdle();
                            ScreenPlay();
                        }
                    }
                    else
                    {
                        // MOVING FORM WHILE PRESSING LEFT BUTTON
                        display.Location = new Point(display.Location.X + e.Location.X - displayMLDownPos.X, display.Location.Y + e.Location.Y - displayMLDownPos.Y);
                        control.Location = display.Location;
                    }
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
            catch (Exception e1)
            {
                Log(e1.Message + " - " + e1.StackTrace);

                //if (e1.Message.Contains("DXGI_ERROR_DEVICE_REMOVED") ) InitializeID3D11();
            }
        }

        // UI Events [DISPLAY CLOSE]
        private void Display_Closing(object sender, FormClosingEventArgs e)
        {
            try
            {
                player.StopMediaStreamer();
                Thread.Sleep(20);
                player.Close();
                Thread.Sleep(20);
                display.Dispose();
            } catch (Exception) { }
        }

        // UI Events [CONTROL SEEKBAR]
        private void SeekBar_MouseUp(object sender, MouseEventArgs e)
        {
            display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 10.0f);

            if (!player.isReady) { display.Focus(); return; }

            UpdateSeekClockTime((long)(lastSeekBarMLDown) * 10000000);
            control.seekBar.Value   =  lastSeekBarMLDown;
            if ( player.hasAudio ) audioPlayer.ResetClbk(); // Clear Buffer For Silence During Seeking
            player.Seek(lastSeekBarMLDown * 1000, false);
            seekClockTime           =  -1;
            if (e.Button == MouseButtons.Left) seekBarMLDown = false;
            display.Focus();
        }
        private void SeekBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                display2.TargetElapsedTime  = TimeSpan.FromSeconds(1.0f / 50.0f);

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
        private void RtbSubsContentsResized(object sender, ContentsResizedEventArgs e)  { control.rtbSubs.ClientSize = new Size(display.Width, e.NewRectangle.Height); }
        private void RtbSubsEnter(object sender, EventArgs e) { display.Cursor = Cursors.Default; display.Focus(); }

        // UI Events [CONTROL LSTMEDIAFILES]
        private void lstMediaFiles_MouseClickDbl(object sender, MouseEventArgs e)
        {
            if ( !player.isTorrent ) { control.lstMediaFiles.Visible = false; display.Focus(); return; }
            if ( control.lstMediaFiles.SelectedItem == null ) return;

            SetMediaFile(control.lstMediaFiles.SelectedItem.ToString());
            display.Focus();
        }
        private void lstMediaFiles_KeyPress(object sender, KeyPressEventArgs e)
        {
            lastUserActionTicks = DateTime.UtcNow.Ticks;

            if ( e.KeyChar != (char)13 ) { Display_KeyPress(sender, e); return; }
            if ( !player.isTorrent ) { control.lstMediaFiles.Visible = false; display.Focus(); return; }
            if ( control.lstMediaFiles.SelectedItem == null ) return;

            SetMediaFile(control.lstMediaFiles.SelectedItem.ToString());
            display.Focus();
        }
        private void lstMediaFiles_MouseMove(object sender, MouseEventArgs e) { lastUserActionTicks = DateTime.UtcNow.Ticks; }

        private void GraphicsApplyChanges()
        {
            // SharpDX bug when using HW Acceleration
            try
            {
                graphics.ApplyChanges();
            }
            catch (Exception e)
            {
                Log(e.Message + " - " + e.StackTrace);

                try
                {
                    Log("Graphic Device Resets");
                    graphics = new GraphicsDeviceManager(display2);
                } catch (Exception e1) {Log(e1.Message + " - " + e1.StackTrace); player.Pause(); }
            }
        }
        private void FullScreenToggle() { if (graphics.PreferredBackBufferWidth == graphics.GraphicsDevice.DisplayMode.Width) NormalScreen(); else FullScreen(); }
        private void FullScreen()
        {
            System.Drawing.Rectangle curScreenBounds = Screen.FromControl(display).Bounds;

            if (graphics.PreferredBackBufferWidth != graphics.GraphicsDevice.DisplayMode.Width)
            {
                displayLastPos                  = display.Location;
                displayLastSize.Width           = graphics.PreferredBackBufferWidth;
                displayLastSize.Height          = graphics.PreferredBackBufferHeight;
            }

            graphics.PreferredBackBufferWidth   = curScreenBounds.Width;
            graphics.PreferredBackBufferHeight  = curScreenBounds.Height;
            display.Location = new Point(curScreenBounds.X, curScreenBounds.Y);

            GraphicsApplyChanges();
            FixFrmControl();

            if (player.isStopped) ScreenStop(); else ScreenPlay();
        }
        private void NormalScreen()
        {
            // TODO: ensure lastSize within current Screens bounds?
            graphics.PreferredBackBufferWidth   = displayLastSize.Width;
            graphics.PreferredBackBufferHeight  = displayLastSize.Height;
            display.Location                    = displayLastPos;
            
            GraphicsApplyChanges();
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
                GraphicsApplyChanges();
            }

            screenPlayHW.Size = new Size(display.Width, display.Height);
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

            FixLstMediaFiles();
        }
        private void FixLstMediaFiles()
        {
            if ( !control.lstMediaFiles.Visible ) return;

            control.lstMediaFiles.Width     = Math.Min(850, display.Width  - 200);
            control.lstMediaFiles.Height    = Math.Min(550, display.Height - 200);
            control.lstMediaFiles.Location  = new Point((control.Width- control.lstMediaFiles.Width) / 2, (control.Height- control.lstMediaFiles.Height) / 2);

            control.lstMediaFiles.Focus();
        }

        // Logging
        private void Log(string msg) { Console.WriteLine(msg); }
    }
}