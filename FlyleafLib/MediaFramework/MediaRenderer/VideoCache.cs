using FlyleafLib.MediaFramework.MediaFrame;

using static FlyleafLib.Config;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public class VideoCache
{
    public long         Count       { get { lock (this) return Last == null ? 0 : Last.Id + 1 - First.Id; } }
    //public long         CountFront  => Current == null ? 0 : Last.Id - Current.Id;
    public bool         IsEmpty     => Count == 0;
    public VideoFrame   Next        => Current?.Next;

    DecoderConfig       dcfg;
    internal VideoFrame RendererFrame, First, Last, Current;
    long                curId;
    bool                isCurrentNext;
    bool                disposeRenderFrame;
    readonly HashSet<long> skippedFEL = []; // Never render BL without its EL.

    internal VideoCache(DecoderConfig dcfg)
        => this.dcfg = dcfg;

    public void Enqueue(VideoFrame frame, bool used = false)
    {   // Called by Decoder & ShowFrame (used = true to force next as current)
        lock (this)
        {
            frame.Id = curId++;

            if (Last == null)
            {
                isCurrentNext = !used;
                First = Current = Last = frame;
                //Log($"E: [{First.Id} {Last.Id}] {(Current != null ? Current.Id : "-")}");
            }
            else
            {
                frame.Prev  = Last;
                Last.Next   = frame;
                Last        = frame;

                if (Current == null || used)
                {
                    isCurrentNext = !used;
                    Current = Last;

                    while (Current.Id - First.Id > dcfg.MaxVideoFramesPrev)
                        { Shrink(); if (First == null) break; }

                    //if (First != null) Log($"E: [{First.Id} {Last.Id}] {(Current != null ? Current.Id : "-")} *");
                }
                //else
                //    Log($"E: [{First.Id} {Last.Id}] {(Current != null ? Current.Id : "-")}");
            }
        }
    }

    public void PushCurrentToLast()
    {   // For Enqueues that don't check MaxVideoFrames (forces shrink)
        lock (this)
        {
            Current = null;
            while (Last != null && Last.Id - First.Id > dcfg.MaxVideoFramesPrev - 1)
                Shrink();
        }
    }

    public bool TryDequeue(out VideoFrame frame)
    {
        lock (this)
        {
            while (Current != null)
            {
                var next = isCurrentNext ? Current : Current.Next;
                if (next == null || (!next.isReady && !skippedFEL.Contains(next.Id)))
                    break;

                if (isCurrentNext)
                    isCurrentNext = false;
                else
                {
                    var id = Current.Id;
                    Current = next;

                    while (id - First.Id > dcfg.MaxVideoFramesPrev - 1)
                        { Shrink(); if (First == null) break; }
                }

                if (skippedFEL.Contains(Current.Id))
                    continue; // A failed EL match must never reach the renderer.

                frame = Current;
                return true;
            }

            frame = null;
            return false;
        }
    }

    void Shrink()
    {
        var first   = First;
        skippedFEL.Remove(first.Id);
        First       = First.Next;

        if (First == null)
            Last = null;
        else
            First.Prev = null;

        if (first  != RendererFrame)
            first.Dispose();
        else
            disposeRenderFrame = true;
    }

    public void SetRendererFrame(VideoFrame frame)
    {
        lock (this)
        {
            if (RendererFrame != null && disposeRenderFrame)
                RendererFrame.Dispose();

            disposeRenderFrame = false;
            RendererFrame = frame;
        }
    }

    // During seek recovery, keep recent BL frames without blocking the decoder.
    internal bool DropOldestFELPreroll()
    {
        lock (this)
        {
            if (First == null || First.isReady)
                return false;

            DropFirstFEL();
            return true;
        }
    }

    // A matching EL has established the first usable post-seek picture.
    internal void DiscardFELPreroll()
    {
        lock (this)
            while (First != null && !First.isReady)
                DropFirstFEL();
    }

    void DropFirstFEL()
    {
        if (Current == First)
        {
            Current = First.Next;
            isCurrentNext = true;
        }

        Shrink();
    }

    // While aligned, any unready BL at the playback cursor signals a stalled pair.
    // It is not safe to skip a single frame and continue ordinal pairing.
    internal bool HasBlockedFEL()
    {
        lock (this)
        {
            var next = isCurrentNext ? Current : Current?.Next;
            return next != null && !next.isReady && !skippedFEL.Contains(next.Id);
        }
    }

    // Only for EOF or loss of synchronization: these BL frames must never render.
    internal void SkipUnmatchedFEL()
    {
        lock (this)
            for (var cur = First; cur != null; cur = cur.Next)
                if (!cur.isReady)
                    skippedFEL.Add(cur.Id);
    }

    // Pair the next decoded EL with the oldest waiting BL in presentation order.
    unsafe internal bool MergeNextELFrame(AVFrame* elFrame)
    {
        lock (this)
        {
            for (var cur = First; cur != null; cur = cur.Next)
            {
                if (cur.isReady || skippedFEL.Contains(cur.Id))
                    continue;

                cur.AVFrameFEL = elFrame;
                cur.isReady = true;
                return true;
            }

            return false;
        }
    }

    // The first post-flush EL random-access picture anchors both output sequences.
    // Only this anchor uses packet identity; B-frame output must use FIFO thereafter.
    unsafe internal bool MergeELAnchor(AVFrame* elFrame)
    {
        lock (this)
        {
            for (var cur = First; cur != null; cur = cur.Next)
            {
                if (cur.isReady || skippedFEL.Contains(cur.Id) || cur.AVFrame->opaque != elFrame->opaque)
                    continue;

                // Preserve any already completed frames during a midstream resync.
                // Unmatched preroll must be skipped, never rendered as BL-only.
                for (var prev = First; prev != cur; prev = prev.Next)
                    if (!prev.isReady)
                        skippedFEL.Add(prev.Id);

                cur.AVFrameFEL = elFrame;
                cur.isReady = true;
                return true;
            }

            return false;
        }
    }

    public void Reset()
    {
        lock (this)
        {
            var cur = First;
            First = Current = Last = null;
            skippedFEL.Clear();

            while (cur != null)
            {
                var next = cur.Next;
                if (cur != RendererFrame)
                    cur.Dispose();
                else
                    disposeRenderFrame = true;
                cur = next;
            }
        }
    }

    public void Dispose()
    {
        lock (this)
        {
            var cur = First;
            First = Current = Last = null;
            skippedFEL.Clear();

            while (cur != null)
            {
                var next = cur.Next;
                cur.Dispose();
                cur = next;
            }

            if (RendererFrame != null)
            {
                RendererFrame.Dispose();
                RendererFrame = null;
            }

            disposeRenderFrame = false;
        }
    }
}


