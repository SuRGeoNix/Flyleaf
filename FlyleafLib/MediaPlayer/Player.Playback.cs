using System;
using System.Threading;
using System.Threading.Tasks;

using FlyleafLib.MediaFramework.MediaDecoder;

using static FlyleafLib.Utils;
using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.MediaPlayer
{
    partial class Player
    {
        /// <summary>
        /// Fires on playback ended successfully
        /// </summary>
        public event EventHandler PlaybackCompleted;
        protected virtual void OnPlaybackCompleted() { PlaybackCompleted?.BeginInvoke(this, new EventArgs(), null, null); }

        public event EventHandler BufferingStarted;
        protected virtual void OnBufferingStarted() { BufferingStarted?.BeginInvoke(this, new EventArgs(), null, null); }

        public event EventHandler BufferingCompleted;
        protected virtual void OnBufferingCompleted() { BufferingCompleted?.BeginInvoke(this, new EventArgs(), null, null); }

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

                        if (HasEnded)
                        {
                            //curTime = Duration * Config.Player.Speed;
                            status = Status.Ended;
                            UI(() =>
                            {
                                Status = Status;
                                UpdateCurTime();
                            });
                            OnPlaybackCompleted();
                        }
                        else
                        {
                            status = Status.Paused;
                            UI(() => Status = Status);
                        }
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
            if (!CanPlay) return;

            // We consider already in UI thread
            curTime = ms * (long)10000;
            Raise(nameof(CurTime));
            //Set(ref _CurTime, curTime, false, nameof(CurTime));
            //UpdateCurTime();
            seeks.Push(new SeekData(ms, forward));

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
                            if (decoder.Seek(seekData.ms, seekData.forward) >= 0)
                            {
                                if (!ReversePlayback && CanPlay)
                                    decoder.GetVideoFrame();
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
                if (disposed || decoder == null) return;
                decoder.Stop();
                lock (lockSeek)
                    lock (lockOpen)
                        Initialize();
            }
        }
    }
    class SeekData
    {
        public int  ms;
        public bool forward;
        public SeekData(int ms, bool forward)
            { this.ms = ms; this.forward = forward; }
    }

}
