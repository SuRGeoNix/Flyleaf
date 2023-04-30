using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;

using static FlyleafLib.Utils;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer;

unsafe partial class Player
{
    public bool IsOpenFileDialogOpen    { get; private set; }

    
    public void SeekBackward()  => SeekBackward_(Config.Player.SeekOffset);
    public void SeekBackward2() => SeekBackward_(Config.Player.SeekOffset2);
    public void SeekBackward3() => SeekBackward_(Config.Player.SeekOffset3);
    public void SeekBackward_(long offset)
    {
        if (!CanPlay) return;

        if (Config.Player.SeekAccurate)
            SeekAccurate(Math.Max((int) ((CurTime - offset) / 10000), 0));
        else
            Seek(Math.Max((int) ((CurTime - offset) / 10000), 0), false);
    }

    public void SeekForward()   => SeekForward_(Config.Player.SeekOffset);
    public void SeekForward2()  => SeekForward_(Config.Player.SeekOffset2);
    public void SeekForward3()  => SeekForward_(Config.Player.SeekOffset3);
    public void SeekForward_(long offset)
    {
        if (!CanPlay) return;

        long seekTs = CurTime + offset;

        if (seekTs <= Duration || isLive)
        {
            if (Config.Player.SeekAccurate)
                SeekAccurate((int)(seekTs / 10000));
            else
                Seek((int)(seekTs / 10000), true);
        }
    }

    public void SeekToChapter(Demuxer.Chapter chapter) =>
        /* TODO
* Accurate pts required (backward/forward check)
* Get current chapter implementation + next/prev
*/
        Seek((int)(chapter.StartTime / 10000.0), true);

    public void CopyToClipboard()
    {
        if (decoder.Playlist.Url == null) 
            System.Windows.Clipboard.SetText("");
        else
            System.Windows.Clipboard.SetText(decoder.Playlist.Url);
    }
    public void CopyItemToClipboard()
    {
        if (decoder.Playlist.Selected == null || decoder.Playlist.Selected.DirectUrl == null)
            System.Windows.Clipboard.SetText("");
        else
            System.Windows.Clipboard.SetText(decoder.Playlist.Selected.DirectUrl);
    }
    public void OpenFromClipboard()
        => OpenAsync(System.Windows.Clipboard.GetText());
    public void OpenFromFileDialog()
    {
        bool wasActivityEnabled = Activity.IsEnabled;
        Activity.IsEnabled = false;
        IsOpenFileDialogOpen = true;

        System.Windows.Forms.OpenFileDialog openFileDialog = new();
        var res = openFileDialog.ShowDialog();

        if(res == System.Windows.Forms.DialogResult.OK)
            OpenAsync(openFileDialog.FileName);

        Activity.IsEnabled = wasActivityEnabled;
        IsOpenFileDialogOpen = false;
    }

