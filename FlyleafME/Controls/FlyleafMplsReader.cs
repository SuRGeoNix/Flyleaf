using System.Buffers.Binary;
using System.Globalization;
using System.IO;

namespace Flyleaf.Controls.WPF;

/// <summary>Read only the main playback intervals and entry marks, not stream or navigation data.</summary>
internal static class FlyleafMplsReader
{
    internal sealed record Playlist(int Number, TimeSpan? Duration = null, int? Chapters = null, int? Angles = null)
    {
        public string Label => Number.ToString("D5", CultureInfo.InvariantCulture) + (Duration is { } time
            ? $" · {(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" + (Chapters is int count ? $" · {count} chapter marks" : "") + (Angles > 1 ? $" · {Angles} angles" : "")
            : " · Metadata unavailable");
    }

    internal static Playlist[] Scan(string root, CancellationToken cancellation)
    {
        var results = new List<Playlist>();
        string folder = Path.Combine(root, "BDMV", "PLAYLIST");
        if (!Directory.Exists(folder)) return [];
        foreach (var file in Directory.EnumerateFiles(folder, "*.mpls"))
        {
            cancellation.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length != 5 || !int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) continue;
            try
            {
                using var stream = File.OpenRead(file);
                if (stream.Length is < 20 or > 16 * 1024 * 1024) { results.Add(new(number)); continue; }
                byte[] data = new byte[(int)stream.Length];
                stream.ReadExactly(data);
                results.Add(Parse(number, data));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { results.Add(new(number)); }
        }
        cancellation.ThrowIfCancellationRequested();
        return results.OrderByDescending(item => item.Duration).ThenBy(item => item.Number).ToArray();
    }

    static int U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    static bool Section(ReadOnlySpan<byte> data, uint offset, int minimum, out int start, out int end)
    {
        start = end = 0;
        if (offset < 20 || offset > data.Length - 4) return false;
        start = (int)offset + 4;
        uint length = U32(data, (int)offset);
        if (length < minimum || length > data.Length - start) return false;
        end = start + (int)length;
        return true;
    }

    internal static Playlist Parse(int number, ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || !data[..4].SequenceEqual("MPLS"u8) ||
            !(data.Slice(4, 4).SequenceEqual("0100"u8) || data.Slice(4, 4).SequenceEqual("0200"u8) || data.Slice(4, 4).SequenceEqual("0300"u8))) return new(number);
        if (!Section(data, U32(data, 8), 6, out int start, out int end)) return new(number);
        int count = U16(data, start + 2), position = start + 6, angles = 1;
        if (count == 0) return new(number);
        var intervals = new (uint In, uint Out)[count];
        long ticks45k = 0;
        for (int i = 0; i < count; i++)
        {
            if (end - position < 2) return new(number);
            int length = U16(data, position); position += 2;
            if (length < 32 || length > end - position) return new(number);
            var item = data.Slice(position, length);
            uint first = U32(item, 12), last = U32(item, 16);
            if (last < first) return new(number);
            intervals[i] = (first, last);
            ticks45k += last - first;
            if ((item[10] & 0x10) != 0)
            {
                if (length < 34 || item[32] == 0 || 34 + (item[32] - 1) * 10 > length) return new(number);
                angles = Math.Max(angles, item[32]);
            }
            position += length;
        }
        // MPLS IN/OUT times use a 45 kHz clock. No media files need to be opened.
        var duration = TimeSpan.FromTicks(ticks45k / 45000 * TimeSpan.TicksPerSecond + ticks45k % 45000 * TimeSpan.TicksPerSecond / 45000);
        int? chapters = null;
        if (Section(data, U32(data, 12), 2, out start, out end))
        {
            int marks = U16(data, start);
            position = start + 2;
            if (marks <= (end - position) / 14)
            {
                chapters = 0;
                for (int i = 0; i < marks; i++, position += 14)
                {
                    if (data[position + 1] != 1) continue;
                    int reference = U16(data, position + 2);
                    uint timestamp = U32(data, position + 4);
                    if (reference < intervals.Length && timestamp >= intervals[reference].In && timestamp <= intervals[reference].Out) chapters++;
                }
            }
        }
        return new(number, duration, chapters, angles);
    }
}
