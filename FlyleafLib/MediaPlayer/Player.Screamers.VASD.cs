﻿using Vortice.Direct3D11;

using FlyleafLib.MediaFramework.MediaDecoder;

namespace FlyleafLib.MediaPlayer;

unsafe partial class Player
{
    const int MAX_DEQUEUE_RETRIES = 20; // (Ms) Tries to avoid re-buffering by waiting the decoder to catch up
    volatile bool isScreamerVASDAudio;
    volatile bool stopScreamerVASDAudio;
    long startTicks, lastSpeedChangeTicks;

    bool BufferVASD()
    {
        /* [Requires recoding]
         * We have three different statuses during A/V opening: Closed/Failed, Opening, Opened (Opened means filled from codec / valid format after 'analyze')
         *  we currently use isOpened only (as two statuses)
         */

        bool gotAudio       = !Audio.IsOpened || Config.Player.MaxLatency != 0;
        bool gotVideo       = false;
        bool shouldStop     = false;
        bool showOneFrame   = true;
        int  audioRetries   = 4;
        int  loops          = 0;

        if (CanTrace) Log.Trace("Buffering");

        while (isVideoSwitch && IsPlaying) Thread.Sleep(10);

        VideoDemuxer.Start();
        VideoDecoder.Start();

        if (Audio.isOpened && Config.Audio.Enabled)
        {
            if (AudioDecoder.OnVideoDemuxer)
                AudioDecoder.Start();
            else if (!decoder.RequiresResync)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }
        }

        if (Subtitles.isOpened && Config.Subtitles.Enabled)
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

        if (Data.isOpened && Config.Data.Enabled)
        {
            if (DataDecoder.OnVideoDemuxer)
                DataDecoder.Start();
            else if (!decoder.RequiresResync)
            {
                DataDemuxer.Start();
                DataDecoder.Start();
            }
        }

        if (vFrame != null && vFrame != renderer.LastFrame) // TBR: lock?
            VideoDecoder.DisposeFrame(vFrame);
        vFrame = null; aFrame = null; sFrame = null; dFrame = null;

        if (Config.Player.MaxLatency != 0)
        {
            lastSpeedChangeTicks = DateTime.UtcNow.Ticks;
            showOneFrame = false;
            speed = AudioDecoder.Speed = VideoDecoder.Speed = 1;
        }