    public void ShowFrame(int frameIndex)
    {
        if (!Video.IsOpened || !CanPlay || VideoDemuxer.IsHLSLive) return;

        lock (lockActions)
        {
            Pause();
            sFrame = null;
            Subtitles.subsText = "";
            if (Subtitles._SubsText != "")
                UI(() => Subtitles.SubsText = Subtitles.SubsText);
            decoder.Flush();
            decoder.RequiresResync = true;

            vFrame = VideoDecoder.GetFrame(frameIndex);
            if (vFrame == null) return;

            if (CanDebug) Log.Debug($"SFI: {VideoDecoder.GetFrameNumber(vFrame.timestamp)}");

            curTime = vFrame.timestamp;
            renderer.Present(vFrame);
            reversePlaybackResync = true;                
            vFrame = null;

            UI(() => UpdateCurTime());
        }
    }
    public void ShowFrameNext()
    {
        if (!Video.IsOpened || !CanPlay || VideoDemuxer.IsHLSLive) return;

        lock (lockActions)
        {
            Pause();
            ReversePlayback = false;
            if (Subtitles._SubsText != "")
            {
                sFrame = null;
                Subtitles.subsText = "";
                Subtitles.SubsText = Subtitles.SubsText;
            }

            if (VideoDecoder.Frames.Count == 0)
                vFrame = VideoDecoder.GetFrameNext();
            else
                VideoDecoder.Frames.TryDequeue(out vFrame);

            if (vFrame == null) return;

            if (CanDebug) Log.Debug($"SFN: {VideoDecoder.GetFrameNumber(vFrame.timestamp)}");

            curTime = curTime = vFrame.timestamp;
            renderer.Present(vFrame);
            reversePlaybackResync = true;
            vFrame = null;

            UI(() => UpdateCurTime());
        }
    }
    public void ShowFramePrev()
    {
        if (!Video.IsOpened || !CanPlay || VideoDemuxer.IsHLSLive) return;

        lock (lockActions)
        {
            Pause();

            if (!ReversePlayback)
            {
                Set(ref _ReversePlayback, true, false);
                Speed = 1;
                if (Subtitles._SubsText != "")
                {
                    sFrame = null;
                    Subtitles.subsText = "";
                    Subtitles.SubsText = Subtitles.SubsText;
                }
                decoder.StopThreads();
                decoder.Flush();
            }

            if (VideoDecoder.Frames.Count == 0)
            {
                // Temp fix for previous timestamps until we seperate GetFrame for Extractor and the Player
                reversePlaybackResync = true;
                int askedFrame = VideoDecoder.GetFrameNumber(CurTime) - 1;
                //Log.Debug($"CurTime1: {TicksToTime(CurTime)}, Asked: {askedFrame}");
                vFrame = VideoDecoder.GetFrame(askedFrame);
                if (vFrame == null) return;

                int recvFrame = VideoDecoder.GetFrameNumber(vFrame.timestamp);
                //Log.Debug($"CurTime2: {TicksToTime(vFrame.timestamp)}, Got: {recvFrame}");
                if (askedFrame != recvFrame)
                {
                    VideoDecoder.DisposeFrame(vFrame);
                    vFrame = null;
                    vFrame = askedFrame > recvFrame
                        ? VideoDecoder.GetFrame(VideoDecoder.GetFrameNumber(CurTime))
                        : VideoDecoder.GetFrame(VideoDecoder.GetFrameNumber(CurTime) - 2);
                }
            }
            else
                VideoDecoder.Frames.TryDequeue(out vFrame);

            if (vFrame == null) return;

            if (CanDebug) Log.Debug($"SFB: {VideoDecoder.GetFrameNumber(vFrame.timestamp)}");

            curTime = vFrame.timestamp;
            renderer.Present(vFrame);
            vFrame = null;
            UI(() => UpdateCurTime()); // For some strange reason this will not be updated on KeyDown (only on KeyUp) which doesn't happen on ShowFrameNext (GPU overload? / Thread.Sleep underlying in UI thread?)
        }
    }

    public void SpeedUp()       => Speed += Config.Player.SpeedOffset;
    public void SpeedUp2()      => Speed += Config.Player.SpeedOffset2;
    public void SpeedDown()     => Speed -= Config.Player.SpeedOffset;
    public void SpeedDown2()    => Speed -= Config.Player.SpeedOffset2;

    public void RotateRight()   => Rotation = (_Rotation + 90) % 360;
    public void RotateLeft()    => Rotation = _Rotation < 90 ? 360 + _Rotation - 90 : _Rotation - 90;

    public void FullScreen()    => Host?.Player_SetFullScreen(true);
    public void NormalScreen()  => Host?.Player_SetFullScreen(false);
    public void ToggleFullScreen()
    {
        if (Host == null)
            return;

        if (Host.Player_GetFullScreen())
            Host.Player_SetFullScreen(false);
        else
            Host.Player_SetFullScreen(true);
    }

    /// <summary>
    /// Starts recording (uses Config.Player.FolderRecordings and default filename title_curTime)
    /// </summary>
    public void StartRecording()
    {
        if (!CanPlay)
            return;
        try
        {
            if (!Directory.Exists(Config.Player.FolderRecordings))
                Directory.CreateDirectory(Config.Player.FolderRecordings);

            string filename = GetValidFileName(string.IsNullOrEmpty(Playlist.Selected.Title) ? "Record" : Playlist.Selected.Title) + $"_{new TimeSpan(CurTime):hhmmss}." + decoder.Extension;
            filename = FindNextAvailableFile(Path.Combine(Config.Player.FolderRecordings, filename));
            StartRecording(ref filename, false);
        } catch { }
    }

