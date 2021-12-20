using System;
using System.IO;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaPlayer
{
    unsafe partial class Player
    {
        public bool IsOpenFileDialogOpen    { get; private set; }

        public void SeekForward()
        {
            if (!CanPlay) return;

            long seekTs = CurTime + Config.Player.SeekOffset;

            if (seekTs <= Duration || isLive)
                Seek((int)(seekTs / 10000), true);
        }
        public void SeekBackward()
        {
            if (!CanPlay) return;

            Seek(Math.Max((int) ((CurTime - Config.Player.SeekOffset) / 10000), 0), false);
            
        }
        public void SeekToChapter(Demuxer.Chapter chapter)
        {
            /* TODO
             * Accurate pts required (backward/forward check)
             * Get current chapter implementation + next/prev
             */
            Seek((int) (chapter.StartTime / 10000.0), true);
        }

        public void CopyToClipboard()
        {
            if (decoder == null | decoder.UserInputUrl == null) return;

            System.Windows.Clipboard.SetText(decoder.UserInputUrl);
        }
        public void OpenFromClipboard()
        {
            OpenAsync(System.Windows.Clipboard.GetText());
        }
        public void OpenFromFileDialog()
        {
            IsOpenFileDialogOpen = true;
            bool allowIdleMode = false;

            if (Config.Player.ActivityMode)
            {
                allowIdleMode = true;
                Config.Player.ActivityMode = false;
            }

            System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            var res = openFileDialog.ShowDialog();

            if(res == System.Windows.Forms.DialogResult.OK)
                OpenAsync(openFileDialog.FileName);

            if (allowIdleMode)
                Config.Player.ActivityMode = true;

            IsOpenFileDialogOpen = false;
        }

        public void ShowFrame(int frameIndex)
        {
            if (!Video.IsOpened || !CanPlay || VideoDemuxer.HLSPlaylist != null) return;

            lock (lockPlayPause)
            {
                Pause();

                vFrame = VideoDecoder.GetFrame(frameIndex);
                if (vFrame == null) return;

                long tmpTimestamp = vFrame.timestamp - Config.Audio.Latency;
                Log($"SFI: {VideoDecoder.GetFrameNumber(tmpTimestamp)}");
                renderer.Present(vFrame);
                reversePlaybackResync = true;
                vFrame = null;
               
                curTime = tmpTimestamp;
                UI(() => UpdateCurTime());
            }
        }
        public void ShowFrameNext()
        {
            if (!Video.IsOpened || !CanPlay || VideoDemuxer.HLSPlaylist != null) return;

            lock (lockPlayPause)
            {
                Pause();
                ReversePlayback = false;

                if (VideoDecoder.Frames.Count == 0)
                    vFrame = VideoDecoder.GetFrameNext();
                else
                    VideoDecoder.Frames.TryDequeue(out vFrame);

                if (vFrame == null) return;

                long tmpTimestamp = vFrame.timestamp - Config.Audio.Latency;
                Log($"SFN: {VideoDecoder.GetFrameNumber(tmpTimestamp)}");
                renderer.Present(vFrame);
                reversePlaybackResync = true;
                vFrame = null;
                
                curTime = tmpTimestamp;
                UI(() => UpdateCurTime());
            }
        }
        public void ShowFramePrev()
        {
            if (!Video.IsOpened || !CanPlay || VideoDemuxer.HLSPlaylist != null) return;

            lock (lockPlayPause)
            {
                Pause();
                ReversePlayback = true;
                if (VideoDecoder.Frames.Count == 0)
                    vFrame = VideoDecoder.GetFrame(VideoDecoder.GetFrameNumber(CurTime) - 1);
                else
                    VideoDecoder.Frames.TryDequeue(out vFrame);

                if (vFrame == null) return;

                long tmpTimestamp = vFrame.timestamp - Config.Audio.Latency;
                Log($"SFB: {VideoDecoder.GetFrameNumber(tmpTimestamp)}");
                renderer.Present(vFrame);
                reversePlaybackResync = true;
                vFrame = null;

                curTime = tmpTimestamp;
                UI(() => UpdateCurTime()); // For some strange reason this will not be updated on KeyDown (only on KeyUp) which doesn't happen on ShowFrameNext (GPU overload? / Thread.Sleep underlying in UI thread?)
            }
        }

        public void SpeedUp()
        {
            if (Speed + 0.25 <= 1)
                Speed += 0.25;
            else
            {
                if (ReversePlayback)
                    return;

                Speed = Speed + 1 > 4 ? 4 : Speed + 1;
            }
        }
        public void SpeedDown()
        {
            if (Speed <= 1)
                Speed = Speed - 0.25 < 0.25 ? 0.25 : Speed - 0.25;
            else
                Speed -= 1;
        }
        
        public void FullScreen()
        {
            if (IsFullScreen) return;

            if (    (VideoView  != null && VideoView.FullScreen()) 
                ||  (Control    != null && Control.FullScreen()))
                IsFullScreen = true;
        }
        public void NormalScreen()
        {
            if (!IsFullScreen) return;

            if (    (VideoView  != null && VideoView.NormalScreen()) 
                ||  (Control    != null && Control.NormalScreen()))
                IsFullScreen = false;
        }
        public void ToggleFullScreen()
        {
            if (IsFullScreen)
                NormalScreen();
            else
                FullScreen();
        }

        /// <summary>
        /// Starts recording (uses current path and default filename)
        /// </summary>
        public void StartRecording()
        {
            string filename = $"FlyleafRecord.{(!VideoDecoder.Disposed && VideoDecoder.Stream != null ? VideoDecoder.Stream.Demuxer.Extension : AudioDecoder.Stream.Demuxer.Extension)}";
            filename = Utils.FindNextAvailableFile(Path.Combine(Environment.CurrentDirectory, filename));
            StartRecording(ref filename, false);
        }

        /// <summary>
        /// Starts recording
        /// </summary>
        /// <param name="filename">Path of the new recording file</param>
        /// <param name="useRecommendedExtension">You can force the output container's format or use the recommended one to avoid incompatibility</param>
        public void StartRecording(ref string filename, bool useRecommendedExtension = true)
        {
            decoder.StartRecording(ref filename, useRecommendedExtension);
            IsRecording = decoder.IsRecording;
        }

        /// <summary>
        /// Stops recording
        /// </summary>
        public void StopRecording()
        {
            decoder.StopRecording();
            IsRecording = decoder.IsRecording;
        }
        public void ToggleRecording()
        {
            if (!CanPlay) return;

            if (IsRecording)
                StopRecording();
            else
                StartRecording();
        }

        /// <summary>
        /// Saves the current video frame to bitmap file (uses current path and default filename)
        /// </summary>
        public void TakeSnapshot()
        {
            TakeSnapshot(null);
        }

        /// <summary>
        /// Saves the current video frame to bitmap file
        /// </summary>
        /// <param name="filename"></param>
        public void TakeSnapshot(string filename)
        {
            if (!CanPlay) return;

            renderer?.TakeSnapshot(filename == null ? Utils.FindNextAvailableFile(Path.Combine(Environment.CurrentDirectory, $"FlyleafSnapshot.bmp")) : filename);
        }

        public void ZoomIn()
        {
            Zoom += Config.Player.ZoomOffset;
        }
        public void ZoomOut()
        {
            Zoom -= Config.Player.ZoomOffset;

            if (renderer.GetViewport.Width < 0 || renderer.GetViewport.Height < 0)
                Zoom += Config.Player.ZoomOffset;
        }

        public void ResetAll()
        {
            Speed = 1;
            PanXOffset = 0;
            PanYOffset = 0;
            Zoom = 0;
        }
    }
}