        do
        {
            loops++;

            if (showOneFrame && !vFrames.IsEmpty)
            {
                ShowOneFrame();
                showOneFrame = false;
            }

            // We allo few ms to show a frame before cancelling
            if ((!showOneFrame || loops > 8) && !seeks.IsEmpty)
                return false;

            if (!gotVideo && !showOneFrame && vFrames.TryDequeue(out vFrame))
                gotVideo = true;

            if (!gotAudio && aFrame == null && !AudioDecoder.Frames.IsEmpty)
                AudioDecoder.Frames.TryDequeue(out aFrame);

            if (gotVideo)
            {
                if (decoder.RequiresResync)
                    decoder.Resync(vFrame.timestamp);

                if (!gotAudio && aFrame != null && Audio.isOpened) // Could be closed from invalid sample rate
                {
                    long aDeviceDelay = Audio.GetDeviceDelay();

                    for (int i = 0; i < Math.Min(20, AudioDecoder.Frames.Count); i++)
                    {
                        if (aFrame == null
                            || aFrame.timestamp - aDeviceDelay > vFrame.timestamp
                            || vFrame.timestamp > duration)
                        {
                            gotAudio = true;
                            break;
                        }

                        if (CanTrace) Log.Trace($"Drop aFrame {TicksToTimeMini(aFrame.timestamp)}");
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

                if (gotVideo && !gotAudio && audioRetries > 0 && (!AudioDecoder.IsRunning || AudioDecoder.Demuxer.Status == MediaFramework.Status.QueueFull))
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

        // Negative Buffer Duration during codec change (we don't dipose the cached frames or we receive them later) *skip waiting for now
        var bufDuration = GetBufferedDuration();
        if (bufDuration >= 0)
            while(seeks.IsEmpty && bufDuration < Config.Player.MinBufferDuration && status == Status.Playing && VideoDemuxer.IsRunning && VideoDemuxer.Status != MediaFramework.Status.QueueFull)
            {
                Thread.Sleep(20);
                bufDuration = GetBufferedDuration();
                if (bufDuration < 0)
                    break;
            }

        if (!seeks.IsEmpty)
            return false;

        decoder.OpenedPlugin.OnBufferingCompleted();

        return true;
    }
    void ScreamerVASD()
    {
        /* [TODO]
         * SecondField goes to Renderer
         * isVideoSwitch should be removed as we use requiresBuffering all time for that?*
         * Warm up to avoid initial drop frames and get accurate FrameStatistics / refresh rate sync with monitor (cpu-gpu sync, possible with Dwm?)
         * We actually need +1 extra frame not for renderer but for the time that we TryDequeue 1 frame and at the same time we keep the LastFrame for the renderer
         *  If we swap it directly we will not need this anymore (however this could cause losing the LastFrame if the new one is corrupted/failed etc.)
         *  We could also get rid of vFrame (use only LastFrame) and no bother disposing it just keep the timestamp from renderframe until present
         */

        bool secondField = false; // To be transfered in renderer
        int dequeueRetries;
        int vDistanceMs, sDistanceMs, dDistanceMs;
        long elapsedTicks;
        bool refreshed = false;
        
        while (status == Status.Playing)
        {
            // Seeks and then requiresBuffering | TBR: missing SeekCompleted callback?
            if (seeks.TryPop(out var seekData))
            {
                if (vFrame != null && vFrame != renderer.LastFrame) // TBR: lock*?
                    { VideoDecoder.DisposeFrame(vFrame); vFrame = null; }

                renderer.RenderPlayStop();

                seeks.Clear();
                requiresBuffering = true;
                ResetFrameStats();

                if (sFramePrev != null)
                {
                    sFramePrev = null;
                    renderer.ClearOverlayTexture();
                    Subtitles.ClearSubsText();
                }

                decoder.PauseDecoders(); // TBR: Required to avoid gettings packets between Seek and ShowFrame which causes resync issues
                StopScreamerVASDAudio();

                if (decoder.Seek(seekData.accurate ? Math.Max(0, seekData.ms - 3000) : seekData.ms, seekData.forward, !seekData.accurate) < 0) // Consider using GetVideoFrame with no timestamp (any) to ensure keyframe packet for faster seek in HEVC
                    Log.Warn("[V] Seek Failed");
                else if (seekData.accurate)
                    decoder.GetVideoFrame(seekData.ms * (long)10000);
            }

            // Ensures we have rendered vFrame ready for present (startTicks/sw)
            if (requiresBuffering)
            {
                if (vFrame != null && vFrame != renderer.LastFrame) // TBR: lock*?
                    { VideoDecoder.DisposeFrame(vFrame); vFrame = null; }

                renderer.RenderPlayStop();

                if (Config.Player.Stats && framesDisplayedDwmEnd > 0)
                {
                    framesDisplayedDwmEnd   = renderer.GetFrameStatistics().PresentCount - framesDisplayedDwmStart;
                    framesDisplayedDwm     += framesDisplayedDwmEnd;
                }

                if (VideoDemuxer.Interrupter.Timedout)
                    break;

                secondField = false;

                OnBufferingStarted();
                StopScreamerVASDAudio();
                BufferVASD();
                requiresBuffering = false;
                if (!seeks.IsEmpty)
                    continue;

                if (vFrame == null)
                {
                    if (decoderHasEnded)
                        OnBufferingCompleted();
                    else
                        Log.Warn("[V] Buffer Empty");

                    break;
                }

                renderer.RenderPlayStart();

                // Prepare 2nd Frame (after ShowOneFrame)
                if (!renderer.RenderPlay(vFrame, false)) // interlace 2nd frame probably here
                {
                    framesFailed++;
                    vFrame = null;
                    requiresBuffering = true;
                    continue;
                }

                if (Config.Player.Stats)
                    framesDisplayedDwmStart = renderer.GetFrameStatistics().PresentCount;

                startTicks = vFrame.timestamp;
                OnBufferingCompleted();
                if (CanInfo) Log.Info($"[V] Started at {TicksToTimeMini(vFrame.timestamp)}]" + (aFrame == null ? "" : $" [A: {TicksToTimeMini(aFrame.timestamp)}]"));
                sw.Restart();
                StartScreamerVASDAudio();
            }

            if (status != Status.Playing)
                break;

            // By the end of the loop we must have vFrame ready for present
            if (vFrame == null)
            {
                if (VideoDecoder.Status == MediaFramework.Status.Ended) // TBR: Transfer to Playback finally for all screamers*?
                {
                    UpdateCurTime(
                        Math.Abs(VideoDemuxer.Duration - curTime) < 2 * VideoDemuxer.VideoStream.FrameDuration ?
                        VideoDemuxer.Duration :
                        curTime + VideoDemuxer.VideoStream.FrameDuration);

                    break;
                }

                Log.Warn("[V] Buffer Empty");
                requiresBuffering = true;
                continue;
            }

            // Valid for Present | -3 to 2 (5ms breath)
            elapsedTicks = (long)(sw.ElapsedTicks * SWFREQ_TO_TICKS);
            vDistanceMs  = (int) ((((vFrame.timestamp - startTicks) / speed) - elapsedTicks) / 10000);

            // CPU Drops to avoid dropping in GPU and also losing sync
            if (vDistanceMs < -3)
            {
                if (CanDebug) Log.Debug($"[V] Frame Dropped ({vDistanceMs})");

                vFrame      = null; // don't dispose (LastFrame)
                secondField = false;
                framesFailed++;

                if (vDistanceMs < -50)// || GetBufferedDuration() < Config.Player.MinBufferDuration / 2)
                {
                    requiresBuffering = true;
                    continue;
                }

                dequeueRetries = MAX_DEQUEUE_RETRIES;
                do
                {
                    while (!isVideoSwitch && !vFrames.TryDequeue(out vFrame) && dequeueRetries-- > 0)
                        Thread.Sleep(1);

                    if (vFrame == null)
                        break;

                    if ((int) ((((vFrame.timestamp - startTicks) / speed) - (long) (sw.ElapsedTicks * SWFREQ_TO_TICKS)) / 10_000) >= 0)
                        break; // found

                    framesFailed++;
                    if (CanDebug) Log.Debug($"[V] Frame Dropped ({(int) ((((vFrame.timestamp - startTicks) / speed) - (long) (sw.ElapsedTicks * SWFREQ_TO_TICKS)) / 10_000)}:M)");
                    VideoDecoder.DisposeFrame(vFrame); vFrame = null;
                } while (true);

                if (vFrame != null && !renderer.RenderPlay(vFrame, secondField))
                {
                    framesFailed++;
                    vFrame = null;
                    requiresBuffering = true;
                }

                continue;
            }

            // Thread Sleep in Parts | 60fps Idle Refresh | Restarts on Late Frame (2sec?)
            if (vDistanceMs > 2)
            {
                if (vDistanceMs > 11)
                {
                    if (status != Status.Playing)
                        break;

                    // Late Frame (e.g. HLS wrapped and that's a keyframe?* -we might need to avoid disposing vFrame here)
                    if (vDistanceMs > 2_000)
                    {
                        requiresBuffering = true;
                        Log.Warn($"[V] Late Frame ({vDistanceMs})");
                    }

                    // Keep ~60fps Idle refresh | TBR: We could avoid preparing frame too early -for slow fps- (as we discard it here)
                    else if (vDistanceMs > 16 && renderer.RefreshPlay(secondField))
                    {
                        if (CanTrace) Log.Trace($"[V] Refreshing {TicksToTime(vFrame.timestamp)}{(secondField ? " | SF" : "")}");
                        framesDisplayed++;
                        refreshed = true;
                    }
                    
                    // Sleep 10ms and recalculate distance
                    else
                        Thread.Sleep(10);

                    continue;
                }

                Thread.Sleep(vDistanceMs);
            }

            // Present Current | Render Next
            if (!refreshed)
            {
                if (CanTrace) Log.Trace($"[V] Presenting {TicksToTime(vFrame.timestamp)}{(secondField ? " | SF" : "")}");

                if (renderer.PresentPlay())
                {
                    framesDisplayed++;
                    UpdateCurTime(vFrame.timestamp, false);
                }
                else
                    framesFailed++;
            }
            else
                refreshed = false;

            if (Config.Player.MaxLatency != 0)
                CheckLatency();

            if (renderer.FieldType != VideoFrameFormat.Progressive && Config.Video.DoubleRate && !secondField)
            {
                secondField = true;
                vFrame.timestamp += renderer.VideoStream.FrameDuration2;
                if (!renderer.RenderPlay(vFrame, secondField))
                {
                    framesFailed++;
                    vFrame = null;
                }
            }
            else
            {
                vFrame          = null; // don't dispose (LastFrame)
                secondField     = false;
                dequeueRetries  = MAX_DEQUEUE_RETRIES;
                while (!isVideoSwitch && !vFrames.TryDequeue(out vFrame) && dequeueRetries-- > 0)
                    Thread.Sleep(1);

                if (vFrame != null && !renderer.RenderPlay(vFrame, secondField))
                {
                    framesFailed++;
                    vFrame = null;
                }
            }

            // Subs | Data (just transfered for now TBR - might lose sync with video above)
            if (Subtitles.isOpened)
            {
                elapsedTicks = (long)(sw.ElapsedTicks * SWFREQ_TO_TICKS);

                if (sFrame == null && !isSubsSwitch)
                    SubtitlesDecoder.Frames.TryPeek(out sFrame);

                sDistanceMs = sFrame != null
                    ? (int)((((sFrame.timestamp - startTicks) / speed) - elapsedTicks) / 10000)
                    : int.MaxValue;

                if (sFramePrev != null && ((sFramePrev.timestamp - startTicks + (sFramePrev.duration * (long)10000)) / speed) - elapsedTicks < 0)
                {
                    if (string.IsNullOrEmpty(sFramePrev.text))
                        renderer.ClearOverlayTexture();
                    else
                        Subtitles.ClearSubsText();

                    sFramePrev = null;
                }

                if (sFrame != null)
                {
                    if (Math.Abs(sDistanceMs) < 30 || (sDistanceMs < -30 && sFrame.duration + sDistanceMs > 0))
                    {
                        if (string.IsNullOrEmpty(sFrame.text))
                        {
                            if (sFrame.sub.num_rects > 0)
                            {
                                renderer.CreateOverlayTexture(sFrame, SubtitlesDecoder.CodecCtx->width, SubtitlesDecoder.CodecCtx->height);
                                SubtitlesDecoder.DisposeFrame(sFrame); // only rects
                            }
                            else
                                renderer.ClearOverlayTexture();
                        }
                        else
                        {
                            Subtitles.subsText = sFrame.text;
                            UI(() => Subtitles.SubsText = Subtitles.subsText);
                        }

                        sFramePrev = sFrame;
                        sFrame = null;
                        SubtitlesDecoder.Frames.TryDequeue(out var devnull);
                    }
                    else if (sDistanceMs < -30)
                    {
                        if (CanDebug) Log.Debug($"sDistanceMs = {sDistanceMs}");

                        SubtitlesDecoder.DisposeFrame(sFrame);
                        renderer.ClearOverlayTexture();
                        sFrame = null;
                        SubtitlesDecoder.Frames.TryDequeue(out var devnull);
                    }
                }
            }
            
            if (Data.isOpened)
            {
                elapsedTicks = (long)(sw.ElapsedTicks * SWFREQ_TO_TICKS);

                if (dFrame == null && !isDataSwitch)
                    DataDecoder.Frames.TryPeek(out dFrame);

                dDistanceMs = dFrame != null
                    ? (int)((((dFrame.timestamp - startTicks) / speed) - elapsedTicks) / 10000)
                    : int.MaxValue;

                if (dFrame != null)
                {
                    if (Math.Abs(dDistanceMs) < 30 || (dDistanceMs < -30))
                    {
                        OnDataFrame?.Invoke(this, dFrame);

                        dFrame = null;
                        DataDecoder.Frames.TryDequeue(out var devnull);
                    }
                    else if (dDistanceMs < -30)
                    {
                        if (CanDebug)
                            Log.Debug($"dDistanceMs = {dDistanceMs}");

                        dFrame = null;
                        DataDecoder.Frames.TryDequeue(out var devnull);
                    }
                }
            }

        } // while Playing

        if (vFrame != null && vFrame != renderer.LastFrame) // TBR: lock*?
            { VideoDecoder.DisposeFrame(vFrame); vFrame = null; }

        renderer.RenderPlayStop();  // Calls RenderRequest which ensure present of the last rendered frame
        StopScreamerVASDAudio();

        if (Config.Player.Stats)
        {
            Thread.Sleep(15); // wait for last present
            framesDisplayedDwmEnd   = SafeSubstract(renderer.GetFrameStatistics().PresentCount, framesDisplayedDwmStart);
            framesDisplayedDwm     += framesDisplayedDwmEnd;
            //Video.fpsCurrent        = 0;
            UI(() =>
            {
                Video.FramesDisplayed   = framesDisplayedDwm + showFrameCount;
                Video.FramesDropped     = SafeSubstract(framesFailed + framesDisplayed, framesDisplayedDwm);
                //Video.FPSCurrent        = Video.fpsCurrent;
            });

            if (CanInfo) Log.Info($"[V] Finished at {TicksToTimeMini(curTime)} | [Presented: {framesDisplayedDwm + showFrameCount}] [Dropped (CPU): {framesFailed}] [Dropped (GPU): {SafeSubstract(framesDisplayed, framesDisplayedDwm)}]]");
        }
        else if (CanInfo) Log.Info($"[V] Finished at {TicksToTimeMini(curTime)}");

    }
    private void CheckLatency()
    {
        long curLatency = GetBufferedDuration();

        if (CanDebug) Log.Debug($"[Latency {curLatency/10000}ms] Frames: {vFrames.Count} Packets: {vPackets.Count} Speed: {speed}");

        if (curLatency <= Config.Player.MinLatency) // We've reached the down limit (back to speed x1)
        {
            ChangeSpeedWithoutBuffering(1, curLatency);
            return;
        }
        else if (curLatency < Config.Player.MaxLatency)
            return;

        var newSpeed = Math.Max(Math.Round((double)curLatency / Config.Player.MaxLatency, 1, MidpointRounding.ToPositiveInfinity), 1.1);

        if (newSpeed > 4) // TBR: dispose only as much as required to avoid rebuffering
        {
            decoder.Flush();
            vFrame = null; // don't dispose (LastFrame)
            requiresBuffering = true;
            if (CanDebug) Log.Debug($"[Latency {curLatency/10000}ms] Clearing queue");
            return;
        }

        ChangeSpeedWithoutBuffering(newSpeed, curLatency);
    }
    void ChangeSpeedWithoutBuffering(double newSpeed, long curLatency)
    {
        if (speed == newSpeed)
            return;

        long curTicks = DateTime.UtcNow.Ticks;

        if (newSpeed != 1 && curTicks - lastSpeedChangeTicks < Config.Player.LatencySpeedChangeInterval)
            return;

        lastSpeedChangeTicks = curTicks;

        if (CanDebug) Log.Debug($"[Latency {curLatency/10000}ms] Speed changed x{speed} -> x{newSpeed}");

        //if (aFrame != null) AudioDecoder.FixSample(aFrame, newSpeed); // Requires lock
        speed = AudioDecoder.Speed = VideoDecoder.Speed = newSpeed;
        startTicks  = curTime;
        sw.Restart();
    }

    long GetBufferedDuration() // No speed aware
        => renderer.FieldType != VideoFrameFormat.Progressive && Config.Video.DoubleRate ?
        (vFrames.Count + vPackets.Count) * renderer.VideoStream.FrameDuration2 :
        (vFrames.Count + vPackets.Count) * renderer.VideoStream.FrameDuration;

    void ScreamerVASDAudio()
    {
        long bufferTicks, delayTicks, elapsedTicks, waitTicks;
        long desyncMs       = 0;    // use Ms to avoid rescale inaccuracy
        long expectingPts   = NoTs; // Will be set on resync
        bool shouldResync   = true;
        
        const long MIN_PLAY_BUFFER  = 40_0000;  // Start fill (TBR: allow some space from MAX to avoid filling all time?*)
        const long MAX_PLAY_BUFFER  = 80_0000;  // Stop  fill (try to keep it low so we can easier switch speed?*)
        const long MIN_DEC_BUFFER   = 19_0000;  // Resync when enough decoded buffer (related to MaxAudioFrame, keep it low for now)
        const long MAX_DESYNC_MS    = 50;       // A small gap between frames can create audio desync (use Ms instead to allow small diff for rescale Tb inaccuracy)

        while (!stopScreamerVASDAudio)
        {
            if (aFrame == null)
                AudioDecoder.Frames.TryDequeue(out aFrame);

            if (aFrame == null)
            {
                if (decoderHasEnded)
                    break;

                Thread.Sleep(10);
                continue;
            }

            bufferTicks = Audio.GetBufferedDuration();
            
            if (!shouldResync)
            {
                if (bufferTicks > MIN_PLAY_BUFFER)      // Play Buffer has enough samples
                    Thread.Sleep(10);
                else if (bufferTicks < 3_0000 && Config.Player.MaxLatency == 0) // Low Play Buffer (risk of using latency on next buffer submit)
                    shouldResync = true;
                else if (bufferTicks <= MIN_PLAY_BUFFER)// Re-filling an already filled buffer | We consider continues (no waitTicks check) however desyncMs will catch it
                {
                    desyncMs += (aFrame.timestamp - expectingPts) / 10000;
                    FillBuffer();
                }
            }
            else
            {
                if (bufferTicks > 3_0000)
                {
                    Thread.Sleep(Math.Min((int)(bufferTicks / 10000), 10));
                    continue;
                }

                delayTicks  = Audio.GetDeviceDelay();
                elapsedTicks= (long)(sw.ElapsedTicks * SWFREQ_TO_TICKS);
                waitTicks   = (long)((aFrame.timestamp - startTicks) / speed) - (elapsedTicks + delayTicks); // TODO: crash on AllocateCircularBuffer

                if (Math.Abs(waitTicks) > 5_000_0000) // Far away
                {
                    // TBR: Infinite loop with AllowFindStreamInfo = false on HLS Live (FirstTimestamp different between A/V)
                    Log.Warn($"[A] Late Frame ({TicksToTimeMini(waitTicks)})");
                    requiresBuffering = true;
                    break;
                }
                else if (waitTicks > 10_0000) // Not yet
                    continue;
                else if (waitTicks < -10_0000) // Drop frames to get closer
                {
                    var dropsBefore = Audio.framesDropped;
                    Audio.framesDropped++;
                    while (AudioDecoder.Frames.TryDequeue(out aFrame) && aFrame != null)
                    {
                        Audio.framesDropped++;
                        waitTicks = (long)((aFrame.timestamp - startTicks) / speed) - (elapsedTicks + delayTicks);
                        if (waitTicks >= -10_0000)
                            break;
                    }

                    if (CanDebug) Log.Debug($"[A] Frames Dropped (Drops: {Audio.framesDropped - dropsBefore}, Total: {Audio.framesDropped})");

                    if (aFrame == null || Math.Abs(waitTicks) > 10_0000)
                        continue;
                }

                // Calc decoded duration (must be enough to avoid crackling) | TBR: lock with speed?
                var frames = AudioDecoder.Frames.ToArray();
                long decodedDuration = 0;
                for (int i = 0; i < frames.Length; i++)
                {
                    decodedDuration += (long)((frames[i].dataLen / 4) * Audio.Timebase);
                    if (decodedDuration > MIN_DEC_BUFFER)
                        break;
                }

                if (decodedDuration < MIN_DEC_BUFFER)
                {
                    if (CanDebug) Log.Debug($"[A] Resync requires more buffer ({TicksToTimeMini(decodedDuration)})");
                    continue;
                }

                shouldResync = false;

                // Small Adjustement (for early frames)
                if (waitTicks > 1_0000)
                    Thread.Sleep((int)(waitTicks / 10000));

                // TBR: We miss samples we add so GetBufferedDuration is not accurate (mainly with filters/speed etc... might happen?)
                // Ensure XAudio has empty buffer (we restart with device latency/delay)
                //if (bufferTicks > 0)
                //    while (Audio.GetBufferedDuration() > 0)
                //        Thread.Sleep(1);
                Audio.ClearBuffer();

                if (CanInfo) Log.Info($"[A] Resynced at {TicksToTimeMini(aFrame.timestamp)} [Diff: {TicksToTimeMini((long)((aFrame.timestamp - startTicks) / speed) - delayTicks)} | {TicksToTimeMini((long)(sw.ElapsedTicks * SWFREQ_TO_TICKS))}]");
                
                // Fill Enough Samples
                desyncMs = 0;
                FillBuffer();
            }

            void FillBuffer()
            {
                Audio.AddSamples(aFrame);
                expectingPts = aFrame.timestamp + (long)(speed * aFrame.dataLen / 4 * Audio.Timebase);
                while (AudioDecoder.Frames.TryDequeue(out aFrame) && aFrame != null && Audio.GetBufferedDuration() < MAX_PLAY_BUFFER)
                {
                    desyncMs += (aFrame.timestamp - expectingPts) / 10000;
                    Audio.AddSamples(aFrame);
                    expectingPts = aFrame.timestamp + (long)(speed * aFrame.dataLen / 4 * Audio.Timebase);

                    if (desyncMs > MAX_DESYNC_MS)
                        break;
                }

                if (desyncMs > MAX_DESYNC_MS)
                {
                    Log.Warn($"[A] Desynced ({TicksToTimeMini(desyncMs * 10000)})");
                    shouldResync = true;
                }
            }
        }
        
        isScreamerVASDAudio = false;
        Audio.ClearBuffer();

        if (CanInfo) Log.Info($"[A] Finished at {TicksToTimeMini(curTime)}");
    }
    void StartScreamerVASDAudio()
    {
        if (!Audio.isOpened)
            return;

        StopScreamerVASDAudio();
        isScreamerVASDAudio = true;
        Thread t = new(ScreamerVASDAudio) // try-catch
        {
            #if DEBUG
            Name            = $"[#{PlayerId}] [A] Playback",
            #endif
            IsBackground    = true
        };
        t.Start();
    }
    void StopScreamerVASDAudio()
    {
        stopScreamerVASDAudio = true;
        while(isScreamerVASDAudio)
            Thread.Sleep(2);

        stopScreamerVASDAudio = false;
    }

    #region Frame Statistics
    uint showFrameCount, framesFailed, framesDisplayed, framesDisplayedDwm, framesDisplayedDwmStart, framesDisplayedDwmEnd;
    internal void ResetFrameStats()
    {
        if (Config.Player.Stats)
        {
            /*Video.fpsCurrent = */showFrameCount = framesFailed = framesDisplayed = framesDisplayedDwm = framesDisplayedDwmEnd = 0;
            UI(() => /*Video.FPSCurrent = */Video.FramesDropped = Video.FramesDisplayed = 0);
        }
    }
    internal (uint, uint) FramesDisplayedDropped()
    {
        var presentCount = renderer.GetFrameStatistics().PresentCount;
        var framesDisplayedDwmCur = framesDisplayedDwm + SafeSubstract(presentCount, framesDisplayedDwmStart);
        return (framesDisplayedDwmCur + showFrameCount, SafeSubstract(framesFailed + framesDisplayed + 1, framesDisplayedDwmCur));
    }
    static uint SafeSubstract(uint a, uint b) => a >= b ? a - b : 0;

    // we consider those during status == Status.Playing (for Engine) | We could expose public those?
    //internal uint FramesDisplayedTotal  => renderer.GetStats().PresentCount;
    //internal uint FramesDisplayed       => framesDisplayedDwm + SafeSubstract(renderer.GetStats().PresentCount, framesDisplayedDwmStart) + showFrameCount;
    //internal uint FramesDropped         => SafeSubstract(framesFailed + framesDisplayed + 1, FramesDisplayed - showFrameCount); // +1 for Current
    #endregion
}
