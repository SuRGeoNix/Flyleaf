namespace Flyleaf.Controls.WPF;

/// <summary>Playback input supplied by an application, independent of any saved document format.</summary>
public sealed class FlyleafQueueEntry
{
    public string Input { get; set; } = "";
    public string? Title { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
}