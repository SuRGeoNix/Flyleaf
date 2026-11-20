using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared choices and selection actions for bar menus and runtime panels.</summary>
internal static class FlyleafTrackMenus
{
    internal static void AddVideoTracks(ItemsControl menu, Player player, Func<bool> isCurrent, Action? opening = null)
    {
        var source = player.Playlist.Selected;
        var embedded = player.Video.Streams;
        if (embedded.Count == 0 && source is not { ExternalVideoStreams.Count: > 0 }) return;
        if (menu.HasItems) FlyleafMenuItems.Separator(menu);

        void Add(ItemsControl target, string label, bool selected, Action open) => AddTrack(target, label, selected, () =>
        {
            if (player.IsDisposed || !isCurrent() || !ReferenceEquals(source, player.Playlist.Selected)) return;
            opening?.Invoke(); open();
        });
        foreach (var stream in embedded)
            Add(menu, VideoTrackLabel((stream.StreamIndex + 1).ToString(), stream.Height, stream.Codec, title: stream.Title),
                player.Video.IsOpened && stream.Enabled, () => player.OpenAsync(stream));

        if (source is not { ExternalVideoStreams.Count: > 0 }) return;
        var external = FlyleafMenuItems.Group(menu, "External");
        foreach (var stream in source.ExternalVideoStreams)
        {
            var label = VideoTrackLabel($"Track {stream.Index + 1}", stream.Height, stream.Codec, protocol: stream.Protocol);
            Add(external, label, player.Video.IsOpened && ReferenceEquals(source.ExternalVideoStream, stream),
                () => player.OpenAsync(stream));
        }
    }

    internal static void AddAudioTracks(ItemsControl menu, Player player, Func<bool> isCurrent, Action? opening = null)
    {
        var source = player.Playlist.Selected;
        var embedded = player.Audio.Streams;
        if (embedded.Count == 0 && source is not { ExternalAudioStreams.Count: > 0 }) return;
        if (menu.HasItems) FlyleafMenuItems.Separator(menu);

        void Add(ItemsControl target, string label, bool selected, Action open) => AddTrack(target, label, selected, () =>
        {
            if (player.IsDisposed || !isCurrent() || !ReferenceEquals(source, player.Playlist.Selected)) return;
            opening?.Invoke(); open();
        });
        foreach (var stream in embedded)
            Add(menu, TrackLabel((stream.StreamIndex + 1).ToString(), stream.Language, stream.Title, stream.Codec),
                player.Audio.IsOpened && stream.Enabled, () => player.OpenAsync(stream));

        if (source is not { ExternalAudioStreams.Count: > 0 }) return;
        var external = FlyleafMenuItems.Group(menu, "External");
        foreach (var stream in source.ExternalAudioStreams)
            Add(external, TrackLabel($"Track {stream.Index + 1}", stream.Language, null, stream.Codec),
                player.Audio.IsOpened && ReferenceEquals(source.ExternalAudioStream, stream), () => player.OpenAsync(stream));
    }

    internal static void AddSubtitleTracks(ItemsControl menu, Player player, Func<bool> isCurrent, Action? opening = null)
    {
        var source = player.Playlist.Selected;
        var embedded = player.Subtitles.Streams;
        if (embedded.Count == 0 && source is not { ExternalSubtitlesStreams.Count: > 0 }) return;
        if (menu.HasItems) FlyleafMenuItems.Separator(menu);

        void Add(ItemsControl target, string label, bool selected, Action open) => AddTrack(target, label, selected, () =>
        {
            if (player.IsDisposed || !isCurrent() || !ReferenceEquals(source, player.Playlist.Selected) || !player.Video.IsOpened) return;
            opening?.Invoke(); open();
        });
        foreach (var stream in embedded)
            Add(menu, TrackLabel((stream.StreamIndex + 1).ToString(), stream.Language, stream.Title, stream.Codec),
                player.Subtitles.IsOpened && stream.Enabled, () => player.OpenAsync(stream));

        if (source is not { ExternalSubtitlesStreams.Count: > 0 }) return;
        var external = FlyleafMenuItems.Group(menu, "External");
        foreach (var stream in source.ExternalSubtitlesStreams)
            Add(external, TrackLabel($"Track {stream.Index + 1}", stream.Language, stream.Title, stream.Codec),
                player.Subtitles.IsOpened && ReferenceEquals(source.ExternalSubtitlesStream, stream), () => player.OpenAsync(stream));
    }

    internal static string VideoTrackLabel(string index, long height, string? codec, string? title = null, string? protocol = null)
        => string.Join(" · ", new[] { index, height > 0 ? $"{height}p" : null, protocol, codec, title }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    internal static string TrackLabel(string? index, Language? language, string? title, string? codec)
    {
        string? name = language?.ToString();
        if (string.IsNullOrWhiteSpace(name)) name = "Unknown";
        return string.Join(" · ", new[] { index, name, title, codec }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    static void AddTrack(ItemsControl menu, string label, bool selected, Action action)
    {
        var item = new MenuItem { Header = label, IsChecked = selected };
        item.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ThemeMenuItemStyle");
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
