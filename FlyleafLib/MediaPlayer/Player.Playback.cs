using FlyleafLib.MediaFramework.MediaDecoder;

namespace FlyleafLib.MediaPlayer;

partial class Player
{
    string stoppedWithError = null;

    /// <summary>
    /// Fires on playback stopped by an error or completed / ended successfully <see cref="Status"/>
    /// Warning: Uses Invoke and it comes from playback thread so you can't pause/stop etc. You need to use another thread if you have to.
    /// </summary>
    public event EventHandler<PlaybackStoppedArgs> PlaybackStopped;
    protected virtual void OnPlaybackStopped(string error = null)
    {
        if (error != null && lastError == null)
        {
            lastError = error;
            UI(() => LastError = lastError);
        }

        PlaybackStopped?.Invoke(this, new PlaybackStoppedArgs(error));
    }

    /// <summary>
    /// Fires when a queued seek is taken for execution. Superseded queued requests do not fire.
    /// Invoked on the seek/playback worker; handlers must not synchronously pause or stop playback.
    /// </summary>
    public event EventHandler<int> SeekStarted;

    /// <summary>
    /// Fires after a seek operation for the requested ms (-1 on failure), before any subsequent playback buffering.
    /// Invoked on the seek/playback worker; handlers must not synchronously pause or stop playback.
    /// </summary>
    /// <remarks>
    /// Obsolete. Use <see cref="SeekResultCompleted"/> instead for cancellation-aware seek results.
    /// </remarks>
    [Obsolete("Use SeekResultCompleted instead. This event will be removed in v4.")]
    public event EventHandler<int> SeekCompleted;

    /// <summary>
    /// Fires after a seek operation completes, providing the requested position and whether the operation was cancelled.
    /// Unlike <see cref="SeekCompleted"/>, distinguishes interrupted or superseded seeks from genuine failures.
    /// Invoked on the seek/playback worker; handlers must not synchronously pause or stop playback.
    /// </summary>
    public event EventHandler<FlyleafSeekResult> SeekResultCompleted;

    /// <summary>
    /// Plays AVS streams
    /// </summary>
    public void Play()
    {
        int requestId;

        lock (lockActions)
        {
            if (!canPlay || status == Status.Playing || status == Status.Ended || IsDisposed)
                return;

            status = Status.Playing;
            requestId = ++playRequestId;
            UI(() => Status = status);
        }

        _ = Task.Run(() => StartPlayWhenReady(requestId));
    }

    void StartPlayWhenReady(int requestId)
    {
        while (true)
        {
            while (taskPlayRuns || taskSeekRuns)
            {
                if (Volatile.Read(ref playRequestId) != requestId)
                    return;

                Thread.Sleep(5);
            }

            Exception startError = null;
            bool startSeekTask = false;
            lock (lockActions)
            {
                if (requestId != playRequestId || !canPlay ||
                    status != Status.Playing || IsDisposed)
                    return;

                lock (seeks)
                {
                    if (taskPlayRuns || taskSeekRuns)
                        continue;

                    Thread t = new(PlayThread)
                    {
                        #if DEBUG
                        Name            = $"[#{PlayerId}] Playback",
                        #endif
                        Priority        = Config.Playback.ThreadPriority,
                        IsBackground    = true
                    };

                    taskPlayRuns = true;
                    try
                    {
                        t.Start();
                    }
                    catch (Exception ex)
                    {
                        taskPlayRuns = false;
                        status = Status.Paused;
                        startError = ex;
                        startSeekTask = canPlay && !seeks.IsEmpty;
                        if (startSeekTask)
                            taskSeekRuns = true;
                    }
                }
            }

            if (startSeekTask)
                StartPausedSeekTask();

            if (startError != null)
            {
                UI(() => Status = status);
                Log.Error($"Playback thread failed to start ({startError.Message})");
            }

            return;
        }
    }

