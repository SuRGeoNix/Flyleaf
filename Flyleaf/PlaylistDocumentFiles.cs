using System.IO;

namespace Flyleaf;

internal static class PlaylistDocumentFiles
{
    internal const int MaximumDocumentBytes = 16 * 1024 * 1024;

    internal static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        // Validate before opening/reading: a movie selected in a playlist dialog
        // must never be accumulated in memory as playlist text.
        if (!FlyleafPlaylistSerializer.IsSupportedExtension(Path.GetExtension(path)))
            throw new FormatException("Choose a .fpls, .m3u, .m3u8 or .pls playlist. To play media files, use Open media or Queue → Add files, then Play.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadTextAsync(stream, cancellationToken);
    }

    internal static async Task<string> ReadTextAsync(Stream source, CancellationToken cancellationToken, System.Text.Encoding? encoding = null)
    {
        // Bound both files and chunked HTTP responses before decoding/accumulating text.
        if (source.CanSeek && source.Length - source.Position > MaximumDocumentBytes) throw TooLarge();
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaximumDocumentBytes - bytes.Length + 1)), cancellationToken);
            if (count == 0) break;
            if (bytes.Length + count > MaximumDocumentBytes) throw TooLarge();
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, encoding ?? System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    internal static FormatException TooLarge() => new("Playlist documents are limited to 16 MiB. Choose a smaller playlist document; media files should be opened as media sources.");
}
