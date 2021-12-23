using System;
using System.Threading;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;

using static FlyleafLib.Utils;

namespace FlyleafLib.MediaPlayer
{
    unsafe partial class Player
    {
        /// <summary>
        /// Fires on buffering started
        /// Warning: Uses Invoke and it comes from playback thread so you can't pause/stop etc. You need to use another thread if you have to.
        /// </summary>
        public event EventHandler BufferingStarted;
        protected virtual void OnBufferingStarted()
        {
            if (onBufferingStarted != onBufferingCompleted) return;
            BufferingStarted?.Invoke(this, new EventArgs()); 
            onBufferingStarted++;

            #if DEBUG
            Log($"OnBufferingStarted");
            #endif
        }

        /// <summary>
        /// Fires on buffering completed (will fire also on failed buffering completed)
        /// (BufferDration > Config.Player.MinBufferDuration)
        /// Warning: Uses Invoke and it comes from playback thread so you can't pause/stop etc. You need to use another thread if you have to.
        /// </summary>
        public event EventHandler<BufferingCompletedArgs> BufferingCompleted;
        protected virtual void OnBufferingCompleted(string error = null)
        {
            if (onBufferingStarted - 1 != onBufferingCompleted) return;
            BufferingCompleted?.Invoke(this, new BufferingCompletedArgs(error));
            onBufferingCompleted++;

            #if DEBUG
            Log($"OnBufferingCompleted {(error != null ? $"(Error: {error})" : "")}");
            #endif
        }

        long onBufferingStarted;
        long onBufferingCompleted;

        private void ShowOneFrame()
        {
            sFrame = null;
            Subtitles.subsText = "";
            if (Subtitles._SubsText != "")
                UIAdd(() => Subtitles.SubsText = Subtitles.SubsText);

            if (VideoDecoder.Frames.Count > 0)
            {
                VideoDecoder.Frames.TryDequeue(out vFrame);
                renderer.Present(vFrame);

                if (seeks.Count == 0)
                {
                    if (VideoDemuxer.HLSPlaylist == null)
                        curTime = vFrame.timestamp - Config.Audio.Latency;
                    UIAdd(() => UpdateCurTime());
                    UI();
                }

                // Required for buffering on paused
                if (decoder.RequiresResync && !IsPlaying && seeks.Count == 0)
                    decoder.Resync(vFrame.timestamp);

                vFrame = null;
            }

            UI();
        }