    void PlayThread()
    {
        try
        {
            Engine.TimeBeginPeriod1();
            Engine.ThreadExecutionStateBegin();

            onBufferingStarted   = 0;
            onBufferingCompleted = 0;
            requiresBuffering    = true;

            if (lastError != null)
            {
                lastError = null;
                UI(() => LastError = lastError);
            }

            if (Usage == Usage.Audio || !Video.IsOpened)
                ScreamerAudioOnly();
            else
            {
                if (Config.Playback.ZeroLatency)
                    ScreamerZeroLatency();
                else if (ReversePlayback)
                {
                    shouldFlushNext = true;
                    ScreamerReverse();
                }
                else
                {
                    shouldFlushPrev = true;
                    ScreamerVASD();
                }

                vFrame = null;
            }

        }
        catch (Exception e)
        {
            Log.Error($"Playback failed ({e.Message})");
        }
        finally
        {
            try
            {
                sFrame = null;

                if (status != Status.Stopped && decoder != null)
                {
                    decoder.PauseOnQueueFull();
                    decoder.PauseDecoders();
                }

                Audio.ClearBuffer();
                Engine.TimeEndPeriod1();
                Engine.ThreadExecutionStateEnd();
                stoppedWithError = null;

                if (status == Status.Playing)
                {
                    if (decoderHasEnded)
                        status = Status.Ended;
                    else
                    {
                        if (Video.IsOpened && VideoDemuxer.Interrupter.Timedout)
                            stoppedWithError = "Timeout";
                        else if (onBufferingStarted - 1 == onBufferingCompleted)
                        {
                            stoppedWithError = "Playback stopped unexpectedly";
                            OnBufferingCompleted("Buffering failed");
                        }
                        else
                        {
                            if (!ReversePlayback)
                            {
                                if (isLive || Math.Abs(duration - curTime) > 3 * 1000 * 10000)
                                    stoppedWithError = "Playback stopped unexpectedly";
                            }
                            else if (curTime > 3 * 1000 * 10000)
                                stoppedWithError = "Playback stopped unexpectedly";
                        }

                        status = Status.Paused;
                    }
                }

                OnPlaybackStopped(stoppedWithError);
                if (CanDebug) Log.Debug($"[SCREAMER] Finished (Status: {status}, Error: {stoppedWithError})");

                UI(() =>
                {
                    Status = status;
                    SetCurTime();
                });
            }
            finally
            {
                bool startSeekTask;
                lock (seeks)
                {
                    taskPlayRuns = false;
                    startSeekTask = canPlay && !seeks.IsEmpty && !taskSeekRuns;
                    if (startSeekTask)
                        taskSeekRuns = true;
                }

                if (startSeekTask)
                    StartPausedSeekTask();
            }
        }
    }

    /// <summary>
    /// Pauses AVS streams
    /// </summary>
    public void Pause()
    {
        lock (lockActions)
        {
            if (!canPlay || status == Status.Ended)
                return;

            playRequestId++;
            status = Status.Paused;
            UI(() => Status = status);

            while (taskPlayRuns) Thread.Sleep(5);
        }
    }

    public void TogglePlayPause()
    {
        if (status == Status.Playing)
            Pause();
        else
            Play();
    }

    public void ToggleReversePlayback()
        => ReversePlayback = !ReversePlayback;

    public void ToggleLoopPlayback()
        => LoopPlayback = !LoopPlayback;

    /// <summary>
    /// Seeks backwards or forwards based on the specified ms to the nearest keyframe
    /// </summary>
    /// <param name="ms"></param>
    /// <param name="forward"></param>
    public void Seek(int ms, bool forward = false) => Seek(ms, forward, false);

    /// <summary>
    /// Seeks at the exact timestamp (with half frame distance accuracy)
    /// </summary>
    /// <param name="ms"></param>
    public void SeekAccurate(int ms)
        => Seek(ms, false, !IsLive);

    public void ToggleSeekAccurate()
        => Config.Playback.SeekAccurate = !Config.Playback.SeekAccurate;

    private void Seek(int ms, bool forward, bool accurate)
    {
        bool startSeekTask;
        lock (seeks)
        {
            if (!canPlay)
                return;

            _CurTime = curTime = ms * (long)10000;
            var version = Interlocked.Increment(ref seekRequestVersion);
            seeks.Push(new SeekData(ms, forward, accurate, version, OpenCancellationVersion));

            startSeekTask = !taskPlayRuns && !taskSeekRuns;
            if (startSeekTask)
                taskSeekRuns = true;
        }

        if (startSeekTask)
            StartPausedSeekTask();

        // TBR: We consider UI here? (_CurTime loses sync)
        // BufferedDuration = 0; Can't se this as it reads the demuxer's (which we didn't seek yet..)
        Raise(nameof(CurTime));
    }

    void StartPausedSeekTask()
    {
        try
        {
            _ = Task.Run(PausedSeekTask);
        }
        catch (Exception ex)
        {
            lock (seeks) taskSeekRuns = false;
            Log.Error($"Failed to start seek task ({ex.Message})");
        }
    }

