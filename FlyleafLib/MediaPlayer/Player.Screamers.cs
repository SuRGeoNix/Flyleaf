using System;
using System.Diagnostics;
using System.Threading;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;

using static FlyleafLib.Utils;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer;

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

        if (CanDebug) Log.Debug($"OnBufferingStarted");
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

        if (error != null && LastError == null)
        {
            lastError = error;
            UI(() => LastError = LastError);
        }

        BufferingCompleted?.Invoke(this, new BufferingCompletedArgs(error));
        onBufferingCompleted++;
        if (CanDebug) Log.Debug($"OnBufferingCompleted{(error != null ? $" (Error: {error})" : "")}");
    }

    long onBufferingStarted;
    long onBufferingCompleted;

    long elapsedTicks;
    long videoStartTicks;
    Stopwatch sw = new();

    private void ShowOneFrame()
    {
        sFrame = null;
        Subtitles.subsText = "";
        if (Subtitles._SubsText != "")
            UIAdd(() => Subtitles.SubsText = Subtitles.SubsText);

        if (!VideoDecoder.Frames.IsEmpty)
        {
            VideoDecoder.Frames.TryDequeue(out vFrame);
            if (vFrame != null) // might come from video input switch interrupt
                renderer.Present(vFrame);

            if (seeks.IsEmpty)
            {
                if (!VideoDemuxer.IsHLSLive)
                    curTime = vFrame.timestamp;
                UIAdd(() => UpdateCurTime());
                UIAll();
            }

            // Required for buffering on paused
            if (decoder.RequiresResync && !IsPlaying && seeks.IsEmpty)
                decoder.Resync(vFrame.timestamp);

            vFrame = null;
        }

        UIAll();
    }

    private bool MediaBuffer()
    {
        if (CanTrace) Log.Trace("Buffering");

        while (isVideoSwitch && IsPlaying) Thread.Sleep(10);

        Audio.ClearBuffer();

        VideoDemuxer.Start();
        VideoDecoder.Start();
         
        long curAudioDeviceDelay = 0;

        if (Config.Audio.Enabled)
        {
            curAudioDeviceDelay = Audio.GetDeviceDelay();
            Audio.sourceVoice.FlushSourceBuffers();

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

        bool gotAudio       = !Audio.IsOpened || Config.Player.MaxLatency != 0;
        bool gotVideo       = false;
        bool shouldStop     = false;
        bool showOneFrame   = Config.Player.MaxLatency == 0;
        int  audioRetries   = 4;
        int  loops          = 0;

        do
        {
            loops++;

            if (showOneFrame && !VideoDecoder.Frames.IsEmpty)
            {
                ShowOneFrame();
                showOneFrame = false;
            }

            // We allo few ms to show a frame before cancelling
            if ((!showOneFrame || loops > 8) && !seeks.IsEmpty)
                return false;

            if (vFrame == null && !VideoDecoder.Frames.IsEmpty)
            {
                VideoDecoder.Frames.TryDequeue(out vFrame);
                if (!showOneFrame) gotVideo = true;
            }

            if (!gotAudio && aFrame == null && !AudioDecoder.Frames.IsEmpty)
                AudioDecoder.Frames.TryDequeue(out aFrame);

            if (vFrame != null)
            {
                if (decoder.RequiresResync)
                    decoder.Resync(vFrame.timestamp);

                if (!gotAudio && aFrame != null)
                {
                    for (int i=0; i<Math.Min(20, AudioDecoder.Frames.Count); i++)
                    {
                        if (aFrame == null || aFrame.timestamp - curAudioDeviceDelay + 20000 > vFrame.timestamp || vFrame.timestamp > Duration) { gotAudio = true; break; }

                        if (CanInfo) Log.Info($"Drop aFrame {TicksToTime(aFrame.timestamp)}");
                        AudioDecoder.Frames.TryDequeue(out aFrame);
                    }

                    // Avoid infinite loop in case of all audio timestamps wrong
                    if (!gotAudio)
                    {
                        audioRetries--;

                        if (audioRetries < 1)
                        {
                            gotAudio = true;
                            aFrame = null;
                            Log.Warn($"Audio Exhausted 1");
                        }
                    }
                }
            }

            if (!IsPlaying || decoderHasEnded)
                shouldStop = true;
            else
            {
                if (!VideoDecoder.IsRunning && !isVideoSwitch)
                {
                    Log.Warn("Video Exhausted");
                    shouldStop= true;
                }

                if (vFrame != null && !gotAudio && audioRetries > 0 && (!AudioDecoder.IsRunning || AudioDecoder.Demuxer.Status == MediaFramework.Status.QueueFull))
                {
                    if (CanWarn) Log.Warn($"Audio Exhausted 2 | {audioRetries}");

                    audioRetries--;

                    if (audioRetries < 1)
                        gotAudio  = true;
                }
            }

            Thread.Sleep(10);

        } while (!shouldStop && (!gotVideo || !gotAudio));

        if (shouldStop && !(decoderHasEnded && IsPlaying && vFrame != null))
        {
            Log.Info("Stopped");
            return false;
        }

        if (vFrame == null)
        {
            Log.Error("No Frames!");
            return false;
        }

        while(seeks.IsEmpty && VideoDemuxer.BufferedDuration < Config.Player.MinBufferDuration && IsPlaying && VideoDemuxer.IsRunning && VideoDemuxer.Status != MediaFramework.Status.QueueFull) Thread.Sleep(20);

        if (!seeks.IsEmpty)
            return false;

        if (CanInfo) Log.Info($"Started [V: {TicksToTime(vFrame.timestamp)}]" + (aFrame == null ? "" : $" [A: {TicksToTime(aFrame.timestamp)}]"));

        vFrame.timestamp= (long) (vFrame.timestamp / Speed);
        videoStartTicks = vFrame.timestamp;
        if (aFrame != null) aFrame.timestamp = (long) (aFrame.timestamp / Speed) - curAudioDeviceDelay;
        if (sFrame != null) sFrame.timestamp = (long) (sFrame.timestamp / Speed);

        decoder.OpenedPlugin.OnBufferingCompleted();

        return true;
    }
    private void Screamer()
    {
        int     ret;
        int     vDistanceMs;
        int     aDistanceMs;
        int     sDistanceMs;
        int     sleepMs;
        long    elapsedSec = 0;

        int     curAudioBuffers = 0;
        int     allowedLateAudioDrops = 7;
        long    curLatency;

        requiresBuffering = true;

        while (Status == Status.Playing)
        {
            if (seeks.TryPop(out SeekData seekData))
            {
                seeks.Clear();
                requiresBuffering = true;

                decoder.PauseDecoders(); // TBR: Required to avoid gettings packets between Seek and ShowFrame which causes resync issues

                ret = decoder.Seek(seekData.ms, seekData.forward, !seekData.accurate);
                if (ret < 0)
                {
                    if (CanWarn) Log.Warn("Seek failed");
                }
                else if (seekData.accurate)
                    decoder.GetVideoFrame(seekData.ms * (long)10000);
            }
            
            if (requiresBuffering)
            {
                OnBufferingStarted();
                MediaBuffer();
                requiresBuffering = false;
                if (!seeks.IsEmpty)
                    continue;

                if (vFrame == null)
                {
                    if (decoderHasEnded)
                        OnBufferingCompleted();

                    Log.Warn("[MediaBuffer] No video frame");
                    break;
                }
                OnBufferingCompleted();
                
                if (aFrame != null && Math.Abs(vFrame.timestamp - aFrame.timestamp) < 2)
                {
                    Audio.AddSamples(aFrame);
                    AudioDecoder.Frames.TryDequeue(out aFrame);
                }

                allowedLateAudioDrops = 7;
                elapsedSec = 0;
                sw.Restart();
            }

            if (vFrame == null)
            {
                if (VideoDecoder.Status == MediaFramework.Status.Ended)
                    break;

                Log.Warn("No video frames");
                requiresBuffering = true;
                continue;
            }

            if (Status != Status.Playing)
                break;
            
            if (aFrame == null && !isAudioSwitch)
            {
                AudioDecoder.Frames.TryDequeue(out aFrame);
                if (aFrame != null)
                    aFrame.timestamp = (long) (aFrame.timestamp / Speed) - Audio.GetDeviceDelay();
            }

            if (sFrame == null && !isSubsSwitch )
            {
                SubtitlesDecoder.Frames.TryPeek(out sFrame);
                if (sFrame != null)
                    sFrame.timestamp = (long) (sFrame.timestamp / Speed); //sFrame.duration = sFrame.duration / Speed;
            }

            elapsedTicks= videoStartTicks + sw.ElapsedTicks;
            vDistanceMs = (int) ((vFrame.timestamp - elapsedTicks) / 10000);

            if (aFrame != null)
            {
                aDistanceMs = (int) ((aFrame.timestamp - elapsedTicks) / 10000);
                curAudioBuffers = Audio.BuffersQueued;

                // On zero we resync
                if (curAudioBuffers != 0 && curAudioBuffers < 3 && aDistanceMs > 2)
                    aDistanceMs /= 2;
            }
            else
                aDistanceMs = int.MaxValue;

            sDistanceMs = sFrame != null ? (int) ((sFrame.timestamp - elapsedTicks) / 10000) : int.MaxValue;
            sleepMs     = Math.Min(vDistanceMs, aDistanceMs) - 1;
            
            if (sleepMs < 0) sleepMs = 0;
            if (sleepMs > 2)
            {
                if (vDistanceMs > 2000)
                {   // Probably happens only on hls when it refreshes the m3u8 playlist / segments (and we are before the allowed cache)
                    Log.Warn($"Restarting ... (HLS?) | Distance: {TicksToTime(sleepMs * (long)10000)}");
                    requiresBuffering = true;
                    continue;
                }

                if (Engine.Config.UICurTimePerSecond &&  (
                    (!MainDemuxer.IsHLSLive && curTime / 10000000 != _CurTime / 10000000) ||
                    (MainDemuxer.IsHLSLive && Math.Abs(elapsedTicks - elapsedSec) > 10000000)))
                {
                    elapsedSec  = elapsedTicks;
                    UI(() => UpdateCurTime());
                }

                Thread.Sleep(sleepMs);
            }

            if (Math.Abs(vDistanceMs - sleepMs) <= 2)
            {
                if (CanTrace) Log.Trace($"[V] Presenting {TicksToTime(vFrame.timestamp)}");

                if (decoder.VideoDecoder.Renderer.Present(vFrame))
                    Video.framesDisplayed++;
                else
                    Video.framesDropped++;

                if (seeks.IsEmpty)
                {
                    if (!MainDemuxer.IsHLSLive)
                        curTime = (long) (vFrame.timestamp * Speed);
                    else
                        curTime = VideoDemuxer.CurTime;

                    if (Config.Player.UICurTimePerFrame)
                        UI(() => UpdateCurTime());
                }

                VideoDecoder.Frames.TryDequeue(out vFrame);
                if (vFrame != null)
                {
                    if (Config.Player.MaxLatency != 0)
                    {
                        curLatency = VideoDemuxer.VideoPackets.Count > 0 ? VideoDemuxer.VideoPackets.LastTimestamp - vFrame.timestamp : 0;
                        double newSpeed = 1;

                        if (curLatency > Config.Player.MaxLatency)
                        {
                            if (curLatency > Config.Player.MaxLatency * 20)
                                { decoder.Flush(); newSpeed = 1; }
                            else if (curLatency > Config.Player.MaxLatency * 10)
                                newSpeed = 1.75;
                            else if (curLatency > Config.Player.MaxLatency * 2)
                                newSpeed = 1.5;
                            else
                                newSpeed = 1.1;
                        }

                        vFrame.timestamp = (long)(vFrame.timestamp / newSpeed);
                        if (newSpeed != _Speed)
                        {
                            _Speed = newSpeed;
                            UI(() => { Raise(nameof(Speed)); });
                            videoStartTicks = vFrame.timestamp;
                            sw.Restart();
                            elapsedSec = 0;
                        }
                    }
                    else if (Speed != 1)
                        vFrame.timestamp = (long)(vFrame.timestamp / Speed);
                }
            }
            else if (vDistanceMs < -2)
            {
                if (vDistanceMs < -10 || VideoDemuxer.BufferedDuration < Config.Player.MinBufferDuration)
                {
                    if (CanInfo) Log.Info($"vDistanceMs = {vDistanceMs}");

                    requiresBuffering = true;
                    continue;
                }

                if (CanDebug) Log.Debug($"vDistanceMs = {vDistanceMs}");

                Video.framesDropped++;
                VideoDecoder.DisposeFrame(vFrame);
                VideoDecoder.Frames.TryDequeue(out vFrame);

                if (Speed != 1 && vFrame != null)
                    vFrame.timestamp = (long)(vFrame.timestamp / Speed);
            }

            if (aFrame != null) // Should use different thread for better accurancy (renderer might delay it on high fps) | also on high offset we will have silence between samples
            {
                if (Math.Abs(aDistanceMs - sleepMs) <= 5)
                {
                    if (curAudioBuffers < 5)
                    {
                        if (CanTrace) Log.Trace($"[A] Presenting {TicksToTime(aFrame.timestamp)}");

                        Audio.AddSamples(aFrame);
                        Audio.framesDisplayed++;
                    }
                    else
                    {
                        if (CanTrace) Log.Trace($"[A] Buffer full, dropping {TicksToTime(aFrame.timestamp)}");
                        Audio.framesDropped++;
                    }

                    AudioDecoder.Frames.TryDequeue(out aFrame);
                    if (aFrame != null)
                        aFrame.timestamp = (long)(aFrame.timestamp / Speed) - Audio.GetDeviceDelay();
                }
                else if (aDistanceMs > 1000) // Drops few audio frames in case of wrong timestamps
                {
                    if (allowedLateAudioDrops > 0)
                    {
                        Audio.framesDropped++;
                        allowedLateAudioDrops--;
                        if (CanDebug) Log.Debug($"aDistanceMs 3 = {aDistanceMs}");
                        AudioDecoder.Frames.TryDequeue(out aFrame);
                        if (aFrame != null)
                            aFrame.timestamp = (long)(aFrame.timestamp / Speed) - Audio.GetDeviceDelay();
                    }
                    //else
                        //if (CanDebug) Log.Debug($"aDistanceMs 3 = {aDistanceMs} | limit reached");
                }
                else if (aDistanceMs < -5) // Will be transfered back to decoder to drop invalid timestamps
                {
                    if (VideoDemuxer.BufferedDuration < Config.Player.MinBufferDuration)
                    {
                        requiresBuffering = true;
                        continue;
                    }

                    if (CanInfo) Log.Info($"aDistanceMs = {aDistanceMs}");

                    if (aDistanceMs < -600)
                    {
                        if (CanTrace) Log.Trace($"All audio frames disposed");
                        Audio.framesDropped += AudioDecoder.Frames.Count;
                        AudioDecoder.DisposeFrames();
                        aFrame = null;
                    }
                    else
                    {
                        int maxdrop = Math.Max(Math.Min((vDistanceMs - sleepMs) - 1, 20), 3);
                        for (int i=0; i<maxdrop; i++)
                        {
                            if (CanTrace) Log.Trace($"aDistanceMs 2 = {aDistanceMs}");
                            Audio.framesDropped++;
                            AudioDecoder.Frames.TryDequeue(out aFrame);
                            if (aFrame != null)
                                aFrame.timestamp = (long)(aFrame.timestamp / Speed) - Audio.GetDeviceDelay();
                            aDistanceMs = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;

                            if (aDistanceMs > 0)
                                break;
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
                    if (CanWarn) Log.Info($"sDistanceMs = {sDistanceMs}");

                    sFrame = null;
                    SubtitlesDecoder.Frames.TryDequeue(out SubtitlesFrame devnull);
                }
            }
        }

        if (CanInfo) Log.Info($"Finished -> {TicksToTime(CurTime)}");
    }

    private bool AudioBuffer()
    {
        while ((isVideoSwitch || isAudioSwitch) && IsPlaying) Thread.Sleep(10);
        if (!IsPlaying) return false;

        aFrame = null;
        Audio.ClearBuffer();
        decoder.AudioStream.Demuxer.Start();
        AudioDecoder.Start();

        while(AudioDecoder.Frames.IsEmpty && IsPlaying && AudioDecoder.IsRunning) Thread.Sleep(10);
        AudioDecoder.Frames.TryDequeue(out aFrame);
        if (aFrame == null) 
            return false;

        if (seeks.IsEmpty)
        {
            if (MainDemuxer.IsHLSLive)
                curTime = aFrame.timestamp;
            UI(() => UpdateCurTime());
        }

        while(seeks.IsEmpty && decoder.AudioStream.Demuxer.BufferedDuration < Config.Player.MinBufferDuration && IsPlaying && decoder.AudioStream.Demuxer.IsRunning && decoder.AudioStream.Demuxer.Status != MediaFramework.Status.QueueFull) Thread.Sleep(20);

        if (!IsPlaying || AudioDecoder.Frames.IsEmpty || !seeks.IsEmpty)
            return false;

        return true;
    }
    private void ScreamerAudioOnly()
    {
        int aDistanceMs;
        long elapsedSec = 0;

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
                        Log.Warn("Seek failed 1");
                }
                else
                {
                    if (decoder.SeekAudio(seekData.ms, seekData.forward) < 0)
                        Log.Warn("Seek failed 2");
                }
            }

            if (requiresBuffering)
            {
                OnBufferingStarted();
                AudioBuffer();
                requiresBuffering = false;
                if (!seeks.IsEmpty)
                    continue;
                OnBufferingCompleted();
                if (aFrame == null) { Log.Warn("[MediaBuffer] No audio frame"); break; }
                videoStartTicks = aFrame.timestamp;

                Audio.AddSamples(aFrame);
                AudioDecoder.Frames.TryDequeue(out aFrame);
                
                sw.Restart();
                elapsedSec = 0;
            }

            if (aFrame == null)
            {
                if (AudioDecoder.Status == MediaFramework.Status.Ended)
                    break;

                Log.Warn("No audio frames");
                requiresBuffering = true;
                continue;
            }

            if (Status != Status.Playing) break;

            elapsedTicks = videoStartTicks + sw.ElapsedTicks;
            aDistanceMs  = (int) ((aFrame.timestamp - elapsedTicks) / 10000);

            if (aDistanceMs > 1000 || aDistanceMs < -10)
            {
                requiresBuffering = true;
                continue;
            }

            if (aDistanceMs > 2)
            {
                if (Engine.Config.UICurTimePerSecond && (
                    (!MainDemuxer.IsHLSLive && curTime / 10000000 != _CurTime / 10000000) ||
                    (MainDemuxer.IsHLSLive && Math.Abs(elapsedTicks - elapsedSec) > 10000000)))
                {
                    elapsedSec = elapsedTicks;
                    UI(() => UpdateCurTime());
                }

                if (Audio.BuffersQueued == 0)
                    Thread.Sleep(aDistanceMs);
                else if (Audio.BuffersQueued < 4)
                    Thread.Sleep(aDistanceMs / 2);
                else
                    Thread.Sleep(aDistanceMs);
            }

            if (!MainDemuxer.IsHLSLive && seeks.IsEmpty)
            {
                curTime = aFrame.timestamp;

                if (Config.Player.UICurTimePerFrame)
                    UI(() => UpdateCurTime());
            }

            //Log($"{Utils.TicksToTime(aFrame.timestamp)}");
            Audio.AddSamples(aFrame);
            AudioDecoder.Frames.TryDequeue(out aFrame);
        }
    }

    private void ScreamerReverse()
    {
        long    elapsedSec = 0;
        int     vDistanceMs;
        int     sleepMs;

        while (Status == Status.Playing)
        {
            if (seeks.TryPop(out SeekData seekData))
            {
                seeks.Clear();
                if (decoder.Seek(seekData.ms, seekData.forward) < 0)
                    Log.Warn("Seek failed");
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

                while (VideoDecoder.Frames.IsEmpty && Status == Status.Playing && VideoDecoder.IsRunning) Thread.Sleep(15);
                OnBufferingCompleted();
                VideoDecoder.Frames.TryDequeue(out vFrame);
                if (vFrame == null) { Log.Warn("No video frame"); break; }
                vFrame.timestamp = (long) (vFrame.timestamp / Speed);

                videoStartTicks = vFrame.timestamp;
                sw.Restart();
                elapsedSec = 0;

                if (!MainDemuxer.IsHLSLive && seeks.IsEmpty)
                    curTime = (long) (vFrame.timestamp * Speed);
                UI(() => UpdateCurTime());
            }

            elapsedTicks    = videoStartTicks - sw.ElapsedTicks;
            vDistanceMs     = (int) ((elapsedTicks - vFrame.timestamp) / 10000);
            sleepMs         = vDistanceMs - 1;

            if (sleepMs < 0) sleepMs = 0;

            if (Math.Abs(vDistanceMs - sleepMs) > 5)
            {
                //Log($"vDistanceMs |-> {vDistanceMs}");
                VideoDecoder.DisposeFrame(vFrame);
                vFrame = null;
                Thread.Sleep(5);
                continue; // rebuffer
            }

            if (sleepMs > 2)
            {
                if (sleepMs > 1000)
                {
                    //Log($"sleepMs -> {sleepMs} , vDistanceMs |-> {vDistanceMs}");
                    VideoDecoder.DisposeFrame(vFrame);
                    vFrame = null;
                    Thread.Sleep(5);
                    continue; // rebuffer
                }

                // Every seconds informs the application with CurTime / Bitrates (invokes UI thread to ensure the updates will actually happen)
                if (Engine.Config.UICurTimePerSecond && (
                    (!MainDemuxer.IsHLSLive && curTime / 10000000 != _CurTime / 10000000) || 
                    (MainDemuxer.IsHLSLive && Math.Abs(elapsedTicks - elapsedSec) > 10000000)))
                {
                    elapsedSec  = elapsedTicks;
                    UI(() => UpdateCurTime());
                }

                Thread.Sleep(sleepMs);
            }

            decoder.VideoDecoder.Renderer.Present(vFrame);
            if (!MainDemuxer.IsHLSLive && seeks.IsEmpty)
            {
                curTime = (long) (vFrame.timestamp * Speed);

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
