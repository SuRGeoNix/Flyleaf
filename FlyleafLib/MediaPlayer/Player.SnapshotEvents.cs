namespace FlyleafLib.MediaPlayer;

public partial class Player
{
    /// <summary>Result of a file snapshot. Invoked on the caller's thread, usually a snapshot worker.</summary>
    public event EventHandler<SnapshotCompletedArgs> SnapshotCompleted;
}

public sealed class SnapshotCompletedArgs(string filename, string error) : EventArgs
{
    public string Filename { get; } = filename;
    public string Error { get; } = error;
    public bool Success => Error == null;
}
