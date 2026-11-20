using Flyleaf;

var folder = new Uri("file:///C:/Media/Lists/list.m3u8");
int assertions = 0;
void Check(bool value, string description)
{
    if (!value) throw new Exception(description);
    assertions++;
}
void Reject(Action action, string description)
{
    try { action(); } catch (FormatException) { assertions++; return; }
    throw new Exception(description);
}
var plain = FlyleafPlaylistSerializer.Read("#EXTM3U\n../movie.mp4\nhttps://example.com/live.m3u8?token=a\n", ".m3u8", folder);
Check(plain.Entries.Count == 2, "Plain M3U entries were lost.");
Check(plain.Entries[0].Input == @"C:\Media\movie.mp4", "Local relative reference was not resolved against the document.");
Check(plain.Entries[1].Input == "https://example.com/live.m3u8?token=a", "Media manifest URL/query was modified.");
var remote = FlyleafPlaylistSerializer.Read("#EXTINF:-1,Radio\n../stream?id=7\n", ".m3u", new Uri("https://example.com/lists/channels.m3u"));
Check(remote.Entries[0].Input == "https://example.com/stream?id=7", "Remote relative reference was not resolved.");
Check(remote.Entries[0].Title == "Radio", "EXTINF title was lost.");
var quoted = FlyleafPlaylistSerializer.Read("#EXTM3U\n#EXTINF:-1 tvg-name=\"One, Two\",Actual title, suffix\nmovie.mp4", ".m3u", folder);
Check(quoted.Entries[0].Title == "Actual title, suffix", "A comma in a quoted attribute corrupted the title.");
var hlsMedia = "#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nsegment.ts\n";
var hlsMaster = "\uFEFF#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nvariant.m3u8\n";
Check(FlyleafPlaylistSerializer.IsHls(hlsMedia) && FlyleafPlaylistSerializer.IsHls(hlsMaster), "HLS media/master detection failed.");
Reject(() => FlyleafPlaylistSerializer.Read(hlsMedia, ".m3u8", folder), "HLS segments became editable queue sources.");
Reject(() => FlyleafPlaylistSerializer.Read(hlsMaster, ".m3u", folder), "HLS variants became editable queue sources.");
var pls = FlyleafPlaylistSerializer.Read("[playlist]\nTitle2=Second\nFile2=two.mp3\nFile1=one.mp3\nTitle1=First\n", ".pls", folder);
Check(pls.Entries.Count == 2 && pls.Entries[0].Title == "First" && pls.Entries[1].Title == "Second", "PLS entries were not ordered by index.");
var native = new FlyleafPlaylistDocument { Title = "Δοκιμή", Entries = [new() { Input = @"C:\Media\movie.mp4", Title = "Film" }] };
var json = FlyleafPlaylistSerializer.Write(native);
var restored = FlyleafPlaylistSerializer.Read(json, ".fpls", folder);
Check(FlyleafPlaylistSerializer.NativeExtension == ".fpls"
    && FlyleafPlaylistSerializer.IsSupportedExtension(".FPLS")
    && !FlyleafPlaylistSerializer.IsSupportedExtension(".flyleafplaylist"), "Native playlist extension selection is incorrect.");
