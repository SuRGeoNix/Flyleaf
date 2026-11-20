using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

using FlyleafLib;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

[Flags]
internal enum FolderMediaType
{
    None = 0,
    Audio = 1,
    Video = 2,
    Images = 4,
    Animations = 8,
    All = Audio | Video | Images | Animations
}
internal enum FolderSort { Explorer, NameAscending, NameDescending, NumberAscending, NumberDescending, ModifiedNewest, ModifiedOldest, SizeLargest, SizeSmallest }

internal sealed record FolderImportOptions(FolderMediaType MediaType, bool IncludeSubfolders, FolderSort Sort);
internal sealed record FolderImportResult(string[] Paths, int SkippedFolders, bool UsedNameFallback);

internal static class FolderQueueImport
{
    static readonly IComparer<string> names = new Utils.NaturalStringComparer();

    internal static Task<FolderImportResult> ScanAsync(string folder, FolderImportOptions options, CancellationToken cancellation)
    {
        if (options.Sort != FolderSort.Explorer)
            return Task.Run(() => Scan(folder, options, null, cancellation), cancellation);

        // Explorer's automation objects belong to an STA. Keep Shell calls and disk IO off the UI thread.
        var completion = new TaskCompletionSource<FolderImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var views = ExplorerFolderOrder.Capture(folder, options.IncludeSubfolders, cancellation);
                completion.SetResult(Scan(folder, options, views, cancellation, ExplorerFolderOrder.UsesLiteralNameOrder()));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { completion.SetCanceled(cancellation); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static FolderImportResult Scan(string folder, FolderImportOptions options,
        IReadOnlyDictionary<string, Dictionary<string, int>>? explorerViews, CancellationToken cancellation,
        bool explorerUsesLiteralNames = false)
    {
        bool explorerSort = options.Sort == FolderSort.Explorer;
        var explorerNames = explorerSort && explorerUsesLiteralNames ? StringComparer.CurrentCultureIgnoreCase : names;
        var root = new DirectoryInfo(folder);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        var paths = new List<string>();
        int skipped = 0;
        bool nameFallback = false;
        var enumeration = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };

        while (pending.TryPop(out var directory))
        {
            cancellation.ThrowIfCancellationRequested();
            var files = new List<FileInfo>();
            var subfolders = new List<DirectoryInfo>();
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos("*", enumeration))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry is FileInfo file && Matches(file, options.MediaType))
                        files.Add(file);
                    else if (options.IncludeSubfolders && entry is DirectoryInfo subfolder &&
                        (subfolder.Attributes & FileAttributes.ReparsePoint) == 0)
                        subfolders.Add(subfolder);
                }
            }
            catch (Exception ex) when (directory != root && ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }

            var view = explorerViews?.GetValueOrDefault(Path.TrimEndingDirectorySeparator(directory.FullName));
            int Rank(FileSystemInfo item) => view?.GetValueOrDefault(item.FullName, int.MaxValue) ?? int.MaxValue;
            var sorted = options.Sort switch
            {
                FolderSort.NameDescending => files.OrderByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase),
                FolderSort.NumberAscending or FolderSort.NumberDescending => FlyleafNameSorting.OrderByNumber(files,
                    file => file.Name, options.Sort == FolderSort.NumberDescending),
                FolderSort.ModifiedNewest => files.OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, names),
                FolderSort.ModifiedOldest => files.OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, names),
                FolderSort.SizeLargest => files.OrderByDescending(file => file.Length).ThenBy(file => file.Name, names),
                FolderSort.SizeSmallest => files.OrderBy(file => file.Length).ThenBy(file => file.Name, names),
                FolderSort.Explorer => files.OrderBy(Rank).ThenBy(file => file.Name, explorerNames),
                _ => files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            };
            foreach (var file in sorted.ThenBy(file => file.Name, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                paths.Add(file.FullName);
                if (explorerSort && Rank(file) == int.MaxValue) nameFallback = true;
            }

            // Visit the parent first, then its subfolders in their display order when available.
            foreach (var subfolder in subfolders.OrderBy(Rank).ThenBy(item => item.Name, explorerNames)
                .ThenBy(item => item.Name, StringComparer.Ordinal).Reverse())
            {
                if (explorerSort && Rank(subfolder) == int.MaxValue) nameFallback = true;
                pending.Push(subfolder);
            }
        }

        return new(paths.ToArray(), skipped, nameFallback);
    }

    static bool Matches(FileInfo file, FolderMediaType filter)
    {
        var extension = file.Extension.TrimStart('.').ToLowerInvariant();
        if (MediaExtensions.Audio.Contains(extension))
            return filter.HasFlag(FolderMediaType.Audio);
        if (MediaExtensions.Video.Contains(extension))
            return filter.HasFlag(FolderMediaType.Video);

        const FolderMediaType pictures = FolderMediaType.Images | FolderMediaType.Animations;
        if ((filter & pictures) == FolderMediaType.None || !MediaExtensions.Images.Contains(extension))
            return false;
        if (filter.HasFlag(pictures)) return true;

        bool animated = extension switch
        {
            "apng" => true,
            "gif" or "webp" => IsAnimated(file, extension),
            _ => false
        };
        return filter.HasFlag(animated ? FolderMediaType.Animations : FolderMediaType.Images);
    }

    static bool IsAnimated(FileInfo file, string extension)
    {
        try
        {
            using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (extension == "gif")
                return new GifBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand).Frames.Count > 1;

            Span<byte> header = stackalloc byte[21];
            return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
                && header[..4].SequenceEqual("RIFF"u8) && header[8..16].SequenceEqual("WEBPVP8X"u8) && (header[20] & 0x02) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or COMException)
        {
            return false;
        }
    }
}
