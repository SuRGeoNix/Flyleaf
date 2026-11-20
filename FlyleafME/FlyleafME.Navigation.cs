using System.Windows;

namespace Flyleaf.Controls.WPF;

/// <summary>Lets a host provide an album/folder/custom sequence instead of the default queue navigation.</summary>
public sealed class FlyleafItemNavigationEventArgs(int offset) : EventArgs
{
    public int Offset { get; } = offset;
    public bool Handled { get; set; }
}

public partial class FlyleafME
{
    public static readonly DependencyProperty ItemNavigationLargeStepProperty = DependencyProperty.Register(
        nameof(ItemNavigationLargeStep), typeof(int), typeof(FlyleafME), new PropertyMetadata(10), value => (int)value is >= 1 and <= 1000);
    public int ItemNavigationLargeStep { get => (int)GetValue(ItemNavigationLargeStepProperty); set => SetValue(ItemNavigationLargeStepProperty, value); }
    public event EventHandler<FlyleafItemNavigationEventArgs>? ItemNavigationRequested;
    public void PreviousItem() => NavigateItems(-1);
    public void NextItem() => NavigateItems(1);
    public void PreviousItems() => NavigateItems(-ItemNavigationLargeStep);
    public void NextItems() => NavigateItems(ItemNavigationLargeStep);

    public void NavigateItems(int offset)
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (offset == 0) return;
        var request = new FlyleafItemNavigationEventArgs(offset); ItemNavigationRequested?.Invoke(this, request);
        if (request.Handled) return;
        if (Queue.Sources.Count > 0) { Queue.NavigateItems(offset); return; }
        if (Player is not { IsDisposed: false } player) return;
        var items = player.Playlist.Items.ToArray(); int current = Array.IndexOf(items, player.Playlist.Selected);
        int target = FlyleafQueue.NavigationIndex(current, items.Length, offset, repeat: false);
        if (target >= 0 && target != current) player.OpenAsync(items[target]);
    }
}