    void PausedSeekTask()
    {
        int ret;
        bool wasEnded = false;
        bool seekExecuting = false;
        SeekData seekData = null;

        try
        {
            Engine.TimeBeginPeriod1();

            while (true)
            {
                lock (seeks)
                {
                    if (!canPlay || !seeks.TryPop(out seekData))
                        break;

                    seeks.Clear();
                }

                seekExecuting = true;
                SeekStarted?.Invoke(this, seekData.ms);

                if (status == Status.Ended)
                {
                    wasEnded = true;
                    status = Status.Paused;
                    UI(() => Status = status);
                }

                if (sFramePrev != null)
                {
                    sFramePrev = null;
                    Renderer.SubsDispose();
                    Subtitles.ClearSubsText();
                }

                if (!Video.IsOpened)
                {
                    if (AudioDecoder.OnVideoDemuxer)
                    {
                        decoder.PauseDecoders();
                        ret = decoder.Seek(seekData.ms, seekData.forward);
                        if (CanWarn && ret < 0)
                            Log.Warn("Seek failed 2");

                        VideoDemuxer.Start();
                    }
                    else
                    {
                        ret = decoder.SeekAudio(seekData.ms, seekData.forward);
                        if (CanWarn && ret < 0)
                            Log.Warn("Seek failed 3");

                        AudioDemuxer.Start();
                    }

                    decoder.PauseOnQueueFull();
                    seekExecuting = false;
                    NotifySeekResult(ret < 0 ? -1 : seekData.ms, seekData);
                }
                else
                {
                    int? seekCompletedMs = null;

                    lock (stepSeekLock)
                    {
                        decoder.PauseDecoders();
                        ret = decoder.Seek(seekData.accurate ? Math.Max(0, seekData.ms - 3000) : seekData.ms, seekData.forward, !seekData.accurate); // 3sec ffmpeg bug for seek accurate when fails to seek backwards (see videodecoder getframe)
                        if (ret < 0)
                        {
                            if (CanWarn) Log.Warn("Seek failed");
                            seekCompletedMs = -1;
                        }
                        else if (!ReversePlayback && CanPlay)
                        {
                            decoder.GetVideoFrame(seekData.accurate ? seekData.ms * (long)10000 : -1);
                            ResetFrameStats();
                            ShowOneFrame();
                            VideoDemuxer.Start();
                            AudioDemuxer.Start();
                            SubtitlesDemuxer.Start();
                            DataDemuxer.Start();
                            decoder.PauseOnQueueFull();
                            seekCompletedMs = seekData.ms;
                        }
                        else seekCompletedMs = CanPlay ? seekData.ms : -1;
                    }

                    if (seekCompletedMs is int seekCompletedMsValue)
                    {
                        seekExecuting = false;
                        NotifySeekResult(seekCompletedMsValue, seekData);
                    }
                }

                Thread.Sleep(20);
            }
        }
        catch (Exception e)
        {
            if (seekExecuting) { seekExecuting = false; NotifySeekResult(-1, seekData); }
            Log.Error($"Seek failed ({e.Message})");
        }
        finally
        {
            try
            {
                decoder.OpenedPlugin?.OnBufferingCompleted();
            }
            finally
            {
                try
                {
                    Engine.TimeEndPeriod1();
                }
                finally
                {
                    bool restartSeek;
                    lock (seeks)
                    {
                        restartSeek = canPlay && !seeks.IsEmpty && !taskPlayRuns;
                        if (!restartSeek)
                            taskSeekRuns = false;
                    }

                    if (restartSeek)
                        StartPausedSeekTask();

                    if ((wasEnded && Config.Playback.AutoPlay) || stoppedWithError != null) // TBR: Possible race condition with if (Status == Status.Playing)
                        Play();
                }
            }
        }
    }

    /// <summary>
    /// Flushes the buffer (demuxers (packets) and decoders (frames))
    /// This is useful mainly for live streams to push the playback at very end (low latency)
    /// </summary>
    public void Flush()
        => decoder.Flush();

    /// <summary>
    /// Stops and Closes AVS streams
    /// </summary>
    public void Stop()
    {
        Interlocked.Increment(ref openCancellationVersion);
        Interlocked.Increment(ref seekRequestVersion);
        lock (lockActions)
        {
            Initialize();
            Renderer?.Reset();
        }
    }
}

public class PlaybackStoppedArgs : EventArgs
{
    public string   Error       { get; }
    public bool     Success     { get; }

    public PlaybackStoppedArgs(string error)
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
    public long Version;
    public long OpenVersion;
    public SeekData(int ms, bool forward, bool accurate, long version, long openVersion)
        { this.ms = ms; this.forward = forward && !accurate; this.accurate = accurate; Version = version; OpenVersion = openVersion; }
}