        private bool MediaBuffer()
        {
            Log("[SCREAMER] Buffering ...");
            while (isVideoSwitch && IsPlaying) Thread.Sleep(10);

            Audio.ClearBuffer();

            VideoDemuxer.Start();
            VideoDecoder.Start();
             
            if (Config.Audio.Enabled)
            {
                if (AudioDecoder.OnVideoDemuxer)
                    AudioDecoder.Start();
                else if (!decoder.RequiresResync)
                {
                    AudioDemuxer.Start();
                    AudioDecoder.Start();
                }
            }

            if (Config.Subtitles.Enabled)
            {
                lock (lockSubtitles)
                if (SubtitlesDecoder.OnVideoDemuxer)
                    SubtitlesDecoder.Start();
                else if (!decoder.RequiresResync)
                {
                    SubtitlesDemuxer.Start();
                    SubtitlesDecoder.Start();
                }
            }

            VideoDecoder.DisposeFrame(vFrame);
            vFrame = null;
            aFrame = null;
            sFrame = null;
            sFramePrev = null;

            //Subtitles.subsText = "";
            //if (Subtitles._SubsText != "")
            //    UI(() => Subtitles.SubsText = Subtitles.SubsText);

            bool gotAudio       = !Audio.IsOpened;
            bool gotVideo       = false;
            bool shouldStop     = false;
            bool showOneFrame   = true;
            int  audioRetries   = 3;

            do
            {
                if (showOneFrame && VideoDecoder.Frames.Count != 0)
                {
                    ShowOneFrame();
                    if (seeks.Count != 0) return false; 

                    showOneFrame = false; 
                }

                if (vFrame == null && VideoDecoder.Frames.Count != 0)
                {
                    VideoDecoder.Frames.TryDequeue(out vFrame);
                    if (!showOneFrame) gotVideo = true;
                }

                if (!gotAudio && aFrame == null && AudioDecoder.Frames.Count != 0)
                    AudioDecoder.Frames.TryDequeue(out aFrame);

                if (vFrame != null)
                {
                    if (decoder.RequiresResync)
                        decoder.Resync(vFrame.timestamp);

                    if (!gotAudio && aFrame != null)
                    {
                        for (int i=0; i<Math.Min(20, AudioDecoder.Frames.Count); i++)
                        {
                            if (aFrame == null || vFrame.timestamp - aFrame.timestamp < Config.Audio.Latency) { gotAudio = true; break; }

                            Log("Drop AFrame  " + TicksToTime(aFrame.timestamp));
                            AudioDecoder.Frames.TryDequeue(out aFrame);
                        }
                    }
                }

                if (!IsPlaying || HasEnded)
                    shouldStop = true;
                else
                {
                    if (!VideoDecoder.IsRunning && !isVideoSwitch) { Log("[SCREAMER] Video Exhausted"); shouldStop= true; }
                    if (vFrame != null && !gotAudio && audioRetries > 0 && (!AudioDecoder.IsRunning || AudioDecoder.Demuxer.Status == MediaFramework.Status.QueueFull)) { 
                        Log($"[SCREAMER] Audio Exhausted {audioRetries}"); audioRetries--; if (audioRetries < 1) 
                            gotAudio  = true; 
                    }
                }

                Thread.Sleep(10);

            } while (!shouldStop && (!gotVideo || !gotAudio));

            if (shouldStop && !(HasEnded && IsPlaying && vFrame != null)) { Log("[SCREAMER] Stopped"); return false; }
            if (vFrame == null) { Log("[SCREAMER] [ERROR] No Frames!"); return false; }

            while(VideoDemuxer.BufferedDuration < Config.Player.MinBufferDuration && IsPlaying && VideoDemuxer.IsRunning && VideoDemuxer.Status != MediaFramework.Status.QueueFull) Thread.Sleep(20);

            Log("[SCREAMER] Buffering Done");

            if (aFrame != null && aFrame.timestamp < vFrame.timestamp) 
                videoStartTicks = Math.Max(aFrame.timestamp, vFrame.timestamp - Config.Audio.Latency);
            else
                videoStartTicks = vFrame.timestamp - Config.Audio.Latency;

            startedAtTicks  = DateTime.UtcNow.Ticks;

            videoStartTicks = (long) (videoStartTicks / Speed);
            vFrame.timestamp = (long) (vFrame.timestamp / Speed);
            if (aFrame != null) aFrame.timestamp = (long) (aFrame.timestamp / Speed);
            if (sFrame != null) sFrame.timestamp = (long) (sFrame.timestamp / Speed);

            decoder.OpenedPlugin.OnBufferingCompleted();
            Log($"[SCREAMER] Started -> {TicksToTime(videoStartTicks)} | [V: {TicksToTime(vFrame.timestamp)}]" + (aFrame == null ? "" : $" [A: {TicksToTime(aFrame.timestamp)}]"));

            return true;
        }    
        private void Screamer()
        {
            int     vDistanceMs;
            int     aDistanceMs;
            int     sDistanceMs;
            int     sleepMs;
            long    elapsedSec = startedAtTicks;

            requiresBuffering = true;

            while (Status == Status.Playing)
            {
                if (seeks.TryPop(out SeekData seekData))
                {
                    seeks.Clear();
                    requiresBuffering = true;

                    decoder.PauseDecoders(); // TBR: Required to avoid gettings packets between Seek and ShowFrame which causes resync issues


                    if (decoder.Seek(seekData.ms, seekData.forward, !seekData.accurate) < 0)
                        Log("[SCREAMER] Seek failed");
                    else if (seekData.accurate)
                        decoder.GetVideoFrame(seekData.ms * (long)10000);
                }

                if (requiresBuffering)
                {
                    OnBufferingStarted();
                    MediaBuffer();
                    elapsedSec = startedAtTicks;
                    requiresBuffering = false;
                    if (seeks.Count != 0) continue;
                    if (vFrame == null) { Log("MediaBuffer() no video frame"); break; }
                    OnBufferingCompleted();
                }

                if (vFrame == null)
                {
                    if (VideoDecoder.Status == MediaFramework.Status.Ended)
                        break;

                    Log("[SCREAMER] No video frames");
                    requiresBuffering = true;
                    continue;
                }

                if (Status != Status.Playing) break;

                if (aFrame == null && !isAudioSwitch)
                {
                    AudioDecoder.Frames.TryDequeue(out aFrame);
                    if (aFrame != null) aFrame.timestamp = (long) (aFrame.timestamp / Speed);
                }
                if (sFrame == null && !isSubsSwitch )
                {
                    SubtitlesDecoder.Frames.TryPeek(out sFrame);
                    if (sFrame != null)
                        sFrame.timestamp = (long) (sFrame.timestamp / Speed); //sFrame.duration = sFrame.duration / Speed;
                }

                elapsedTicks    = videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks);
                vDistanceMs     = (int) ((vFrame.timestamp - elapsedTicks) / 10000);
                aDistanceMs     = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sDistanceMs     = sFrame != null ? (int) ((sFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sleepMs         = Math.Min(vDistanceMs, aDistanceMs) - 1;

                if (sleepMs < 0) sleepMs = 0;
                if (sleepMs > 2)
                {
                    if (sleepMs > 1000)
                    {   // Probably happens only on hls when it refreshes the m3u8 playlist / segments (and we are before the allowed cache)
                        Log($"[SCREAMER] Restarting ... (HLS?) | Distance: {TicksToTime(sleepMs * (long)10000)}");
                        requiresBuffering = true;
                        continue; 
                    }

                    if (Master.UICurTimePerSecond &&  (
                        (MainDemuxer.HLSPlaylist == null && curTime / 10000000 != _CurTime / 10000000) ||
                        (MainDemuxer.HLSPlaylist != null && Math.Abs(elapsedTicks - elapsedSec) > 10000000)))
                    {
                        elapsedSec  = elapsedTicks;
                        UI(() => UpdateCurTime());
                    }

                    Thread.Sleep(sleepMs);
                }

                if (Math.Abs(vDistanceMs - sleepMs) <= 2)
                {
                    //Log($"[V] Presenting {TicksToTime(vFrame.timestamp - Config.Audio.Latency)}");

                    if (decoder.VideoDecoder.Renderer.Present(vFrame))
                        Video.framesDisplayed++;
                    else
                        Video.framesDropped++;

                    if (seeks.Count == 0)
                    {
                        if (MainDemuxer.HLSPlaylist == null)
                            curTime = (long) ((vFrame.timestamp - Config.Audio.Latency) * Speed);
                        else
                            curTime = VideoDemuxer.CurTime;

                        if (Config.Player.UICurTimePerFrame)
                            UI(() => UpdateCurTime());
                    }

                    VideoDecoder.Frames.TryDequeue(out vFrame);
                    if (Speed != 1 && vFrame != null)
                        vFrame.timestamp = (long) (vFrame.timestamp / Speed);
                }
                else if (vDistanceMs < -2)
                {
                    Video.framesDropped++;
                    VideoDecoder.DisposeFrame(vFrame);
                    VideoDecoder.Frames.TryDequeue(out vFrame);
                    if (Speed != 1 && vFrame != null)
                        vFrame.timestamp = (long) (vFrame.timestamp / Speed);

                    Log($"vDistanceMs 2 |-> {vDistanceMs}");

                    if (vDistanceMs < -10)
                        requiresBuffering = true;
                    continue;
                }

                if (aFrame != null) // Should use different thread for better accurancy (renderer might delay it on high fps) | also on high offset we will have silence between samples
                {
                    if (Math.Abs(aDistanceMs - sleepMs) <= 10)
                    {
                        //Log($"[A] Presenting {TicksToTime(aFrame.timestamp)}");
                        Audio.AddSamples(aFrame.audioData);
                        AudioDecoder.Frames.TryDequeue(out aFrame);
                        if (aFrame != null) aFrame.timestamp = (long) (aFrame.timestamp / Speed);
                    }
                    else if (aDistanceMs < -10) // Will be transfered back to decoder to drop invalid timestamps
                    {
                        if (aDistanceMs < -600)
                        {
                            Log($"aDistanceMs 2 |-> {aDistanceMs} | All audio frames disposed");
                            AudioDecoder.DisposeFrames();
                            aFrame = null;
                        }
                        else
                        {
                            int maxdrop = Math.Max(Math.Min((vDistanceMs - sleepMs) - 1, 20), 3);
                            Log($"-=-=-= {maxdrop} -=-=-=");
                            for (int i=0; i<maxdrop; i++)
                            {
                                Log($"aDistanceMs 2 |-> {aDistanceMs}");
                                AudioDecoder.Frames.TryDequeue(out aFrame);
                                if (aFrame != null) aFrame.timestamp = (long) (aFrame.timestamp / Speed);
                                aDistanceMs = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                                if (aDistanceMs > -7) break;
                            }
                        }
                    }
                }

                if (sFramePrev != null && elapsedTicks - sFramePrev.timestamp > (long)sFramePrev.duration * 10000)
                {
                    Subtitles.subsText = "";
                    UI(() => Subtitles.SubsText = Subtitles.SubsText);

                    sFramePrev = null;
                }

                if (sFrame != null)
                {
                    if (Math.Abs(sDistanceMs - sleepMs) < 30 || (sDistanceMs < -30 && sFrame.duration + sDistanceMs > 0))
                    {
                        Subtitles.subsText = sFrame.text;
                        UI(() => Subtitles.SubsText = Subtitles.SubsText);

                        sFramePrev = sFrame;
                        sFrame = null;
                        SubtitlesDecoder.Frames.TryDequeue(out SubtitlesFrame devnull);
                    }
                    else if (sDistanceMs < -30)
                    {
                        Log($"sDistanceMs 2 |-> {sDistanceMs}");

                        sFrame = null;
                        SubtitlesDecoder.Frames.TryDequeue(out SubtitlesFrame devnull);
                    }
                }
            }

            Log($"[SCREAMER] Finished -> {TicksToTime(CurTime)}");
        }

        private void ScreamerLowLatency()
        {
            long    secondTime = DateTime.UtcNow.Ticks;
            long    avgFrameDuration = VideoDecoder.VideoStream.FrameDuration > 0 ? VideoDecoder.VideoStream.FrameDuration : (int) (10000000.0 / 25.0);
            long    lastPresentTime = 0;

            VideoDecoder.DisposeFrame(vFrame);
            vFrame = null;

            //decoder.Seek(0);
            decoder.Flush();
            VideoDemuxer.Start();
            VideoDecoder.Start();

            if (Config.Player.LowLatencyMaxVideoFrames < 1) Config.Player.LowLatencyMaxVideoFrames = 1;

            while (Status == Status.Playing)
            {
                if (vFrame == null)
                {
                    //OnBufferingStarted();
                    while (VideoDecoder.Frames.Count == 0 && Status == Status.Playing) Thread.Sleep(20);
                    //OnBufferingCompleted();
                    if (Status != Status.Playing) break;

                    while (VideoDecoder.Frames.Count >= Config.Player.LowLatencyMaxVideoFrames && VideoDemuxer.VideoPackets.Count >= Config.Player.LowLatencyMaxVideoPackets)
                    {
                        Video.FramesDropped++;
                        VideoDecoder.DisposeFrame(vFrame);
                        VideoDecoder.Frames.TryDequeue(out vFrame);
                    }

                    if (vFrame == null) VideoDecoder.Frames.TryDequeue(out vFrame);
                }
                else
                {
                    long curTime = DateTime.UtcNow.Ticks;

                    if (Master.UICurTimePerSecond && curTime - secondTime > 10000000 - avgFrameDuration)
                    {
                        secondTime = curTime;
                        UI(() => UpdateCurTime());
                    }

                    int sleepMs = (int) ((avgFrameDuration - (curTime - lastPresentTime)) / 10000);
                    if (sleepMs < 11000 && sleepMs > 2) Thread.Sleep(sleepMs);
                    renderer.Present(vFrame);
                    if (MainDemuxer.HLSPlaylist == null && seeks.Count == 0)
                    {
                        this.curTime = (long) ((vFrame.timestamp - Config.Audio.Latency) * Speed);

                        if (Config.Player.UICurTimePerFrame)
                            UI(() => UpdateCurTime());
                    }
                    lastPresentTime = DateTime.UtcNow.Ticks;
                    vFrame = null;
                }
            }
        }

        private bool AudioBuffer()
        {
            while ((isVideoSwitch || isAudioSwitch) && IsPlaying) Thread.Sleep(10);
            if (!IsPlaying) return false;

            aFrame = null;
            Audio.ClearBuffer();
            decoder.AudioStream.Demuxer.Start();
            AudioDecoder.Start();

            while(AudioDecoder.Frames.Count == 0 && IsPlaying && AudioDecoder.IsRunning) Thread.Sleep(10);
            AudioDecoder.Frames.TryDequeue(out aFrame);
            if (aFrame == null) 
                return false;

            if (seeks.Count == 0)
            {
                if (MainDemuxer.HLSPlaylist == null)
                    curTime = aFrame.timestamp;
                UI(() => UpdateCurTime());
            }

            while(decoder.AudioStream.Demuxer.BufferedDuration < Config.Player.MinBufferDuration && IsPlaying && decoder.AudioStream.Demuxer.IsRunning && decoder.AudioStream.Demuxer.Status != MediaFramework.Status.QueueFull) Thread.Sleep(20);

            if (!IsPlaying || AudioDecoder.Frames.Count == 0)
                return false;

            startedAtTicks  = DateTime.UtcNow.Ticks;
            videoStartTicks = aFrame.timestamp;

            return true;
        }
        private void ScreamerAudioOnly()
        {
            int aDistanceMs;
            long elapsedSec = startedAtTicks;

            requiresBuffering = true;

            while (IsPlaying)
            {
                if (seeks.TryPop(out SeekData seekData))
                {
                    seeks.Clear();
                    requiresBuffering = true;

                    if (AudioDecoder.OnVideoDemuxer)
                    {
                        if (decoder.Seek(seekData.ms, seekData.forward) < 0)
                            Log("[SCREAMER] Seek failed 1");
                    }
                    else
                    {
                        if (decoder.SeekAudio(seekData.ms, seekData.forward) < 0)
                            Log("[SCREAMER] Seek failed 2");
                    }
                }

                if (requiresBuffering)
                {
                    OnBufferingStarted();
                    AudioBuffer();
                    elapsedSec = startedAtTicks;
                    requiresBuffering = false;
                    if (seeks.Count != 0) continue;
                    OnBufferingCompleted();
                    if (aFrame == null) { Log("MediaBuffer() no audio frame"); break; }
                }

                if (aFrame == null)
                {
                    if (AudioDecoder.Status == MediaFramework.Status.Ended)
                        break;

                    Log("[SCREAMER] No audio frames");
                    requiresBuffering = true;
                    continue;
                }

                if (Status != Status.Playing) break;

                elapsedTicks = videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks);
                aDistanceMs  = (int) ((aFrame.timestamp - elapsedTicks) / 10000);

                if (aDistanceMs > 1000 || aDistanceMs < -100)
                {
                    requiresBuffering = true;
                    continue;
                }

                if (aDistanceMs > 100)
                {
                    if (Master.UICurTimePerSecond &&  (
                        (MainDemuxer.HLSPlaylist == null && curTime / 10000000 != _CurTime / 10000000) ||
                        (MainDemuxer.HLSPlaylist != null && Math.Abs(elapsedTicks - elapsedSec) > 10000000)))
                    {
                        elapsedSec  = elapsedTicks;
                        UI(() => UpdateCurTime());
                    }

                    Thread.Sleep(aDistanceMs-15);
                }

                if (MainDemuxer.HLSPlaylist == null && seeks.Count == 0)
                {
                    curTime = aFrame.timestamp;

                    if (Config.Player.UICurTimePerFrame)
                        UI(() => UpdateCurTime());
                }

                Audio.AddSamples(aFrame.audioData, false);
                AudioDecoder.Frames.TryDequeue(out aFrame);
            }
        }

