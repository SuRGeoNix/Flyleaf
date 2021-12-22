using System;
using System.Threading;

using FlyleafLib.MediaFramework.MediaDecoder;

using static FlyleafLib.Utils;
using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.MediaPlayer
{
    partial class Player
    {
        /// <summary>
        /// Fires on playback stopped by an error or completed / ended successfully
        /// Warning: Uses Invoke and it comes from playback thread so you can't pause/stop etc. You need to use another thread if you have to.
        /// </summary>
        public event EventHandler<PlaybackCompletedArgs> PlaybackCompleted;
        protected virtual void OnPlaybackCompleted(string error = null)
        {
            PlaybackCompleted?.Invoke(this, new PlaybackCompletedArgs(error));

            #if DEBUG
            Log($"OnPlaybackCompleted {(error != null ? $"(Error: {error})" : "")}");
            #endif
        }

        /// <summary>
        /// Plays AVS streams
        /// </summary>
        public void Play()
        {
            lock (lockPlayPause)
            {
                if (!CanPlay || Status == Status.Playing)
                    return;

                status = Status.Playing;
                UI(() => Status = Status);
                EnsureThreadDone(tSeek);
                EnsureThreadDone(tPlay);

                tPlay = new Thread(() =>
                {
                    try
                    {
                        TimeBeginPeriod(1);
                        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED);

                        mainDemuxer = VideoDemuxer;
                        onBufferingStarted   = 0;
                        onBufferingCompleted = 0;

                        if (Config.Player.Usage == Usage.LowLatencyVideo)
                            ScreamerLowLatency();
                        else if (Config.Player.Usage == Usage.Audio || !Video.IsOpened)
                        {
                            mainDemuxer = AudioDecoder.OnVideoDemuxer ? VideoDemuxer : AudioDemuxer;
                            ScreamerAudioOnly();
                        }
                        else
                        {
                            if (ReversePlayback)
                                ScreamerReverse();
                            else
                                Screamer();
                        }

                    } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }

                    finally
                    {
                        if (Status == Status.Stopped)
                            decoder?.Stop();
                        else if (decoder != null) 
                        {
                            decoder.PauseOnQueueFull();
                            decoder.PauseDecoders();
                        } 

                        Audio.ClearBuffer();
                        VideoDecoder.DisposeFrame(vFrame);
                        vFrame = null;
                        TimeEndPeriod(1);
                        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);

                        bool error = false;

                        // Missed Buffering Completed means error
                        if (onBufferingStarted - 1 == onBufferingCompleted)
                        {
                            error = IsPlaying;
                            OnBufferingCompleted(error ? "Unknown" : null);
                        }

                        if (IsPlaying)
                        {
                            if (!error)
                                error = isLive || Math.Abs(Duration - CurTime) > 3 * 1000 * 10000;

                            if (HasEnded)
                                status = Status.Ended;
                            else
                                status = Status.Paused;

                            OnPlaybackCompleted(error ? "Unknown" : null);
                        }

                        UI(() =>
                        {
                            Status = Status;
                            UpdateCurTime();
                        });
                    }
                });
                tPlay.Name = "Play";
                tPlay.IsBackground = true;
                tPlay.Start();
            }
        }

        /// <summary>
        /// Pauses AVS streams
        /// </summary>
        public void Pause()
        {
            lock (lockPlayPause)
            {
                if (!CanPlay || Status == Status.Ended)
                    return;

                status = Status.Paused;
                UI(() => Status = Status);
                EnsureThreadDone(tPlay);
            }
        }

        public void TogglePlayPause()
        {
            if (IsPlaying)
                Pause();
            else 
                Play();
        }

        public void ToggleReversePlayback()
        {
            ReversePlayback = !ReversePlayback;
        }

        /// <summary>
        /// Seeks backwards or forwards based on the specified ms to the nearest keyframe
        /// </summary>
        /// <param name="ms"></param>
        /// <param name="forward"></param>
        public void Seek(int ms, bool forward = false)
        {
            Seek(ms, forward, false);
        }

        /// <summary>
        /// Seeks at the exact timestamp (with half frame distance accuracy)
        /// </summary>
        /// <param name="ms"></param>
        public void SeekAccurate(int ms)
        {
            Seek(ms, false, true);
        }

        private void Seek(int ms, bool forward, bool accurate)
        {
            if (!CanPlay) return;

            // We consider already in UI thread
            curTime = ms * (long)10000;
            Raise(nameof(CurTime));
            //Set(ref _CurTime, curTime, false, nameof(CurTime));
            //UpdateCurTime();
            seeks.Push(new SeekData(ms, forward, accurate));

            decoder.OpenedPlugin?.OnBuffering();

            if (Status == Status.Playing) return;

            lock (lockSeek) { if (IsSeeking) return; IsSeeking = true; }

            tSeek = new Thread(() =>
            {
                //bool wasEnded = Status == Status.Ended || HasEnded;
                try
                {
                    TimeBeginPeriod(1);
                    
                    while (seeks.TryPop(out SeekData seekData) && CanPlay && !IsPlaying)
                    {
                        seeks.Clear();

                        if (!Video.IsOpened)
                        {
                            if (AudioDecoder.OnVideoDemuxer)
                            {
                                if (decoder.Seek(seekData.ms, seekData.forward) < 0)
                                    Log("[SEEK] Failed 2");

                                VideoDemuxer.Start();
                            }
                            else
                            {
                                if (decoder.SeekAudio(seekData.ms, seekData.forward) < 0)
                                    Log("[SEEK] Failed 3");

                                AudioDemuxer.Start();
                            }

                            decoder.PauseOnQueueFull();
                        }
                        else
                        {
                            VideoDecoder.Pause();
                            if (decoder.Seek(seekData.ms, seekData.forward, !seekData.accurate) >= 0)
                            {
                                if (!ReversePlayback && CanPlay)
                                    decoder.GetVideoFrame(seekData.accurate ? seekData.ms * (long)10000 : -1);
                                if (!ReversePlayback && CanPlay)
                                {
                                    ShowOneFrame();
                                    VideoDemuxer.Start();
                                    AudioDemuxer.Start();
                                    SubtitlesDemuxer.Start();
                                    decoder.PauseOnQueueFull();
                                }
                            }
                            else
                                Log("[SEEK] Failed");
                        }

                        Thread.Sleep(20);
                    }
                } catch (Exception e)
                {
                    Log($"[SEEK] Error {e.Message}");
                } finally
                {
                    decoder.OpenedPlugin?.OnBufferingCompleted();
                    TimeEndPeriod(1);
                    lock (lockSeek) IsSeeking = false;
                    if (Status == Status.Ended && Config.Player.AutoPlay)
                        Play();
                }
            });

            tSeek.Name = "Seek";
            tSeek.IsBackground = true;
            tSeek.Start();
        }

        /// <summary>
        /// Stops and Closes AVS streams
        /// </summary>
        public void Stop()
        {
            canPlay = false;

            lock (this)
            {
                status = Status.Stopped;
                UI(() =>
                {
                    CanPlay = CanPlay;
                    Status  = Status;
                });

                EnsureThreadDone(tPlay);
                if (IsDisposed || decoder == null) return;
                decoder.Stop();
                lock (lockSeek)
                    lock (lockOpen)
                        Initialize();
            }
        }
    }

    public class PlaybackCompletedArgs : EventArgs
    {
        public string   Error       { get; }
        public bool     Success     { get; }
            
        public PlaybackCompletedArgs(string error)
        {
            Error   = error;
            Success = Error == null;
        }
    }

    class SeekData
    {
        public int  ms;
        public bool forward;
        public bool accurate;
        public SeekData(int ms, bool forward, bool accurate)
            { this.ms = ms; this.forward = forward && !accurate; this.accurate = accurate; }
    }

}