Reject(() => FlyleafPlaylistSerializer.Read(json, ".flyleafplaylist", folder), "The retired playlist extension was accepted.");
Check(restored.Title == native.Title && restored.Entries[0].Title == "Film", "Native metadata/policy round-trip failed.");
Check(!json.Contains("children\"") && !json.Contains("position"), "Native document includes runtime child/session state.");
Reject(() => FlyleafPlaylistSerializer.Read("{}", ".fpls", folder), "Missing schema fields were accepted.");
Reject(() => FlyleafPlaylistSerializer.Read("{\"version\":2,\"entries\":[]}", ".fpls", folder), "Unsupported native version was accepted.");
Reject(() => FlyleafPlaylistSerializer.Read("{\"version\":1,\"entries\":[{\"input\":\"\"}]}", ".fpls", folder), "An invalid native entry was accepted.");
var httpOptions = FlyleafPlaylistSerializer.Read("#EXTM3U\n#EXTINF:-1,ANT1\n#EXTVLCOPT:http-referrer=http://watch.antennaplus.gr\n#EXTVLCOPT:http-user-agent=Chrome\nhttps://example.com/Ant1.m3u8\n#EXTINF:-1,Next\nhttps://example.com/next.m3u8", ".m3u", folder);
Check(httpOptions.Entries[0].Referrer == "http://watch.antennaplus.gr" && httpOptions.Entries[0].UserAgent == "Chrome", "IPTV per-entry Referer/User-Agent options were lost.");
Check(httpOptions.Entries[1].Referrer == null && httpOptions.Entries[1].UserAgent == null, "HTTP options leaked into the next channel.");
var httpRoundTrip = FlyleafPlaylistSerializer.Read(FlyleafPlaylistSerializer.Write(httpOptions), ".fpls", folder);
Check(httpRoundTrip.Entries[0].UserAgent == "Chrome" && httpRoundTrip.Entries[0].Referrer == httpOptions.Entries[0].Referrer, "Native save lost HTTP entry options.");
var httpExport = FlyleafPlaylistSerializer.Read(FlyleafPlaylistSerializer.WriteM3U(httpOptions), ".m3u8", folder);
Check(httpExport.Entries[0].Referrer == httpOptions.Entries[0].Referrer && httpExport.Entries[0].UserAgent == "Chrome" && httpExport.Entries[1].UserAgent == null, "M3U export lost or leaked HTTP entry options.");
Reject(() => FlyleafPlaylistSerializer.Read("#EXTM3U\n#EXTVLCOPT:unknown-option=1\nmovie.mp4", ".m3u", folder), "Unknown per-entry options were silently discarded.");
Reject(() => FlyleafPlaylistSerializer.Write(new() { Entries = [new() { Input = "https://example.com", UserAgent = "Chrome\r\nInjected: header" }] }), "HTTP header line injection was accepted.");
var exported = FlyleafPlaylistSerializer.WriteM3U(native);
var imported = FlyleafPlaylistSerializer.Read(exported, ".m3u8", folder);
Check(imported.Entries[0].Input == native.Entries[0].Input && imported.Entries[0].Title == "Film", "M3U export/reimport failed.");
Check(imported.Entries[0].Title == "Film", "M3U lost the playlist title.");
var empty = FlyleafPlaylistSerializer.Read(FlyleafPlaylistSerializer.WriteM3U(new()), ".m3u8", folder);
Check(empty.Entries.Count == 0, "Empty M3U export/reimport failed.");
Reject(() => FlyleafPlaylistSerializer.Read("{\"version\":1,\"entries\":null}", ".fpls", folder), "A null entry collection was accepted.");
Reject(() => FlyleafPlaylistSerializer.WriteM3U(new() { Entries = [new() { Input = "one.mp4\ntwo.mp4" }] }), "An input line break injected a second M3U entry.");
var untitled = FlyleafPlaylistSerializer.Read("#EXTINF:-1,\nmovie.mp4", ".m3u", folder);
Check(untitled.Entries[0].Title == null, "An empty imported title suppressed the source's default name.");
// Nonexistent media paths must be rejected as media BEFORE any file IO. This
// guards the reported movie-as-text overflow without needing a multi-GB fixture.
var missingMovie = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mkv");
Reject(() => Flyleaf.PlaylistDocumentFiles.ReadTextAsync(missingMovie, default).GetAwaiter().GetResult(), "A movie reached playlist file IO.");
var playlistFixture = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".M3U8");
try
{
    File.WriteAllText(playlistFixture, "#EXTM3U\nmovie.mp4\n", System.Text.Encoding.Unicode);
    var loaded = Flyleaf.PlaylistDocumentFiles.ReadTextAsync(playlistFixture, default).GetAwaiter().GetResult();
    Check(loaded == "#EXTM3U\nmovie.mp4\n", "Validated playlist reading lost BOM/encoding handling.");
}
finally { File.Delete(playlistFixture); }
var largePlaylist = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".m3u");
try
{
    using (var sparse = File.Create(largePlaylist)) sparse.SetLength(Flyleaf.PlaylistDocumentFiles.MaximumDocumentBytes + 1L);
    Reject(() => Flyleaf.PlaylistDocumentFiles.ReadTextAsync(largePlaylist, default).GetAwaiter().GetResult(), "Oversized playlist was accumulated as text.");
}
finally { File.Delete(largePlaylist); }
using (var oversizedResponse = new NonSeekableDocument(Flyleaf.PlaylistDocumentFiles.MaximumDocumentBytes + 1))
    Reject(() => Flyleaf.PlaylistDocumentFiles.ReadTextAsync(oversizedResponse, default).GetAwaiter().GetResult(), "An oversized chunked response bypassed the limit.");
using (var latinResponse = new MemoryStream(System.Text.Encoding.Latin1.GetBytes("#EXTINF:-1,Niño\nmovie.mp4")))
    Check(Flyleaf.PlaylistDocumentFiles.ReadTextAsync(latinResponse, default, System.Text.Encoding.Latin1).GetAwaiter().GetResult().Contains("Niño"), "Declared response encoding was ignored.");
using (var canceled = new CancellationTokenSource())
using (var response = new MemoryStream([1, 2, 3]))
{
    canceled.Cancel();
    try { Flyleaf.PlaylistDocumentFiles.ReadTextAsync(response, canceled.Token).GetAwaiter().GetResult(); throw new Exception("Canceled playlist read completed."); }
    catch (OperationCanceledException) { assertions++; }
}
if (args.Length > 0)
{
    var realInput = FlyleafPlaylistSerializer.Read(File.ReadAllText(args[0]), ".m3u", new Uri("https://raw.githubusercontent.com/iptv-org/iptv/refs/heads/master/streams/gr.m3u"));
    Check(realInput.Entries.Count > 50 && realInput.Entries.Any(e => e.UserAgent == "Chrome" && e.Referrer == "http://watch.antennaplus.gr"), "The supplied Greek IPTV playlist did not import with its channel options.");
    Console.WriteLine($"Real IPTV playlist imported: {realInput.Entries.Count} entries.");
}
Console.WriteLine($"Playlist document checks passed: {assertions}");

sealed class NonSeekableDocument(int remaining) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int size = Math.Min(count, remaining); Array.Fill(buffer, (byte)'x', offset, size); remaining -= size; return size;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int size = Math.Min(buffer.Length, remaining); buffer.Span[..size].Fill((byte)'x'); remaining -= size; return ValueTask.FromResult(size);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