        private void ScreamerReverse()
        {
            long    elapsedSec = startedAtTicks;
            int     vDistanceMs;
            int     sleepMs;

            while (Status == Status.Playing)
            {
                if (seeks.TryPop(out SeekData seekData))
                {
                    seeks.Clear();
                    if (decoder.Seek(seekData.ms, seekData.forward) < 0)
                        Log("[SCREAMER] Seek failed");
                }

                if (vFrame == null)
                {
                    if (VideoDecoder.Status == MediaFramework.Status.Ended)
                        break;

                    OnBufferingStarted();
                    if (reversePlaybackResync)
                    {
                        decoder.Flush();
                        VideoDemuxer.EnableReversePlayback(CurTime);
                        reversePlaybackResync = false;
                    }
                    VideoDemuxer.Start();
                    VideoDecoder.Start();

                    while (VideoDecoder.Frames.Count == 0 && Status == Status.Playing && VideoDecoder.IsRunning) Thread.Sleep(15);
                    OnBufferingCompleted();
                    VideoDecoder.Frames.TryDequeue(out vFrame);
                    if (vFrame == null) { Log("[SCREAMER] No video frame"); break; }
                    vFrame.timestamp = (long) (vFrame.timestamp / Speed);
                    videoStartTicks = vFrame.timestamp;
                    startedAtTicks  = DateTime.UtcNow.Ticks;
                    elapsedTicks = videoStartTicks;
                    elapsedSec = startedAtTicks;

                    if (MainDemuxer.HLSPlaylist == null && seeks.Count == 0)
                        curTime = (long) ((vFrame.timestamp - Config.Audio.Latency) * Speed);
                    UI(() => UpdateCurTime());
                }

                elapsedTicks    = videoStartTicks - (DateTime.UtcNow.Ticks - startedAtTicks);
                vDistanceMs     = (int) ((elapsedTicks - vFrame.timestamp) / 10000);
                sleepMs         = vDistanceMs - 1;

                if (sleepMs < 0) sleepMs = 0;

                if (Math.Abs(vDistanceMs - sleepMs) > 5)
                {
                    Log($"vDistanceMs |-> {vDistanceMs}");
                    VideoDecoder.DisposeFrame(vFrame);
                    vFrame = null;
                    Thread.Sleep(5);
                    continue; // rebuffer
                }

                if (sleepMs > 2)
                {
                    if (sleepMs > 1000)
                    {
                        Log($"sleepMs -> {sleepMs} , vDistanceMs |-> {vDistanceMs}");
                        VideoDecoder.DisposeFrame(vFrame);
                        vFrame = null;
                        Thread.Sleep(5);
                        continue; // rebuffer
                    }

                    // Every seconds informs the application with CurTime / Bitrates (invokes UI thread to ensure the updates will actually happen)
                    if (Master.UICurTimePerSecond && (
                        (MainDemuxer.HLSPlaylist == null && curTime / 10000000 != _CurTime / 10000000) || 
                        (MainDemuxer.HLSPlaylist != null && Math.Abs(elapsedTicks - elapsedSec) > 10000000)))
                    {
                        elapsedSec  = elapsedTicks;
                        UI(() => UpdateCurTime());
                    }

                    Thread.Sleep(sleepMs);
                }

                decoder.VideoDecoder.Renderer.Present(vFrame);
                if (MainDemuxer.HLSPlaylist == null && seeks.Count == 0)
                {
                    curTime = (long) ((vFrame.timestamp - Config.Audio.Latency) * Speed);

                    if (Config.Player.UICurTimePerFrame)
                        UI(() => UpdateCurTime());
                }
                    
                VideoDecoder.Frames.TryDequeue(out vFrame);
                if (vFrame != null)
                    vFrame.timestamp = (long) (vFrame.timestamp / Speed);
            }
        }
    }

    public class BufferingCompletedArgs : EventArgs
    {
        public string   Error       { get; }
        public bool     Success     { get; }
            
        public BufferingCompletedArgs(string error)
        {
            Error   = error;
            Success = Error == null;
        }
    }
}
