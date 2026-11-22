using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace FlyleafLib.MediaPlayer;

/// <summary>Results associated with an individual seek operation.</summary>
public readonly record struct FlyleafSeekResult(int Milliseconds, bool Cancelled);

public unsafe partial class Player
{
    long openCancellationVersion;
    long seekRequestVersion;

    long OpenCancellationVersion => Volatile.Read(ref openCancellationVersion);

    bool IsOpenCancelled(string error, long version)
        => error != null && (error == "Cancelled" || version != OpenCancellationVersion);

    void FinalizeOpenCancellation(ref string error, long version)
    {
        if (!IsOpenCancelled(error, version))
            return;

        error = "Cancelled";
        if (status != Status.Stopped || lastError != null)
        {
            status = Status.Stopped;
            lastError = null;
            UI(() => { LastError = lastError; Status = status; });
        }
    }

    /// <summary>Whether an open-completion result represents an expected cancellation.</summary>
    public bool WasOpenCancelled(object result) => result switch
    {
        OpenCompletedArgs r => r.Error == "Cancelled",
        OpenSessionCompletedArgs r => r.Error == "Cancelled",
        OpenPlaylistItemCompletedArgs r => r.Error == "Cancelled",
        StreamOpenedArgs r => r.Error == "Cancelled",
        ExternalStreamOpenedArgs r => r.Error == "Cancelled",
        _ => false
    };

    void NotifySeekResult(int milliseconds, SeekData request)
    {
        bool cancelled = milliseconds < 0 && request != null &&
            (request.Version != Volatile.Read(ref seekRequestVersion) ||
             request.OpenVersion != OpenCancellationVersion ||
             status is Status.Stopped or Status.Opening);

        SeekResultCompleted?.Invoke(this, new(milliseconds, cancelled));
        SeekCompleted?.Invoke(this, milliseconds);
    }

    /// <summary>Raised when an explicit open interruption is requested, allowing UI consumers to clear pending operation feedback.</summary>
    public event EventHandler OperationCancellationRequested;

    /// <summary>
    /// Requests cancellation of the current open or seek operation without
    /// stopping playback or resetting the player.
    /// Cancellation is asynchronous and completion events are still raised.
    /// </summary>
    public void CancelCurrentOperation()
    {
        if (IsDisposed) return;
        Interlocked.Increment(ref openCancellationVersion);
        decoder.Interrupt = true;
        OperationCancellationRequested?.Invoke(this, EventArgs.Empty);
    }
}