    /// <summary>
    /// Starts recording
    /// </summary>
    /// <param name="filename">Path of the new recording file</param>
    /// <param name="useRecommendedExtension">You can force the output container's format or use the recommended one to avoid incompatibility</param>
    public void StartRecording(ref string filename, bool useRecommendedExtension = true)
    {
        if (!CanPlay)
            return;

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
    /// <para>Saves the current video frame (encoding based on file extention .bmp, .png, .jpg)</para>
    /// <para>If filename not specified will use Config.Player.FolderSnapshots and with default filename title_frameNumber.ext (ext from Config.Player.SnapshotFormat)</para>
    /// <para>If width/height not specified will use the original size. If one of them will be set, the other one will be set based on original ratio</para>
    /// <para>If frame not specified will use the current/last frame</para>
    /// </summary>
    /// <param name="filename">Specify the filename (null: will use Config.Player.FolderSnapshots and with default filename title_frameNumber.ext (ext from Config.Player.SnapshotFormat)</param>
    /// <param name="width">Specify the width (-1: will keep the ratio based on height)</param>
    /// <param name="height">Specify the height (-1: will keep the ratio based on width)</param>
    /// <param name="frame">Specify the frame (null: will use the current/last frame)</param>
    /// <exception cref="Exception"></exception>
    public void TakeSnapshotToFile(string filename = null, int width = -1, int height = -1, VideoFrame frame = null)
    {
        if (!CanPlay)
            return;

        if (filename == null)
        {
            try
            {
                if (!Directory.Exists(Config.Player.FolderSnapshots))
                    Directory.CreateDirectory(Config.Player.FolderSnapshots);

                // TBR: if frame is specified we don't know the frame's number
                filename = GetValidFileName(string.IsNullOrEmpty(Playlist.Selected.Title) ? "Snapshot" : Playlist.Selected.Title) + $"_{(frame == null ? VideoDecoder.GetFrameNumber(CurTime).ToString() : "X")}.{Config.Player.SnapshotFormat}";
                filename = FindNextAvailableFile(Path.Combine(Config.Player.FolderSnapshots, filename));
            } catch { return; }
        }

        string ext = GetUrlExtention(filename);

        ImageFormat imageFormat;

        switch (ext)
        {
            case "bmp":
                imageFormat = ImageFormat.Bmp;
                break;

            case "png":
                imageFormat = ImageFormat.Png;
                break;

            case "jpg":
            case "jpeg":
                imageFormat = ImageFormat.Jpeg;
                break;

            default:
                throw new Exception($"Invalid snapshot extention '{ext}' (valid .bmp, .png, .jpeg, .jpg");
        }

        if (renderer == null)
            return;

        var snapshotBitmap = renderer.GetBitmap(width, height, frame);
        if (snapshotBitmap == null)
            return;

        Exception e = null;
        try { snapshotBitmap.Save(filename, imageFormat); } catch (Exception e2) { e = e2; }
        snapshotBitmap.Dispose();

        if (e != null)
            throw e;
    }

    /// <summary>
    /// <para>Returns a bitmap of the current or specified video frame</para>
    /// <para>If width/height not specified will use the original size. If one of them will be set, the other one will be set based on original ratio</para>
    /// <para>If frame not specified will use the current/last frame</para>
    /// </summary>
    /// <param name="width">Specify the width (-1: will keep the ratio based on height)</param>
    /// <param name="height">Specify the height (-1: will keep the ratio based on width)</param>
    /// <param name="frame">Specify the frame (null: will use the current/last frame)</param>
    /// <returns></returns>
    public Bitmap TakeSnapshotToBitmap(int width = -1, int height = -1, VideoFrame frame = null) => renderer?.GetBitmap(width, height, frame);

    public void ZoomIn() => ZoomTimes++;
    public void ZoomOut() => ZoomTimes--;
    public void ZoomIn(System.Windows.Point p)
    {
        var prev = renderer.GetViewport;

        if (p.X < prev.X || p.X > prev.X + prev.Width || p.Y < prev.Y || p.Y > prev.Y + prev.Height)
        {
            ZoomIn();
            return;
        }

        SetZoom(_ZoomTimes + 1, false);
        renderer.SetPanX((int)(PanXOffset - renderer.GetViewport.X) + (int)(p.X - ((p.X - prev.X) * renderer.GetViewport.Width / prev.Width)), false);
        PanYOffset = (int)(PanYOffset - renderer.GetViewport.Y) + (int)(p.Y - ((p.Y - prev.Y) * renderer.GetViewport.Height / prev.Height));
    }
    public void ZoomOut(System.Windows.Point p)
    {
        var prev = renderer.GetViewport;

        if (p.X < prev.X || p.X > prev.X + prev.Width || p.Y < prev.Y || p.Y > prev.Y + prev.Height)
        {
            ZoomOut();
            return;
        }

        SetZoom(_ZoomTimes - 1, false);
        renderer.SetPanX((int)(PanXOffset - renderer.GetViewport.X) + (int)(p.X - ((p.X - prev.X) * renderer.GetViewport.Width / prev.Width)), false);
        PanYOffset = (int)(PanYOffset - renderer.GetViewport.Y) + (int)(p.Y - ((p.Y - prev.Y) * renderer.GetViewport.Height / prev.Height));
    }

    public void ResetAll()
    {
        Speed = 1;
        PanXOffset = 0;
        PanYOffset = 0;
        Rotation = 0;
        Zoom = 100;
        ReversePlayback = false;
    }
}
