using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDemuxer;

public unsafe class Interrupter
{
    public int          ForceInterrupt  { get; set; }
    public Demuxer      Demuxer         { get; private set; }

    public Requester    Requester       { get; private set; }
    public int          Interrupted     { get; private set; }

    public AVIOInterruptCB_callback_func GetCallBackFunc() { return interruptClbk; }
    AVIOInterruptCB_callback_func   interruptClbk = new AVIOInterruptCB_callback_func();     
    AVIOInterruptCB_callback        InterruptClbk = (opaque) =>
    {
        GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
        Demuxer demuxer = (Demuxer)demuxerHandle.Target;

        return demuxer.Interrupter.ShouldInterrupt(demuxer);
    };
    Stopwatch sw = new();

    public int ShouldInterrupt(Demuxer demuxer)
    {
        if (demuxer.Status == Status.Stopping)
        {
            if (CanDebug) demuxer.Log.Debug($"{demuxer.Interrupter.Requester} Interrupt (Stopping) !!!");
            
            return demuxer.Interrupter.Interrupted = 1;
        }

        if (demuxer.Config.AllowTimeouts)
        {
            long curTimeout = 0;
            switch (demuxer.Interrupter.Requester)
            {
                case Requester.Close:
                    curTimeout = demuxer.Config.CloseTimeout;
                    break;

                case Requester.Open:
                    curTimeout = demuxer.Config.OpenTimeout;
                    break;

                case Requester.Read:
                    curTimeout = demuxer.Config.ReadTimeout;
                    break;

                case Requester.Seek:
                    curTimeout = demuxer.Config.SeekTimeout;
                    break;
            }

            if (sw.ElapsedTicks > curTimeout)
            {
                demuxer.OnTimedOut();

                // Prevent Live Streams from Timeout (while demuxer is at the end)
                if (demuxer.Interrupter.Requester == Requester.Read && (demuxer.Duration == 0 || (demuxer.HLSPlaylist != null && demuxer.HLSPlaylist->cur_seq_no > demuxer.HLSPlaylist->last_seq_no - 2)))
                {
                    // TBR: Add retries (per input? per thread start?) as it can actually ended and keep reading forever
                    if (CanTrace) demuxer.Log.Trace($"{demuxer.Interrupter.Requester} Timeout !!!! {sw.ElapsedTicks / 10000} ms | Live HLS Excluded");

                    demuxer.Interrupter.Request(Requester.Read);

                    return demuxer.Interrupter.Interrupted = 0;
                }

                if (CanWarn) demuxer.Log.Warn($"{demuxer.Interrupter.Requester} Timeout !!!! {sw.ElapsedTicks / 10000} ms");

                return demuxer.Interrupter.Interrupted = 1;
            }
        }

        if (demuxer.Interrupter.Requester == Requester.Close) return 0;

        if (demuxer.Interrupter.ForceInterrupt != 0 && demuxer.allowReadInterrupts)
        {
            if (CanTrace) demuxer.Log.Trace($"{demuxer.Interrupter.Requester} Interrupt !!!");
            return demuxer.Interrupter.Interrupted = 1;
        }

        return demuxer.Interrupter.Interrupted = 0;
    }

    public Interrupter(Demuxer demuxer)
    {
        Demuxer = demuxer;
        interruptClbk.Pointer = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
    }

    public void Request(Requester requester)
    {
        if (!Demuxer.Config.AllowTimeouts) return;

        Requester = requester;
        sw.Restart();
    }
}

public enum Requester
{
    Close,
    Open,
    Read,
    Seek
}
