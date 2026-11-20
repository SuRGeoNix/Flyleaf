using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Per-item timing, independent of the dispatcher and decoder.</summary>
internal sealed class FlyleafQueueProgression
{
    double elapsedMilliseconds;
    bool timedPlayback, advanced;
    internal bool TimedPlayback => timedPlayback;
    internal void Reset() { elapsedMilliseconds = 0; timedPlayback = advanced = false; }

    internal bool Update(double elapsed, int interval, Status status, long duration, long currentTime,
        bool imageSource, bool liveSource, bool enabled, bool blocked, bool singleFrameImage = false)
    {
        // image2 can assign a still JPEG one synthetic frame of duration (e.g. 40 ms).
        // An actual progressing clock still wins, even for an estimated one-frame input.
        timedPlayback |= currentTime > 0 || duration > 0 && !singleFrameImage;
        if (advanced || !enabled || blocked || liveSource && !imageSource ||
            status is not (Status.Playing or Status.Ended)) return false;

        elapsedMilliseconds += Math.Max(0, elapsed);
        // Moving media always completes naturally. Only zero-time image inputs
        // may advance without Ended; the format alone never proves a still image.
        bool next = timedPlayback ? status == Status.Ended
            : (imageSource || status == Status.Ended) && elapsedMilliseconds >= interval;
        if (next) advanced = true;
        return next;
    }

    internal static bool IsImageFormat(string? format) =>
        format != null && (format.EndsWith("_pipe", StringComparison.Ordinal) ||
            format is "image2" or "image2pipe" or "gif" or "apng" or "ico" or "webp_anim");
}